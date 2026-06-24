import Rhino
import Rhino.Geometry as rg
import System

doc = __rhino_doc__

# 1. Get objects from layer "context 2"
layer_index = doc.Layers.FindByFullPath("context 2", -1)
if layer_index < 0:
    print("Layer 'context 2' not found")
else:
    objs = doc.Objects.FindByLayer("context 2")
    meshes = []
    for obj in objs:
        if obj.ObjectType == Rhino.DocObjects.ObjectType.Mesh:
            meshes.append(obj.Geometry)
        elif obj.ObjectType == Rhino.DocObjects.ObjectType.Brep:
            brep_meshes = rg.Mesh.CreateFromBrep(obj.Geometry, rg.MeshingParameters.FastRenderMesh)
            if brep_meshes:
                for m in brep_meshes:
                    meshes.append(m)
        elif obj.ObjectType == Rhino.DocObjects.ObjectType.Extrusion:
            mesh = obj.Geometry.GetMesh(rg.MeshType.Any)
            if mesh:
                meshes.append(mesh)

    print("Found {} meshes/objects in 'context 2'".format(len(meshes)))

    # Height tiering: pre-sort into 10-meter height bins based on max Z
    bins = {}
    for m in meshes:
        bbox = m.GetBoundingBox(True)
        z_max = bbox.Max.Z
        z_min = bbox.Min.Z
        
        # Determine 10m height bin
        bin_idx = int(z_max // 10)
        if bin_idx not in bins:
            bins[bin_idx] = []
        bins[bin_idx].append({'mesh': m, 'z_max': z_max, 'z_min': z_min})

    # create new layer 'CFD_Context2'
    out_layer_name = "CFD_Context2"
    out_layer_idx = doc.Layers.FindByFullPath(out_layer_name, -1)
    if out_layer_idx < 0:
        new_layer = Rhino.DocObjects.Layer()
        new_layer.Name = out_layer_name
        new_layer.Color = System.Drawing.Color.Red
        out_layer_idx = doc.Layers.Add(new_layer)

    tol = doc.ModelAbsoluteTolerance

    for bin_idx, items in bins.items():
        print("Processing bin {} (approx {}m - {}m) with {} items".format(bin_idx, bin_idx*10, (bin_idx+1)*10, len(items)))
        
        overall_z_max = max([item['z_max'] for item in items])
        overall_z_min = min([item['z_min'] for item in items])
        
        # a. Extract mesh outlines & project to Z=0
        outlines = []
        for item in items:
            m = item['mesh']
            polylines = m.GetOutlines(rg.Plane.WorldXY)
            if polylines:
                for polyline in polylines:
                    # project to z=0
                    for i in range(polyline.Count):
                        pt = polyline[i]
                        polyline[i] = rg.Point3d(pt.X, pt.Y, 0)
                    if polyline.IsValid:
                        crv = polyline.ToPolylineCurve()
                        outlines.append(crv)
                        
        if not outlines:
            continue
            
        print("  Found {} outlines".format(len(outlines)))

        # 1. Dilation: offset out by 5m
        dilated_curves = []
        for crv in outlines:
            if crv.ClosedCurveOrientation(rg.Plane.WorldXY) == rg.CurveOrientation.Clockwise:
                crv.Reverse()
                
            offsets = crv.Offset(rg.Plane.WorldXY, 5.0, tol, rg.CurveOffsetCornerStyle.Round)
            if offsets:
                for off_c in offsets:
                    if off_c.ClosedCurveOrientation(rg.Plane.WorldXY) == rg.CurveOrientation.Clockwise:
                        off_c.Reverse()
                    dilated_curves.append(off_c)

        if not dilated_curves:
            continue
            
        print("  Dilated to {} curves".format(len(dilated_curves)))

        # 2. Incremental Union (Area sorted largest to smallest)
        dilated_with_area = []
        for crv in dilated_curves:
            amp = rg.AreaMassProperties.Compute(crv)
            area = amp.Area if amp else 0
            dilated_with_area.append((area, crv))
        
        dilated_with_area.sort(key=lambda x: x[0], reverse=True)
        
        current_union = [dilated_with_area[0][1]]
        for i in range(1, len(dilated_with_area)):
            crv = dilated_with_area[i][1]
            try:
                res = rg.Curve.CreateBooleanUnion(current_union + [crv], tol)
                if res and len(res) > 0:
                    current_union = list(res)
                else:
                    current_union.append(crv)
            except Exception as e:
                current_union.append(crv)

        print("  Unioned to {} curves".format(len(current_union)))
                
        # 3. Erosion: offset in by 5m (-5.0 distance)
        eroded_curves = []
        for union_crv in current_union:
            if union_crv.ClosedCurveOrientation(rg.Plane.WorldXY) == rg.CurveOrientation.Clockwise:
                union_crv.Reverse()
            offsets = union_crv.Offset(rg.Plane.WorldXY, -5.0, tol, rg.CurveOffsetCornerStyle.Round)
            if offsets:
                for off_c in offsets:
                    if off_c.ClosedCurveOrientation(rg.Plane.WorldXY) == rg.CurveOrientation.Clockwise:
                        off_c.Reverse()
                    eroded_curves.append(off_c)

        print("  Eroded to {} curves".format(len(eroded_curves)))
                    
        # Extrude and cap
        for crv in eroded_curves:
            crv.Translate(rg.Vector3d(0, 0, overall_z_min))
            height = overall_z_max - overall_z_min
            if height <= 0:
                continue
                
            extrusion = rg.Extrusion.Create(crv, height, True)
            brep = None
            if extrusion:
                brep = extrusion.ToBrep()
            else:
                sweep = rg.Surface.CreateExtrusion(crv, rg.Vector3d(0, 0, height))
                if sweep:
                    brep = sweep.ToBrep()
                    brep = brep.CapPlanarHoles(tol)

            if brep:
                final_meshes = rg.Mesh.CreateFromBrep(brep, rg.MeshingParameters.FastRenderMesh)
                if final_meshes and len(final_meshes) > 0:
                    final_mesh = rg.Mesh()
                    for m in final_meshes:
                        final_mesh.Append(m)
                    
                    attr = Rhino.DocObjects.ObjectAttributes()
                    attr.LayerIndex = out_layer_idx
                    doc.Objects.AddMesh(final_mesh, attr)
                else:
                    attr = Rhino.DocObjects.ObjectAttributes()
                    attr.LayerIndex = out_layer_idx
                    doc.Objects.AddBrep(brep, attr)
            else:
                print("  Failed to extrude or create brep")

print("Done")
