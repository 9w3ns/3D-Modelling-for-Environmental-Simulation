import rhinoscriptsyntax as rs
import Rhino
import scriptcontext as sc
import time
import System

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
                    if not geo: continue

                    br = None
                    if isinstance(geo, Rhino.Geometry.Brep): br = geo
                    elif isinstance(geo, Rhino.Geometry.Extrusion): br = geo.ToBrep()
                    elif isinstance(geo, Rhino.Geometry.Mesh): br = Rhino.Geometry.Brep.CreateFromMesh(geo, True)
                    
                    if br:
                        silhouettes = Rhino.Geometry.Silhouette.Compute(br, Rhino.Geometry.SilhouetteType.Projecting, Rhino.Geometry.Vector3d.ZAxis, 0.1, 0.01)
                        if silhouettes:
                            xform = Rhino.Geometry.Transform.PlanarProjection(proj_plane)
                            for s in silhouettes:
                                if s.Curve:
                                    c = s.Curve
                                    c.Transform(xform)
                                    outline_curves.append(c)
                        
                        if not outline_curves:
                            # FALLBACK 1: Naked Edges
                            naked = br.DuplicateNakedEdgeCurves()
                            if naked:
                                xform = Rhino.Geometry.Transform.PlanarProjection(proj_plane)
                                for c in naked:
                                    c.Transform(xform)
                                    outline_curves.append(c)
                    
                    if not outline_curves and bbox:
                        # FALLBACK 2: Bounding Box Rectangle
                        rect = Rhino.Geometry.Rectangle3d(proj_plane, Rhino.Geometry.Interval(bbox[0].X, bbox[6].X), Rhino.Geometry.Interval(bbox[0].Y, bbox[6].Y))
                        outline_curves.append(rect.ToNurbsCurve())
                    
                    if outline_curves:
                        joined = Rhino.Geometry.Curve.JoinCurves(outline_curves, 0.1)
                        if joined:
                            for j_crv in joined:
                                if not j_crv.IsClosed: j_crv.MakeClosed(0.1)
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
                    # 3D Boolean Union
                    final_breps = []
                    if len(level_breps) > 1:
                        unioned = Rhino.Geometry.Brep.CreateBooleanUnion(level_breps, sc.doc.ModelAbsoluteTolerance)
                        final_breps = unioned if (unioned and len(unioned) > 0) else level_breps
                    else:
                        final_breps = level_breps
                        
                    for fb in final_breps:
                        fb.MergeCoplanarFaces(sc.doc.ModelAbsoluteTolerance)
                        for face in fb.Faces:
                            face_brep = face.DuplicateFace(False)
                            if face_brep and face_brep.IsValid:
                                new_id = sc.doc.Objects.AddBrep(face_brep)
                                if new_id: rs.ObjectLayer(new_id, floor_target)

        # --- PART 2: Walls (Oriented Bounding Box Union) ---
        print("Phase 3: Reconstructing walls via Oriented Bounding Box (OBB) union...")
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
            
            for b, walls in wall_bins.items():
                if not walls: continue
                print(f"  Level {b[0]}m -> {b[1]}m ({len(walls)} segments)")
                level_wall_boxes = []
                height = b[1] - b[0]
                
                for w_oid in walls:
                    geo = rs.coercegeometry(w_oid)
                    if not geo: continue
                    br = None
                    if isinstance(geo, Rhino.Geometry.Brep): br = geo
                    elif isinstance(geo, Rhino.Geometry.Extrusion): br = geo.ToBrep()
                    elif isinstance(geo, Rhino.Geometry.Mesh): br = Rhino.Geometry.Brep.CreateFromMesh(geo, True)
                    
                    if br:
                        # 1. Get footprint
                        proj_plane = Rhino.Geometry.Plane(Rhino.Geometry.Point3d(0,0,b[0]), Rhino.Geometry.Vector3d.ZAxis)
                        silhouettes = Rhino.Geometry.Silhouette.Compute(br, Rhino.Geometry.SilhouetteType.Projecting, Rhino.Geometry.Vector3d.ZAxis, 0.1, 0.01)
                        foot_curves = []
                        if silhouettes:
                            xf = Rhino.Geometry.Transform.PlanarProjection(proj_plane)
                            for s in silhouettes:
                                if s.Curve:
                                    c = s.Curve; c.Transform(xf); foot_curves.append(c)
                        
                        if not foot_curves:
                            naked = br.DuplicateNakedEdgeCurves()
                            if naked:
                                xf = Rhino.Geometry.Transform.PlanarProjection(proj_plane)
                                for c in naked: c.Transform(xf); foot_curves.append(c)
                        
                        if foot_curves:
                            joined = Rhino.Geometry.Curve.JoinCurves(foot_curves, 0.1)
                            if joined:
                                for j_crv in joined:
                                    if not j_crv.IsClosed: j_crv.MakeClosed(0.1)
                                    if j_crv.IsClosed:
                                        # 2. CREATE ORIENTED BOX
                                        planar = Rhino.Geometry.Brep.CreatePlanarBreps(j_crv, 0.1)
                                        if planar:
                                            face = planar[0].Faces[0]
                                            # Correctly unpack result from FrameAt
                                            success, plane = face.FrameAt(face.Domain(0).Mid, face.Domain(1).Mid)
                                            if success:
                                                obb = Rhino.Geometry.Box(plane, planar[0])
                                                if obb.IsValid:
                                                    # Force height to level height
                                                    obb.Z = Rhino.Geometry.Interval(0, height)
                                                    level_wall_boxes.append(obb.ToBrep())

                if level_wall_boxes:
                    # 4. Iterative Union
                    print(f"    Attempting union of {len(level_wall_boxes)} OBBs...")
                    unioned_walls = []
                    if len(level_wall_boxes) > 1:
                        current = level_wall_boxes[0]
                        remaining = level_wall_boxes[1:]
                        for i in range(len(remaining)):
                            u = Rhino.Geometry.Brep.CreateBooleanUnion([current, remaining[i]], sc.doc.ModelAbsoluteTolerance)
                            if u and len(u) > 0:
                                current = u[0]
                            else:
                                unioned_walls.append(current)
                                current = remaining[i]
                        unioned_walls.append(current)
                    else:
                        unioned_walls = level_wall_boxes
                        
                    for fw in unioned_walls:
                        if not fw or not fw.IsValid: continue
                        fw.MergeCoplanarFaces(sc.doc.ModelAbsoluteTolerance)
                        for face in fw.Faces:
                            face_brep = face.DuplicateFace(False)
                            if face_brep and face_brep.IsValid:
                                new_id = sc.doc.Objects.AddBrep(face_brep)
                                if new_id: rs.ObjectLayer(new_id, wall_target)

        if rs.IsLayer("Analysis::Phase2"): rs.LayerVisible("Analysis::Phase2", False)
        elapsed = time.time() - start_time
        print(f"Phase 3 Complete in {elapsed:.1f}s. Levels: {len(levels) if 'levels' in locals() else 0}")

    except Exception as e:
        import traceback
        print(f"Error during Phase 3: {str(e)}\n{traceback.format_exc()}")
    finally:
        rs.EnableRedraw(True)

run_phase_3()
