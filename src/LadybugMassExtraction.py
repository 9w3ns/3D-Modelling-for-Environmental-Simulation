import rhinoscriptsyntax as rs
import Rhino
import scriptcontext as sc
import time
from collections import deque

# --- Ramer-Douglas-Peucker Smoothing ---
def rdp_point_line_dist(pt, start, end):
    vx = end.X - start.X
    vy = end.Y - start.Y
    wx = pt.X - start.X
    wy = pt.Y - start.Y
    
    c1 = wx * vx + wy * vy
    if c1 <= 0: return pt.DistanceTo(start)
    c2 = vx * vx + vy * vy
    if c2 <= c1: return pt.DistanceTo(end)
        
    b = c1 / c2
    proj = Rhino.Geometry.Point3d(start.X + b * vx, start.Y + b * vy, start.Z)
    return pt.DistanceTo(proj)

def rdp_open(pts, epsilon):
    dmax = 0.0
    index = 0
    end = len(pts) - 1
    for i in range(1, end):
        d = rdp_point_line_dist(pts[i], pts[0], pts[end])
        if d > dmax:
            index = i
            dmax = d
    if dmax > epsilon:
        res1 = rdp_open(pts[:index+1], epsilon)
        res2 = rdp_open(pts[index:], epsilon)
        return res1[:-1] + res2
    else:
        return [pts[0], pts[end]]

def smooth_closed_polyline(polyline_curve, epsilon):
    if epsilon <= 0: return polyline_curve
    success, polyline = polyline_curve.TryGetPolyline()
    if not success: return polyline_curve
    
    pts = list(polyline)
    if len(pts) < 4: return polyline_curve
    
    max_d = -1
    idx_b = 0
    for i in range(len(pts)-1):
        d = pts[0].DistanceTo(pts[i])
        if d > max_d:
            max_d = d
            idx_b = i
            
    half1 = pts[0:idx_b+1]
    half2 = pts[idx_b:-1] + [pts[0]]
    
    s1 = rdp_open(half1, epsilon)
    s2 = rdp_open(half2, epsilon)
    
    final_pts = s1[:-1] + s2
    return Rhino.Geometry.PolylineCurve(final_pts)
# ---------------------------------------

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

def generate_eroded_grid(curves, res, x_min, y_min, x_steps, y_steps):
    grid = [[0 for _ in range(y_steps)] for _ in range(x_steps)]
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
                    
    grid_dilated = [[grid[i][j] for j in range(y_steps)] for i in range(x_steps)]
    dirs_dilate = [(0,1), (0,-1), (1,0), (-1,0), (1,1), (-1,-1), (1,-1), (-1,1)]
    for i in range(1, x_steps-1):
        for j in range(1, y_steps-1):
            if grid[i][j] == 1:
                for dx, dy in dirs_dilate:
                    grid_dilated[i+dx][j+dy] = 1
                    
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
                    
    grid_eroded = [[grid_dilated[i][j] for j in range(y_steps)] for i in range(x_steps)]
    for i in range(1, x_steps-1):
        for j in range(1, y_steps-1):
            if grid_dilated[i][j] != 2:
                for dx, dy in dirs_dilate:
                    if grid_dilated[i+dx][j+dy] == 2:
                        grid_eroded[i][j] = 2
                        break
    return grid_eroded

def grids_are_similar(g1, g2, x_steps, y_steps, threshold=0.95):
    if g1 is None or g2 is None: return False
    solid1 = 0
    solid2 = 0
    overlap = 0
    for i in range(x_steps):
        for j in range(y_steps):
            s1 = (g1[i][j] != 2)
            s2 = (g2[i][j] != 2)
            if s1: solid1 += 1
            if s2: solid2 += 1
            if s1 and s2: overlap += 1
            
    m = max(solid1, solid2)
    if m == 0: return True
    return (overlap / float(m)) >= threshold

def union_grids(g1, g2, x_steps, y_steps):
    for i in range(x_steps):
        for j in range(y_steps):
            if g2[i][j] != 2:
                g1[i][j] = 1 # Mark as solid

def grid_to_smoothed_curves(grid, z_curr, res, x_min, y_min, x_steps, y_steps, smoothing_tol):
    cell_curves = []
    for i in range(x_steps):
        for j in range(y_steps):
            if grid[i][j] != 2:
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
                
    if not cell_curves: return []
    
    unioned = Rhino.Geometry.Curve.CreateBooleanUnion(cell_curves, 0.01)
    if not unioned: return []
    
    smoothed = []
    for u_crv in unioned:
        smoothed.append(smooth_closed_polyline(u_crv, smoothing_tol))
    return smoothed

def run_ladybug_mass_extraction():
    target_layer = "Target Geometry"
    output_layer = "Analysis::Ladybug_Test_Output"
    contour_layer = "Analysis::Ladybug_Test_Contours"
    slice_interval = 1.0 # Changed contour distance back to 1.0m
    grid_resolution = 1.0 # Reverted grid resolution back to 1.0m
    smoothing_tolerance = 1.0 # Reverted to 1.0 as it works best for the user
    similarity_threshold = 0.80 # Increased to 0.80 to trigger more blocks
    
    rs.EnableRedraw(False)
    start_time = time.time()
    
    try:
        if not rs.IsLayer("Analysis"): rs.AddLayer("Analysis")
        if not rs.IsLayer(output_layer): rs.AddLayer("Ladybug_Test_Output", (255, 150, 0), parent="Analysis")
        else:
            old = rs.ObjectsByLayer(output_layer)
            if old: rs.DeleteObjects(old)
            
        if not rs.IsLayer(contour_layer): rs.AddLayer("Ladybug_Test_Contours", (0, 200, 255), parent="Analysis")
        else:
            old_c = rs.ObjectsByLayer(contour_layer)
            if old_c: rs.DeleteObjects(old_c)
            
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
            
        raycast_mesh = Rhino.Geometry.Mesh()
        for m in all_meshes:
            raycast_mesh.Append(m)
            
        bbox = raycast_mesh.GetBoundingBox(True)
        z_min = bbox.Min.Z
        z_max = bbox.Max.Z
        
        # Setup Global Grid
        global_x_min = bbox.Min.X - grid_resolution * 3
        global_x_max = bbox.Max.X + grid_resolution * 3
        global_y_min = bbox.Min.Y - grid_resolution * 3
        global_y_max = bbox.Max.Y + grid_resolution * 3
        x_steps = int((global_x_max - global_x_min) / grid_resolution)
        y_steps = int((global_y_max - global_y_min) / grid_resolution)
        
        print("Slicing from Z={:.1f} to {:.1f} in {}m intervals...".format(z_min, z_max, slice_interval))
        
        z_curr = z_min
        blocks = [] # {"grid": grid, "z_start": float, "z_end": float}
        
        while z_curr < z_max:
            z_top = min(z_curr + slice_interval, z_max)
            intersect_curves = []
            
            # 2 Slices per meter to ensure we catch short platform edges
            slice_heights = [z_curr + 0.1, z_top - 0.1]
            for hz in slice_heights:
                if hz >= z_max: continue
                plane = Rhino.Geometry.Plane(Rhino.Geometry.Point3d(0,0,hz), Rhino.Geometry.Vector3d.ZAxis)
                crv_arr = Rhino.Geometry.Intersect.Intersection.MeshPlane(raycast_mesh, plane)
                if crv_arr:
                    for c in crv_arr: 
                        intersect_curves.append(c.ToPolylineCurve())
                
            if intersect_curves:
                grid = generate_eroded_grid(intersect_curves, grid_resolution, global_x_min, global_y_min, x_steps, y_steps)
                
                # Compare with previous block to merge identical vertical slices
                if blocks and grids_are_similar(blocks[-1]["grid"], grid, x_steps, y_steps, similarity_threshold):
                    blocks[-1]["z_end"] = z_top
                    union_grids(blocks[-1]["grid"], grid, x_steps, y_steps)
                else:
                    # Don't add completely empty grids to blocks unless it's the only way
                    solid_count = sum(1 for i in range(x_steps) for j in range(y_steps) if grid[i][j] != 2)
                    if solid_count > 0:
                        blocks.append({"grid": grid, "z_start": z_curr, "z_end": z_top})
                    elif blocks:
                        # If empty, just extend the previous block through the empty space
                        blocks[-1]["z_end"] = z_top
                    
            z_curr += slice_interval
            
        print("Tier grouping complete. Found {} distinct vertical blocks. Extruding and Unioning...".format(len(blocks)))
        
        tier_breps = []
        for b in blocks:
            crvs = grid_to_smoothed_curves(b["grid"], b["z_start"], grid_resolution, global_x_min, global_y_min, x_steps, y_steps, smoothing_tolerance)
            for c in crvs:
                # Bake the contour curve so the user can see the 2D footprint process
                contour_id = sc.doc.Objects.AddCurve(c)
                if contour_id: rs.ObjectLayer(contour_id, contour_layer)
                
                ext = Rhino.Geometry.Extrusion.Create(c, b["z_end"] - b["z_start"], True)
                if ext: tier_breps.append(ext.ToBrep())
        
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
