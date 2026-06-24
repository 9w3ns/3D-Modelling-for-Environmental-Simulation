import rhinoscriptsyntax as rs
import Rhino
import scriptcontext as sc
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

def get_category_and_data(oid):
    """Returns (Category, ExtraData) where ExtraData might be aperture curves from voids."""
    # 1. Apertures (Pre-defined glass)
    if is_glass(oid): 
        return "Apertures", None
        
    bbox = rs.BoundingBox(oid)
    if not bbox: 
        return "Shading", None
        
    dx = rs.Distance(bbox[0], bbox[1])
    dy = rs.Distance(bbox[0], bbox[3])
    dz = rs.Distance(bbox[0], bbox[4])
    
    # 2. Floors
    # Z thickness is roughly 10-50cm
    if 0.05 <= dz <= 0.60:
        # Ensure it's horizontal
        if dx > dz * 1.5 and dy > dz * 1.5:
            area = rs.Area(oid)
            if area and area >= 30:
                return "Floors", None
            else:
                return "Shading", None # Small floors become shading
            
    # 3. Walls & Void Detection
    is_wall = False
    aperture_curves = []
    
    # Standard height check
    if dz >= 1.5:
        # Check thickness
        thick = min(dx, dy)
        if 0.001 <= thick <= 0.60:
            is_wall = True
        else:
            # Fallback for angled walls using volume/area (supports Breps and Meshes)
            vol = None
            area = None
            
            if rs.IsPolysurface(oid) and rs.IsPolysurfaceClosed(oid):
                v_res = rs.SurfaceVolume(oid)
                a_res = rs.Area(oid)
                if v_res and a_res:
                    vol, area = v_res[0], a_res
            elif rs.IsMesh(oid):
                v_res = rs.MeshVolume(oid)
                a_res = rs.MeshArea(oid)
                if v_res and a_res:
                    vol, area = v_res[0], a_res[1]
            
            if vol is not None and area and area > 0:
                approx_thick = vol / (area / 2.0)
                if 0.001 <= approx_thick <= 0.60:
                    is_wall = True
            
            # Final fallback: If it's a single surface or open mesh, check its "verticality"
            if not is_wall:
                # If it's very thin in one horizontal dimension compared to its height
                # (Catching single-face vertical planes)
                if min(dx, dy) < 0.1 and dz > min(dx, dy) * 2:
                    is_wall = True
                        
    if is_wall:
        # Analyze topology for voids (Apertures)
        try:
            brep = rs.coercebrep(oid)
            if brep:
                for face in brep.Faces:
                    # Find inner loops (holes)
                    for loop in face.Loops:
                        if loop.LoopType == Rhino.Geometry.BrepLoopType.Inner:
                            # Extract the loop as a curve
                            curve = loop.To3dCurve()
                            if curve:
                                aperture_curves.append(curve)
        except Exception as e:
            # If void detection fails (e.g. on a complex mesh), we still keep it as a wall
            # but log the issue if necessary.
            pass
        return "Walls", aperture_curves
                        
    # 4. Shading fallback
    return "Shading", None

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
            
        print(f"Phase 2: Categorizing {len(objs)} objects and generating missing apertures...")
        
        counts = {"Floors": 0, "Walls": 0, "Apertures": 0, "Shading": 0}
        
        for oid in objs:
            try:
                # Copy to target
                new_obj = rs.CopyObject(oid)
                if not new_obj: continue
                
                cat, extra = get_category_and_data(oid)
                
                rs.ObjectLayer(new_obj, f"{base_target}::{cat}")
                counts[cat] += 1
                
                # Handle generated apertures from voids
                if extra: # extra is a list of curves
                    for crv in extra:
                        # Create planar surface from loop
                        planar_breps = Rhino.Geometry.Brep.CreatePlanarBreps(crv, 0.01)
                        if planar_breps:
                            for b in planar_breps:
                                ap_id = sc.doc.Objects.AddBrep(b)
                                if ap_id:
                                    rs.ObjectLayer(ap_id, f"{base_target}::Apertures")
                                    counts["Apertures"] += 1
            except Exception as e:
                print(f"Warning: Failed to process object {oid}: {str(e)}")
                continue
            
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