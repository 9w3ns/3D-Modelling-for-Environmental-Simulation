import Rhino
import Rhino.Geometry as rg
import scriptcontext as sc
import rhinoscriptsyntax as rs

def cleanup_and_extract(source_layer="Target Geometry", threshold_diag=0.4, thinness_limit=0.2, min_area=0.1):
    """
    Surgical Workflow Optimized for Large Documents
    """
    rs.EnableRedraw(False)
    try:
        # 1. Clear Analysis Layer
        analysis_layer = "Analysis::Ladybug::Geometry"
        if rs.IsLayer(analysis_layer):
            old_objs = rs.ObjectsByLayer(analysis_layer)
            if old_objs: rs.DeleteObjects(old_objs)
        else:
            rs.AddLayer(analysis_layer, (0, 0, 255))

        # 2. Select Source Geometry
        if not rs.IsLayer(source_layer):
            print("Layer '{}' not found.".format(source_layer))
            return

        objs = rs.ObjectsByLayer(source_layer)
        if not objs:
            print("No objects found on layer '{}'".format(source_layer))
            return
        
        print("Initial objects: {}".format(len(objs)))

        # 3. Step 1: Filter and Explode Blocks
        to_process = []
        instances = [o for o in objs if rs.IsBlockInstance(o)]
        print("Exploding {} blocks...".format(len(instances)))
        while instances:
            new_instances = []
            for inst in instances:
                exploded = rs.ExplodeBlockInstance(inst)
                if exploded:
                    for e in exploded:
                        if rs.IsBlockInstance(e): new_instances.append(e)
                        else: to_process.append(e)
            instances = new_instances
        
        to_process.extend([o for o in objs if not rs.IsBlockInstance(o)])

        # 4. Step 2: Surgical Explosion of Polysurfaces
        print("Surgically processing {} objects...".format(len(to_process)))
        final_list = []
        discarded_count = 0
        
        for oid in to_process:
            bbox = rs.BoundingBox(oid)
            if not bbox: continue
            
            x = rs.Distance(bbox[0], bbox[1])
            y = rs.Distance(bbox[0], bbox[3])
            z = rs.Distance(bbox[0], bbox[4])
            dims = sorted([x, y, z])
            diag = rs.Distance(bbox[0], bbox[6])

            # PRE-FILTER: Discard obviously small or thin assemblies
            if diag < threshold_diag or (dims[0] < thinness_limit and dims[1] < thinness_limit):
                rs.DeleteObject(oid)
                discarded_count += 1
                continue
            
            # COMPLEXITY CHECK: Explode if it's a complex polysurface (likely window frame)
            if rs.IsPolysurface(oid):
                scount = rs.PolysurfaceSurfaceCount(oid)
                if scount > 2: # Likely an assembly (box frame, etc)
                    faces = rs.ExplodePolysurfaces(oid, delete_input=True)
                    if faces: final_list.extend(faces)
                    else: final_list.append(oid)
                else:
                    final_list.append(oid)
            else:
                final_list.append(oid)

        print("Post-surgical count: {}. Discarded assemblies: {}".format(len(final_list), discarded_count))

        # 5. Final Pass: Detail Filtering
        survivors = []
        for i, obj_id in enumerate(final_list):
            bbox = rs.BoundingBox(obj_id)
            if not bbox:
                rs.DeleteObject(obj_id)
                continue
                
            x = rs.Distance(bbox[0], bbox[1])
            y = rs.Distance(bbox[0], bbox[3])
            z = rs.Distance(bbox[0], bbox[4])
            dims = sorted([x, y, z])
            diag = rs.Distance(bbox[0], bbox[6])

            # Final size checks
            if diag < threshold_diag or (dims[0] < thinness_limit and dims[1] < thinness_limit):
                rs.DeleteObject(obj_id)
                discarded_count += 1
                continue

            # Area Check for faces
            area = 0
            if rs.IsSurface(obj_id) or rs.IsMesh(obj_id):
                area = rs.Area(obj_id)
            
            if area and area < min_area:
                rs.DeleteObject(obj_id)
                discarded_count += 1
                continue

            survivors.append(obj_id)

        print("Final Analysis Geometry count: {}".format(len(survivors)))

        # Move to analysis layer
        for obj_id in survivors:
            rs.ObjectLayer(obj_id, analysis_layer)
            
    finally:
        rs.EnableRedraw(True)

if __name__ == "__main__":
    cleanup_and_extract()
