import Rhino
import scriptcontext as sc
import System
import os
import uuid

doc = __rhino_doc__

# Layers to export
layers_to_export = ["Simplified 5", "CFD_Context2"]
export_dir = r"C:\Users\9w3n\Desktop\3D Modelling for Environmental Simulation\3D-Modelling-for-Environmental-Simulation"

for layer_name in layers_to_export:
    layer_idx = doc.Layers.FindByFullPath(layer_name, -1)
    if layer_idx >= 0:
        objs = doc.Objects.FindByLayer(layer_name)
        if not objs:
            continue
            
        # Unselect all first
        doc.Objects.UnselectAll()
        
        for obj in objs:
            obj.Select(True)
            
        doc.Views.Redraw()
        
        # Generate short hex ID
        short_id = uuid.uuid4().hex[:6]
        filename = "GeometryforCFD_{}_{}.stl".format(layer_name.replace(" ", ""), short_id)
        filepath = os.path.join(export_dir, filename)
        
        # Command string for STL export
        cmd = "_-Export \"{}\" _Enter _DetailedOptions _AdvancedOptions _Angle=15 _AspectRatio=0 _Distance=0.01 _Density=0.5 _Grid=16 _MaxEdgeLength=0 _MinEdgeLength=0.0001 _Enter _Enter".format(filepath)
        
        Rhino.RhinoApp.RunScript(cmd, False)
        print("Exported {} to {}".format(layer_name, filename))

doc.Objects.UnselectAll()
print("Export complete.")
