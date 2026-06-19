import rhinoscriptsyntax as rs
import Rhino
import scriptcontext as sc
import time
from collections import deque

seen_signatures = set()

def is_glass(layer_name):
    layer_name = layer_name.lower()
    return "glass" in layer_name or "glazing" in layer_name or "window" in layer_name

def process_geometry(geometry, xform, is_glass_override):
    bbox = geometry.GetBoundingBox(True)
    if not bbox.IsValid: return []
    
    dims = sorted([bbox.Max.X - bbox.Min.X, bbox.Max.Y - bbox.Min.Y, bbox.Max.Z - bbox.Min.Z])
    diag = bbox.Diagonal.Length
    
    area = 0.0
    if isinstance(geometry, Rhino.Geometry.Mesh):
        amp = Rhino.Geometry.AreaMassProperties.Compute(geometry)
        if amp: area = amp.Area
    elif isinstance(geometry, Rhino.Geometry.Brep):
        amp = Rhino.Geometry.AreaMassProperties.Compute(geometry)
        if amp: area = amp.Area
    elif isinstance(geometry, Rhino.Geometry.Extrusion):
        amp = Rhino.Geometry.AreaMassProperties.Compute(geometry.ToBrep())
        if amp: area = amp.Area
        
    if not is_glass_override:
        if diag < 0.4 or (dims[0] < 0.2 and dims[1] < 0.2) or (dims[0] < 0.05 and diag < 1.0):
            return []
        if area > 0 and area < 0.1:
            return []
        if area > 0:
            x, y, z = dims[0], dims[1], dims[2]
            bbox_area = 2 * ((x*y) + (y*z) + (z*x))
            if bbox_area > 0 and (area / bbox_area) < 0.55 and diag < 10.0:
                return []
                
    # Deduplication
    pt = bbox.Center
    pt.Transform(xform)
    sig = (round(pt.X, 3), round(pt.Y, 3), round(pt.Z, 3), round(area, 3))
    if sig in seen_signatures:
        return []
    seen_signatures.add(sig)
    
    meshes = []
    if isinstance(geometry, Rhino.Geometry.Mesh):
        m = geometry.Duplicate()
        m.Transform(xform)
        meshes.append(m)
    elif isinstance(geometry, (Rhino.Geometry.Brep, Rhino.Geometry.Extrusion)):
        br = geometry if isinstance(geometry, Rhino.Geometry.Brep) else geometry.ToBrep()
        mp = Rhino.Geometry.MeshingParameters.FastRenderMesh
        b_meshes = Rhino.Geometry.Mesh.CreateFromBrep(br, mp)
        if b_meshes:
            for bm in b_meshes:
                bm.Transform(xform)
                meshes.append(bm)
    return meshes

def extract_meshes(geometry, xform=Rhino.Geometry.Transform.Identity, is_glass_override=False):
    if isinstance(geometry, Rhino.Geometry.InstanceReferenceGeometry):
        meshes = []
        idef = sc.doc.InstanceDefinitions.FindId(geometry.ParentIdefId)
        if idef:
            for obj in idef.GetObjects():
                layer_name = sc.doc.Layers[obj.Attributes.LayerIndex].Name
                child_glass = is_glass_override or is_glass(layer_name)
                child_xform = xform * geometry.Xform
                meshes.extend(extract_meshes(obj.Geometry, child_xform, child_glass))
        return meshes
    else:
        return process_geometry(geometry, xform, is_glass_override)

def create_solid_footprint(curves, z_curr, slice_interval, res=2.0):
    if not curves: return None
    
    bbox = Rhino.Geometry.BoundingBox.Empty
    for c in curves:
        bbox.Union(c.GetBoundingBox(True))
        
    x_min = bbox.Min.X - res*3
    x_max = bbox.Max.X + res*3
    y_min = bbox.Min.Y - res*3
    y_max = bbox.Max.Y + res*3
    
    x_steps = int((x_max - x_min) / res)
    y_steps = int((y_max - y_min) / res)
    
    grid = [[0 for _ in range(y_steps)] for _ in range(x_steps)]
    
    # Rasterize
    for crv in curves:
        length = crv.GetLength()
        if length == 0: continue
        divs = int(length / (res/4.0)) + 1
        params = crv.DivideByCount(divs, True)
        if params:
            for p in params:
                pt = crv.PointAt(p)
                i = int((pt.X - x_min) / res)
                j = int((pt.Y - y_min) / res)
                if 0 <= i < x_steps and 0 <= j < y_steps:
                    grid[i][j] = 1
                    
    # Dilate (Thicken walls to bridge gaps like doors/windows)
    grid_dilated = [[grid[i][j] for j in range(y_steps)] for i in range(x_steps)]
    dirs_dilate = [(0,1), (0,-1), (1,0), (-1,0), (1,1), (-1,-1), (1,-1), (-1,1)]
    for i in range(1, x_steps-1):
        for j in range(1, y_steps-1):
            if grid[i][j] == 1:
                for dx, dy in dirs_dilate:
                    grid_dilated[i+dx][j+dy] = 1
                    
    # Flood fill from (0,0) (Outside)
    q = deque()
    q.append((0, 0))
    grid_dilated[0][0] = 2
    
    while q:
        cx, cy = q.popleft()
        for dx, dy in dirs_dilate:
            nx, ny = cx + dx, cy + dy
            if 0 <= nx < x_steps and 0 <= ny < y_steps:
                if grid_dilated[nx][ny] == 0:
                    grid_dilated[nx][ny] = 2
                    q.append((nx, ny))
                    
    # Generate cells for solid (not 2)
    # EROSION STEP: To fix the "bloated" look, we must erode the shape by 1 cell 
    # to reverse the 1-cell dilation we did earlier.
    grid_eroded = [[grid_dilated[i][j] for j in range(y_steps)] for i in range(x_steps)]
    
    for i in range(1, x_steps-1):
        for j in range(1, y_steps-1):
            if grid_dilated[i][j] != 2: # It is solid inside
                # If it touches the outside (2), it gets eroded
                for dx, dy in dirs_dilate:
                    if grid_dilated[i+dx][j+dy] == 2:
                        grid_eroded[i][j] = 2
                        break
                        
    cell_curves = []
    for i in range(x_steps):
        for j in range(y_steps):
            if grid_eroded[i][j] != 2:
                cx = x_min + i * res + res/2.0
                cy = y_min + j * res + res/2.0
                hs = res / 2.0
                pts = [
                    Rhino.Geometry.Point3d(cx-hs, cy-hs, z_curr),
                    Rhino.Geometry.Point3d(cx+hs, cy-hs, z_curr),
                    Rhino.Geometry.Point3d(cx+hs, cy+hs, z_curr),
                    Rhino.Geometry.Point3d(cx-hs, cy+hs, z_curr),
                    Rhino.Geometry.Point3d(cx-hs, cy-hs, z_curr)
                ]
                cell_curves.append(Rhino.Geometry.PolylineCurve(pts))
                
    if not cell_curves: return None
    
    unioned = Rhino.Geometry.Curve.CreateBooleanUnion(cell_curves, 0.01)
    if not unioned: return None
    
    extrusions = []
    for u_crv in unioned:
        ext = Rhino.Geometry.Extrusion.Create(u_crv, slice_interval, True)
        if ext: extrusions.append(ext.ToBrep())
        
    return extrusions

def run_ladybug_mass_extraction():
    target_layer = "Target Geometry"
    output_layer = "Analysis::Ladybug_Test_Output"
    slice_interval = 3.0
    grid_resolution = 1.0 # Reduced from 2.0 to 1.0 for tighter fit
    
    rs.EnableRedraw(False)
    start_time = time.time()
    
    try:
        # Setup layers
        if not rs.IsLayer("Analysis"): rs.AddLayer("Analysis")
        if not rs.IsLayer(output_layer): rs.AddLayer("Ladybug_Test_Output", (255, 150, 0), parent="Analysis")
        else:
            old = rs.ObjectsByLayer(output_layer)
            if old: rs.DeleteObjects(old)
            
        print("Gathering and filtering Target Geometry...")
        seen_signatures.clear()
        
        raw_objects = rs.ObjectsByLayer(target_layer)
        if not raw_objects:
            print("No objects found in Target Geometry.")
            return
            
        all_meshes = []
        for oid in raw_objects:
            geo = rs.coercegeometry(oid)
            layer_name = rs.ObjectLayer(oid)
            all_meshes.extend(extract_meshes(geo, is_glass_override=is_glass(layer_name)))
            
        if not all_meshes:
            print("No valid geometry extracted.")
            return
            
        # Join meshes for faster intersection
        raycast_mesh = Rhino.Geometry.Mesh()
        for m in all_meshes:
            raycast_mesh.Append(m)
            
        bbox = raycast_mesh.GetBoundingBox(True)
        z_min = bbox.Min.Z
        z_max = bbox.Max.Z
        
        print("Slicing from Z={:.1f} to {:.1f} in {}m intervals...".format(z_min, z_max, slice_interval))
        
        z_curr = z_min
        tier_breps = []
        
        while z_curr < z_max:
            # Fix "Floating" issue: Take 3 slices per tier to ensure we don't miss short platforms
            z_top = min(z_curr + slice_interval, z_max)
            actual_interval = z_top - z_curr
            
            intersect_curves = []
            
            # Slice at bottom, middle, and top of the tier
            slice_heights = [z_curr + 0.1, z_curr + actual_interval / 2.0, z_top - 0.1]
            
            for hz in slice_heights:
                if hz >= z_max: continue
                plane = Rhino.Geometry.Plane(Rhino.Geometry.Point3d(0,0,hz), Rhino.Geometry.Vector3d.ZAxis)
                crv_arr = Rhino.Geometry.Intersect.Intersection.MeshPlane(raycast_mesh, plane)
                if crv_arr:
                    for c in crv_arr: 
                        intersect_curves.append(c.ToPolylineCurve())
                
            if intersect_curves:
                exts = create_solid_footprint(intersect_curves, z_curr, actual_interval, grid_resolution)
                if exts:
                    tier_breps.extend(exts)
                    
            z_curr += slice_interval
            
        print("Tier processing complete. Generated {} tier masses. Unioning...".format(len(tier_breps)))
        
        if tier_breps:
            final_breps = tier_breps
            if len(tier_breps) > 1:
                unioned_walls = []
                current = tier_breps[0]
                for i in range(1, len(tier_breps)):
                    u = Rhino.Geometry.Brep.CreateBooleanUnion([current, tier_breps[i]], 0.01)
                    if u and len(u) > 0:
                        current = u[0]
                    else:
                        unioned_walls.append(current)
                        current = tier_breps[i]
                unioned_walls.append(current)
                final_breps = unioned_walls
                
            for fb in final_breps:
                fb.MergeCoplanarFaces(0.01)
                new_id = sc.doc.Objects.AddBrep(fb)
                if new_id: rs.ObjectLayer(new_id, output_layer)
                
        elapsed = time.time() - start_time
        print("Phase 3 (Ladybug) Complete in {:.1f}s.".format(elapsed))
        
    except Exception as e:
        import traceback
        print("Error: {}\n{}".format(str(e), traceback.format_exc()))
    finally:
        rs.EnableRedraw(True)

if __name__ == "__main__":
    run_ladybug_mass_extraction()
