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
        print("Phase 3: Processing floors via border projection and unioning...")
        source_floors = rs.ObjectsByLayer(floor_source)
        level_elevations = []
        
        if source_floors:
            # Group by elevation (rounded to 2 decimal places for robustness)
            floor_bins = {}
            for oid in source_floors:
                bbox = rs.BoundingBox(oid)
                if not bbox: continue
                max_z = round(bbox[6].Z, 2)
                level_elevations.append(max_z)
                
                if max_z not in floor_bins: floor_bins[max_z] = []
                floor_bins[max_z].append(oid)

            for elevation, oids in floor_bins.items():
                print(f"  Processing Level {elevation}m ({len(oids)} objects)")
                level_breps = []
                max_thickness = 0.3
                
                for oid in oids:
                    bbox = rs.BoundingBox(oid)
                    if bbox:
                        thickness = bbox[6].Z - bbox[0].Z
                        if thickness > max_thickness: max_thickness = thickness

                    # Extract Top Outlines (Silhouette)
                    proj_plane = Rhino.Geometry.Plane(Rhino.Geometry.Point3d(0,0,elevation), Rhino.Geometry.Vector3d.ZAxis)
                    outline_curves = []
                    
                    geo = rs.coercegeometry(oid)
                    if not geo:
                        print(f"    ERROR: Could not coerce geometry for {oid}")
                        continue

                    if isinstance(geo, Rhino.Geometry.Mesh):
                        polys = geo.GetOutlines(proj_plane)
                        if polys:
                            for p in polys: outline_curves.append(p.ToPolylineCurve())
                    else:
                        # Convert to Brep if it's an Extrusion or other surface type
                        br = None
                        if isinstance(geo, Rhino.Geometry.Brep): br = geo
                        elif isinstance(geo, Rhino.Geometry.Extrusion): br = geo.ToBrep()
                        
                        if br:
                            silhouettes = Rhino.Geometry.Silhouette.Compute(br, Rhino.Geometry.SilhouetteType.Projected, proj_plane, 0.1, 0.01)
                            if silhouettes:
                                for s in silhouettes: outline_curves.append(s.Curve)
                            
                            if not outline_curves:
                                # FALLBACK 1: Naked Edges
                                naked = br.DuplicateNakedEdgeCurves()
                                if naked:
                                    xform = Rhino.Geometry.Transform.PlanarProjection(proj_plane)
                                    for c in naked:
                                        c.Transform(xform)
                                        outline_curves.append(c)
                    
                    if not outline_curves:
                        # FALLBACK 2: Bounding Box Rectangle (last resort)
                        if bbox:
                            rect = Rhino.Geometry.Rectangle3d(proj_plane, Rhino.Geometry.Interval(bbox[0].X, bbox[6].X), Rhino.Geometry.Interval(bbox[0].Y, bbox[6].Y))
                            outline_curves.append(rect.ToNurbsCurve())
                            print(f"    Warning: Used BBox fallback for {oid}")
                    
                    if outline_curves:
                        joined = Rhino.Geometry.Curve.JoinCurves(outline_curves, 0.1)
                        if joined:
                            for j_crv in joined:
                                if not j_crv.IsClosed:
                                    print(f"    WARNING: Open curve found for {oid}, attempting to close...")
                                    j_crv.MakeClosed(0.1)
                                
                                if j_crv.IsClosed:
                                    # Create individual 3D slab
                                extrusion = Rhino.Geometry.Extrusion.Create(j_crv, -max_thickness, True)
                                if extrusion:
                                    level_breps.append(extrusion.ToBrep())
                                else:
                                    planar = Rhino.Geometry.Brep.CreatePlanarBreps(j_crv, 0.01)
                                    if planar:
                                        for p_br in planar:
                                            path = Rhino.Geometry.LineCurve(Rhino.Geometry.Point3d(0,0,0), Rhino.Geometry.Point3d(0,0,-max_thickness))
                                            slab = p_br.Faces[0].CreateExtrusion(path, True)
                                            if slab: level_breps.append(slab)
                
                if level_breps:
                    # Perform 3D Boolean Union on all slabs for this level
                    final_breps = []
                    if len(level_breps) > 1:
                        unioned = Rhino.Geometry.Brep.CreateBooleanUnion(level_breps, sc.doc.ModelAbsoluteTolerance)
                        if unioned and len(unioned) > 0:
                            final_breps = unioned
                            print(f"    Success: Unioned {len(level_breps)} slabs into {len(unioned)} objects.")
                        else:
                            # FALLBACK: If boolean fails, keep the original pieces so nothing is lost
                            final_breps = level_breps
                            print(f"    Warning: Boolean Union failed for Level {elevation}m. Preserving {len(level_breps)} individual slabs.")
                    else:
                        final_breps = level_breps
                        
                    for fb in final_breps:
                        new_id = sc.doc.Objects.AddBrep(fb)
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

