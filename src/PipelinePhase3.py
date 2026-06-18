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
            
        # --- PART 1: Floors (Top-Face Extraction) ---
        print("Phase 3: Processing floors...")
        source_floors = rs.ObjectsByLayer(floor_source)
        level_elevations = []
        
        if source_floors:
            for oid in source_floors:
                is_closed = False
                if rs.IsPolysurface(oid) and rs.IsPolysurfaceClosed(oid): is_closed = True
                elif rs.IsMesh(oid) and rs.IsMeshClosed(oid): is_closed = True

                if is_closed:
                    new_id = rs.CopyObject(oid)
                    rs.ObjectLayer(new_id, floor_target)
                    bbox = rs.BoundingBox(new_id)
                    if bbox: level_elevations.append(bbox[6].Z)
                    continue
                
                # Extract Top Faces from open geometry
                bbox = rs.BoundingBox(oid)
                if not bbox: continue
                obj_max_z = bbox[6].Z
                
                brep = rs.coercebrep(oid)
                if not brep and rs.IsMesh(oid):
                    mesh = rs.coercemesh(oid)
                    if mesh:
                        brep = Rhino.Geometry.Brep.CreateFromMesh(mesh, True)
                        
                if not brep: continue
                
                top_face_loops = []
                for face in brep.Faces:
                    normal = face.NormalAt(face.Domain(0).Mid, face.Domain(1).Mid)
                    if normal.Z > 0.9: # Pointing up
                        face_bbox = face.GetBoundingBox(True)
                        if abs(face_bbox.Max.Z - obj_max_z) < 0.2: # Near the top
                            for loop in face.Loops:
                                if loop.LoopType == Rhino.Geometry.BrepLoopType.Outer:
                                    crv = loop.To3dCurve()
                                    if crv and crv.IsClosed:
                                        top_face_loops.append(crv)
                
                if top_face_loops:
                    # Project to exact max_z
                    proj_plane = Rhino.Geometry.Plane(Rhino.Geometry.Point3d(0,0,obj_max_z), Rhino.Geometry.Vector3d.ZAxis)
                    xform = Rhino.Geometry.Transform.PlanarProjection(proj_plane)
                    for c in top_face_loops: c.Transform(xform)
                    
                    unioned = Rhino.Geometry.Curve.CreateBooleanUnion(top_face_loops, sc.doc.ModelAbsoluteTolerance)
                    if unioned:
                        for u_crv in unioned:
                            planar = Rhino.Geometry.Brep.CreatePlanarBreps(u_crv, sc.doc.ModelAbsoluteTolerance)
                            if planar:
                                for p_br in planar:
                                    new_id = sc.doc.Objects.AddBrep(p_br)
                                    if new_id: rs.ObjectLayer(new_id, floor_target)
                                    level_elevations.append(obj_max_z)

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
                    if unioned:
                        for crv in unioned:
                            # Extrude DOWNWARDS
                            height = b[1] - b[0]
                            if height > 0.01:
                                extrusion = Rhino.Geometry.Extrusion.Create(crv, -height, True)
                                if extrusion:
                                    final_brep = extrusion.ToBrep()
                                    new_id = sc.doc.Objects.AddBrep(final_brep)
                                    if new_id: rs.ObjectLayer(new_id, wall_target)

        if rs.IsLayer("Analysis::Phase2"):
            rs.LayerVisible("Analysis::Phase2", False)
        
        elapsed = time.time() - start_time
        return f"Phase 3 Complete in {elapsed:.1f}s. Levels: {len(levels) if 'levels' in locals() else 0}"

    except Exception as e:
        import traceback
        return f"Error during Phase 3: {str(e)}\n{traceback.format_exc()}"
    finally:
        rs.EnableRedraw(True)

if __name__ == "__main__":
    print(run_phase_3())
