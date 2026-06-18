import rhinoscriptsyntax as rs
import time

def is_glass(obj_id):
    layer = rs.ObjectLayer(obj_id).lower()
    mat_idx = rs.ObjectMaterialIndex(obj_id)
    # Check layer name
    if "glass" in layer or "glazing" in layer or "window" in layer:
        return True
    # Check material name if exists
    if mat_idx > -1:
        mat_name = rs.MaterialName(mat_idx).lower()
        if "glass" in mat_name or "glazing" in mat_name:
            return True
    return False

def get_category(oid):
    # 1. Apertures
    if is_glass(oid): 
        return "Apertures"
        
    bbox = rs.BoundingBox(oid)
    if not bbox: 
        return "Shading"
        
    dx = rs.Distance(bbox[0], bbox[1])
    dy = rs.Distance(bbox[0], bbox[3])
    dz = rs.Distance(bbox[0], bbox[4])
    
    # 2. Floors
    # Z thickness is roughly 10-50cm (using 5cm-60cm for safety tolerance)
    if 0.05 <= dz <= 0.60:
        # Ensure it's horizontal (X and Y are significantly larger than Z)
        if dx > dz * 1.5 and dy > dz * 1.5:
            return "Floors"
            
    # 3. Walls
    # Z (Height) should be at least 1.5m
    if dz >= 1.5:
        # If X is the thickness (orthogonal wall)
        if 0.05 <= dx <= 0.45 and dy > dx * 1.5:
            return "Walls"
        # If Y is the thickness (orthogonal wall)
        if 0.05 <= dy <= 0.45 and dx > dy * 1.5:
            return "Walls"
            
        # Fallback for diagonal/angled walls
        # If both DX and DY are large, the bounding box is a square but the wall is thin inside it.
        if dx > 0.45 and dy > 0.45:
            # Approximate thickness = Volume / (Area / 2)
            if rs.IsPolysurfaceClosed(oid):
                vol = rs.SurfaceVolume(oid)
                area = rs.Area(oid)
                if vol and area and area > 0:
                    approx_thick = vol[0] / (area / 2.0)
                    if 0.05 <= approx_thick <= 0.45:
                        return "Walls"
                        
    # 4. Shading
    # If it passes detail filters but isn't a floor, wall, or window, it's shading.
    return "Shading"

def run_phase_2():
    source_layer = "Analysis::Phase1"
    base_target = "Analysis::Phase2"
    
    categories = ["Floors", "Walls", "Apertures", "Shading"]
    colors = {
        "Floors": (255, 150, 150),   # Pinkish
        "Walls": (150, 150, 255),    # Light Blue
        "Apertures": (150, 255, 255),# Cyan
        "Shading": (200, 200, 200)   # Grey
    }
    
    rs.EnableRedraw(False)
    start_time = time.time()
    try:
        # Setup Layers
        if not rs.IsLayer("Analysis"): rs.AddLayer("Analysis")
        if not rs.IsLayer(base_target): rs.AddLayer(base_target, (0,0,0), "Analysis")
        
        for cat in categories:
            layer_path = f"{base_target}::{cat}"
            if not rs.IsLayer(layer_path):
                rs.AddLayer(layer_path, colors[cat])
            else:
                # Clear existing
                old = rs.ObjectsByLayer(layer_path)
                if old: rs.DeleteObjects(old)
                
        objs = rs.ObjectsByLayer(source_layer)
        if not objs:
            return "No objects found on " + source_layer
            
        print(f"Phase 2: Categorizing {len(objs)} objects...")
        copied_objs = rs.CopyObjects(objs)
        
        counts = {"Floors": 0, "Walls": 0, "Apertures": 0, "Shading": 0}
        
        for obj in copied_objs:
            cat = get_category(obj)
            rs.ObjectLayer(obj, f"{base_target}::{cat}")
            counts[cat] += 1
            
        # Hide Phase 1 layer so we only see Phase 2
        rs.LayerVisible(source_layer, False)
            
        elapsed = time.time() - start_time
        report = f"Phase 2 Complete in {elapsed:.1f}s.\n"
        for c in categories:
            report += f"  - {c}: {counts[c]}\n"
        return report

    except Exception as e:
        return f"Error during Phase 2: {str(e)}"
    finally:
        rs.EnableRedraw(True)

if __name__ == "__main__":
    print(run_phase_2())