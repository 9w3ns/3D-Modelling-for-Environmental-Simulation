import rhinoscriptsyntax as rs
import time

def is_glass(obj_id):
    layer = rs.ObjectLayer(obj_id).lower()
    if "glass" in layer or "glazing" in layer or "window" in layer:
        return True
    return False

def run_phase_1():
    source_layer = "Target Geometry 3"
    target_layer = "Analysis::Phase1"
    
    rs.EnableRedraw(False)
    start_time = time.time()
    try:
        # 1. Setup Target Layer
        if not rs.IsLayer("Analysis"): rs.AddLayer("Analysis")
        if not rs.IsLayer(target_layer): rs.AddLayer(target_layer, (0, 255, 0))
        
        # Clear existing geometry in target layer
        old_objs = rs.ObjectsByLayer(target_layer)
        if old_objs: rs.DeleteObjects(old_objs)
            
        # 2. Get Source Geometry
        objs = rs.ObjectsByLayer(source_layer)
        if not objs:
            return "No objects found on " + source_layer
            
        print(f"Phase 1: Copying {len(objs)} objects to target layer...")
        # Copy to avoid destroying original model
        copied_objs = rs.CopyObjects(objs)
        for obj in copied_objs:
            rs.ObjectLayer(obj, target_layer)
            
        print("Phase 1: Exploding Blocks and Inheriting Layers...")
        instances = [o for o in copied_objs if rs.IsBlockInstance(o)]
        to_check = [o for o in copied_objs if not rs.IsBlockInstance(o)]
        
        while instances:
            new_instances = []
            for inst in instances:
                parent_layer = rs.ObjectLayer(inst)
                exploded = rs.ExplodeBlockInstance(inst)
                if exploded:
                    for e in exploded:
                        # Inherit parent's semantic layer
                        rs.ObjectLayer(e, parent_layer)
                        if rs.IsBlockInstance(e):
                            new_instances.append(e)
                        else:
                            to_check.append(e)
            instances = new_instances
                
        print(f"Phase 1: Filtering {len(to_check)} raw geometries and removing duplicates...")
        survivors = []
        discarded = 0
        duplicates = 0
        seen_signatures = set()
        
        for oid in to_check:
            # Filter 01: Purge non-Brep/Mesh (curves, points)
            if not (rs.IsPolysurface(oid) or rs.IsSurface(oid) or rs.IsMesh(oid)):
                rs.DeleteObject(oid)
                discarded += 1
                continue
                
            # Deduplication: Check if we've seen this geometry before
            bbox = rs.BoundingBox(oid)
            if not bbox:
                rs.DeleteObject(oid)
                discarded += 1
                continue
            
            # Create a signature: Centroid + Area (rounded to 3 decimals to handle float drift)
            centroid = rs.SurfaceAreaCentroid(oid)[0] if (rs.IsSurface(oid) or rs.IsPolysurface(oid)) else rs.MeshAreaCentroid(oid)
            if not centroid:
                centroid = rs.BoxCenter(bbox)
            
            area = rs.Area(oid) or 0
            
            # Signature = (x, y, z, area)
            sig = (round(centroid.X, 3), round(centroid.Y, 3), round(centroid.Z, 3), round(area, 3))
            
            if sig in seen_signatures:
                rs.DeleteObject(oid)
                duplicates += 1
                continue
            
            seen_signatures.add(sig)

            # Protect Glass/Apertures
            if is_glass(oid):
                survivors.append(oid)
                continue
                
            x = rs.Distance(bbox[0], bbox[1])
            y = rs.Distance(bbox[0], bbox[3])
            z = rs.Distance(bbox[0], bbox[4])
            dims = sorted([x, y, z])
            diag = rs.Distance(bbox[0], bbox[6])

            # Detail cull (diagonal < 0.4m or thin stick < 0.2m)
            if diag < 0.4 or (dims[0] < 0.2 and dims[1] < 0.2) or (dims[0] < 0.05 and diag < 1.0):
                rs.DeleteObject(oid)
                discarded += 1
                continue
                
            # Surface area check & Solidity Ratio (Hollow Frame Check)
            if rs.IsSurface(oid) or rs.IsMesh(oid) or rs.IsPolysurface(oid):
                area = rs.Area(oid)
                if area and area < 0.1:
                    rs.DeleteObject(oid)
                    discarded += 1
                    continue
                
                # Solidity Ratio: Compare actual area to bounding box area
                # Bounding box surface area = 2*(xy + yz + zx)
                bbox_area = 2 * ((dims[0]*dims[1]) + (dims[1]*dims[2]) + (dims[2]*dims[0]))
                if bbox_area > 0:
                    solidity_ratio = area / bbox_area
                    # If it's very hollow (actual area is less than 55% of the bounding box area)
                    # AND it's not a massive structure (to protect complex large masses)
                    if solidity_ratio < 0.55 and diag < 10.0:
                        rs.DeleteObject(oid)
                        discarded += 1
                        continue
                        
            survivors.append(oid)
            
        elapsed = time.time() - start_time
        return f"Phase 1 Complete in {elapsed:.1f}s. Survivors: {len(survivors)}, Discarded: {discarded}, Duplicates: {duplicates}"
        
    except Exception as e:
        return f"Error during Phase 1: {str(e)}"
    finally:
        rs.EnableRedraw(True)

if __name__ == "__main__":
    print(run_phase_1())