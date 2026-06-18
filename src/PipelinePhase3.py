import rhinoscriptsyntax as rs
import Rhino
import scriptcontext as sc
import time

def run_phase_3():
    # 1. Setup
    floor_source = "Analysis::Phase2::Floors"
    wall_source = "Analysis::Phase2::Walls"
    target_base = "Analysis::Phase3"
    
    floor_target = target_base + "::Floors"
    wall_target = target_base + "::Walls"
    
    rs.EnableRedraw(False)
    start_time = time.time()
    
    try:
        # Create Layers
        if not rs.IsLayer("Analysis"): rs.AddLayer("Analysis")
        if not rs.IsLayer(target_base): rs.AddLayer(target_base, (0,0,0), parent="Analysis")
        if not rs.IsLayer(floor_target): rs.AddLayer(floor_target, (255, 100, 100))
        if not rs.IsLayer(wall_target): rs.AddLayer(wall_target, (100, 100, 255))
        
        # Clear target layers
        for layer in [floor_target, wall_target]:
            old = rs.ObjectsByLayer(layer)
            if old: rs.DeleteObjects(old)
            
        # --- PART 1: Floors (Border Projection & Extrusion) ---
        print("Phase 3: Processing floors via border projection...")
        source_floors = rs.ObjectsByLayer(floor_source)
        level_elevations = []
        
        if source_floors:
            for oid in source_floors:
                is_closed = False
                if rs.IsPolysurface(oid) and rs.IsPolysurfaceClosed(oid): is_closed = True
                # We no longer consider closed meshes as "finished", force reconstruction to Brep

                bbox = rs.BoundingBox(oid)
                if not bbox: continue
                max_z = bbox[6].Z
                min_z = bbox[0].Z
                level_elevations.append(max_z)

                if is_closed:
                    new_id = rs.CopyObject(oid)
                    rs.ObjectLayer(new_id, floor_target)
                    continue
                
                # Calculate thickness
                thickness = max_z - min_z
                if thickness < 0.1: thickness = 0.3
                
                # Extract Top Outlines (Silhouette)
                proj_plane = Rhino.Geometry.Plane(Rhino.Geometry.Point3d(0,0,max_z), Rhino.Geometry.Vector3d.ZAxis)
                outline_curves = []
                
                if rs.IsMesh(oid):
                    m = rs.coercemesh(oid)
                    if m: 
                        polys = m.GetOutlines(proj_plane)
                        if polys:
                            for p in polys: outline_curves.append(p.ToPolylineCurve())
                else:
                    br = rs.coercebrep(oid)
                    if br:
                        silhouettes = Rhino.Geometry.Silhouette.Compute(br, Rhino.Geometry.SilhouetteType.Projected, proj_plane, 0.1, 0.01)
                        if silhouettes:
                            for s in silhouettes: outline_curves.append(s.Curve)
                
                if outline_curves:
                    joined = Rhino.Geometry.Curve.JoinCurves(outline_curves, 0.1)
                    if joined:
                        closed_curves = [c for c in joined if c.IsClosed]
                        if closed_curves:
                            unioned = Rhino.Geometry.Curve.CreateBooleanUnion(closed_curves, 0.1)
                            if unioned:
                                for crv in unioned:
                                    # Try simple extrusion first
                                    extrusion = Rhino.Geometry.Extrusion.Create(crv, -thickness, True)
                                    if extrusion:
                                        final_brep = extrusion.ToBrep()
                                        new_id = sc.doc.Objects.AddBrep(final_brep)
                                        if new_id: rs.ObjectLayer(new_id, floor_target)
                                    else:
                                        # Fallback to planar brep extrusion
                                        planar = Rhino.Geometry.Brep.CreatePlanarBreps(crv, 0.01)
                                        if planar:
                                            for p_br in planar:
                                                path = Rhino.Geometry.LineCurve(Rhino.Geometry.Point3d(0,0,0), Rhino.Geometry.Point3d(0,0,-thickness))
                                                slab = p_br.Faces[0].CreateExtrusion(path, True)
                                                if slab:
                                                    new_id = sc.doc.Objects.AddBrep(slab)
                                                    if new_id: rs.ObjectLayer(new_id, floor_target)

        # --- PART 2: Walls (Top-Face Outline Extrusion) ---
        print("Phase 3: Reconstructing walls from top-face outlines...")
        source_walls = rs.ObjectsByLayer(wall_source)
        
        if source_walls and level_elevations:
            # Prepare bins
            levels = sorted(list(set([round(z, 2) for z in level_elevations])))
            if min(levels) > 0.5: levels.insert(0, 0.0)
            
            bins = []
            for i in range(len(levels)):
                lower = levels[i]
                upper = levels[i+1] if i+1 < len(levels) else lower + 3.0
                bins.append((lower, upper))
            
            wall_bins = {b: [] for b in bins}
            for oid in source_walls:
                bbox = rs.BoundingBox(oid)
                if not bbox: continue
                z_mid = (bbox[0].Z + bbox[6].Z) / 2.0
                
                best_bin = bins[0]
                min_dist = 1e10
                for b in bins:
                    dist = abs(z_mid - (b[0] + b[1])/2.0)
                    if dist < min_dist:
                        min_dist = dist
                        best_bin = b
                wall_bins[best_bin].append(oid)
            
            # Process levels
            for b, walls in wall_bins.items():
                if not walls: continue
                print(f"  Level {b[0]}m -> {b[1]}m")
                
                bin_ceiling_z = b[1]
                top_outlines = []
                
                for w_oid in walls:
                    w_bbox = rs.BoundingBox(w_oid)
                    if not w_bbox: continue
                    w_max_z = w_bbox[6].Z
                    
                    brep = rs.coercebrep(w_oid)
                    if not brep and rs.IsMesh(w_oid):
                        mesh = rs.coercemesh(w_oid)
                        if mesh: brep = Rhino.Geometry.Brep.CreateFromMesh(mesh, True)
                    
                    if not brep: continue
                    
                    for face in brep.Faces:
                        normal = face.NormalAt(face.Domain(0).Mid, face.Domain(1).Mid)
                        if normal.Z > 0.9: # Pointing up
                            face_bbox = face.GetBoundingBox(True)
                            if abs(face_bbox.Max.Z - w_max_z) < 0.2: # Is a top face
                                for loop in face.Loops:
                                    if loop.LoopType == Rhino.Geometry.BrepLoopType.Outer:
                                        crv = loop.To3dCurve()
                                        if crv and crv.IsClosed:
                                            top_outlines.append(crv)
                
                if top_outlines:
                    # Project all to the bin ceiling
                    ceiling_plane = Rhino.Geometry.Plane(Rhino.Geometry.Point3d(0,0,bin_ceiling_z), Rhino.Geometry.Vector3d.ZAxis)
                    xform = Rhino.Geometry.Transform.PlanarProjection(ceiling_plane)
                    for c in top_outlines: c.Transform(xform)
                    
                    # Union the footprints
                    unioned = Rhino.Geometry.Curve.CreateBooleanUnion(top_outlines, sc.doc.ModelAbsoluteTolerance)
                    curves_to_extrude = unioned if unioned else top_outlines
                    for crv in curves_to_extrude:
                        # Extrude DOWNWARDS
                            height = b[1] - b[0]
                            if height > 0.01:
                                extrusion = Rhino.Geometry.Extrusion.Create(crv, -height, True)
                                if extrusion:
                                    final_brep = extrusion.ToBrep()
                                    new_id = sc.doc.Objects.AddBrep(final_brep)
                                    if new_id: rs.ObjectLayer(new_id, wall_target)
                                else:
                                    # Fallback extrusion
                                    planar = Rhino.Geometry.Brep.CreatePlanarBreps(crv, 0.01)
                                    if planar:
                                        for p_br in planar:
                                            path = Rhino.Geometry.LineCurve(Rhino.Geometry.Point3d(0,0,0), Rhino.Geometry.Point3d(0,0,-height))
                                            slab = p_br.Faces[0].CreateExtrusion(path, True)
                                            if slab:
                                                new_id = sc.doc.Objects.AddBrep(slab)
                                                if new_id: rs.ObjectLayer(new_id, wall_target)

        # Hide Phase 2
        if rs.IsLayer("Analysis::Phase2"):
            rs.LayerVisible("Analysis::Phase2", False)
        
        elapsed = time.time() - start_time
        print(f"Phase 3 Complete in {elapsed:.1f}s. Levels: {len(levels) if 'levels' in locals() else 0}")

    except Exception as e:
        import traceback
        print(f"Error during Phase 3: {str(e)}\n{traceback.format_exc()}")
    finally:
        rs.EnableRedraw(True)

run_phase_3()

