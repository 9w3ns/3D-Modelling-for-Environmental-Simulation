import Rhino
import scriptcontext as sc
import rhinoscriptsyntax as rs
import System

def verify_ladybug_transformation():
    """
    DIAGNOSTIC VERSION 2: 'Outer Shell Extrusion' Logic
    Replaces Bounding Box with true 2D footprint extrusion.
    """
    
    source_layer_name = "Original"
    output_layer_path = "Analysis::Ladybug::Geometry"
    
    print("--- DIAGNOSTIC START ---")
    
    # 1. Output Layer Setup
    full_path = rs.AddLayer(output_layer_path, System.Drawing.Color.Blue)
    rs.LayerVisible(full_path, True)
    
    objs = rs.ObjectsByLayer(full_path)
    if objs: rs.DeleteObjects(objs)
    
    layer_obj = sc.doc.Layers.FindByFullPath(full_path, True)
    if layer_obj < 0:
        print("FAIL: Could not find output layer index.")
        return
    layer_index = layer_obj

    # 2. Source Ingestion
    all_layers = sc.doc.Layers
    source_layer_index = -1
    for layer in all_layers:
        if layer.Name == source_layer_name:
            source_layer_index = layer.Index
            break
            
    if source_layer_index == -1:
        print("FAIL: Could not find a layer named 'Original'.")
        return

    # 3. Recursive Process
    processed_count = [0]
    error_count = [0]
    
    # Dictionary to group geometry by top-level block/object
    # Key: Top-level object ID, Value: List of 2D bounding box rectangles (Curves)
    cluster_footprints = {}
    cluster_heights = {}
    
    def process_geometry(obj_id, top_level_id, current_xf=None):
        if rs.IsBlockInstance(obj_id):
            block_name = rs.BlockInstanceName(obj_id)
            inner_ids = rs.BlockObjects(block_name)
            instance_xf = rs.BlockInstanceXform(obj_id)
            
            new_xf = current_xf * instance_xf if current_xf else instance_xf
            
            for inner_id in inner_ids:
                process_geometry(inner_id, top_level_id, new_xf)
        else:
            geo = rs.coercegeometry(obj_id)
            if geo:
                temp_geo = geo.Duplicate()
                if current_xf:
                    temp_geo.Transform(current_xf)
                
                bbox = temp_geo.GetBoundingBox(True)
                if bbox.IsValid:
                    # Track max height for the cluster
                    if top_level_id not in cluster_heights:
                        cluster_heights[top_level_id] = {'min_z': bbox.Min.Z, 'max_z': bbox.Max.Z}
                    else:
                        cluster_heights[top_level_id]['min_z'] = min(cluster_heights[top_level_id]['min_z'], bbox.Min.Z)
                        cluster_heights[top_level_id]['max_z'] = max(cluster_heights[top_level_id]['max_z'], bbox.Max.Z)
                    
                    # Create 2D rectangle at Z=0 for this specific piece
                    pts = [
                        Rhino.Geometry.Point3d(bbox.Min.X, bbox.Min.Y, 0),
                        Rhino.Geometry.Point3d(bbox.Max.X, bbox.Min.Y, 0),
                        Rhino.Geometry.Point3d(bbox.Max.X, bbox.Max.Y, 0),
                        Rhino.Geometry.Point3d(bbox.Min.X, bbox.Max.Y, 0),
                        Rhino.Geometry.Point3d(bbox.Min.X, bbox.Min.Y, 0)
                    ]
                    rect = Rhino.Geometry.PolylineCurve(pts)
                    
                    if top_level_id not in cluster_footprints:
                        cluster_footprints[top_level_id] = []
                    cluster_footprints[top_level_id].append(rect)

    # Step A: Gather all footprints
    it = sc.doc.Objects.GetEnumerator()
    found_source_objs = 0
    for rh_obj in it:
        if rh_obj.Attributes.LayerIndex == source_layer_index:
            found_source_objs += 1
            process_geometry(rh_obj.Id, rh_obj.Id)
            
    # Step B: Union and Extrude each cluster
    for top_level_id, rectangles in cluster_footprints.items():
        height_data = cluster_heights.get(top_level_id)
        if not height_data: continue
        
        height = height_data['max_z'] - height_data['min_z']
        if height <= 0.01: 
            error_count[0] += 1
            continue
            
        # Boolean Union all 2D rectangles
        if len(rectangles) == 1:
            final_footprint = rectangles[0]
        else:
            unioned = Rhino.Geometry.Curve.CreateBooleanUnion(rectangles, 0.01)
            if unioned and len(unioned) > 0:
                # Get the largest outer boundary
                max_area = -1
                final_footprint = None
                for crv in unioned:
                    amp = Rhino.Geometry.AreaMassProperties.Compute(crv)
                    if amp and amp.Area > max_area:
                        max_area = amp.Area
                        final_footprint = crv
            else:
                final_footprint = None
                
        if final_footprint:
            # Move to correct Z height
            final_footprint.Translate(Rhino.Geometry.Vector3d(0, 0, height_data['min_z']))
            
            extrusion = Rhino.Geometry.Extrusion.Create(final_footprint, height, True)
            if extrusion:
                final_brep = extrusion.ToBrep()
                if final_brep:
                    attr = Rhino.DocObjects.ObjectAttributes()
                    attr.LayerIndex = layer_index
                    attr.UserDictionary.Set("SourceID", str(top_level_id))
                    res_id = sc.doc.Objects.AddBrep(final_brep, attr)
                    if res_id != System.Guid.Empty:
                        processed_count[0] += 1
                        continue
        error_count[0] += 1

            
    sc.doc.Views.Redraw()
    print("SUMMARY: Found {0} source objects.".format(found_source_objs))
    print("SUMMARY: Successfully baked {0} Extrusions.".format(processed_count[0]))
    print("SUMMARY: Failed to bake {0} objects.".format(error_count[0]))
    print("--- DIAGNOSTIC END ---")

if __name__ == "__main__":
    verify_ladybug_transformation()
