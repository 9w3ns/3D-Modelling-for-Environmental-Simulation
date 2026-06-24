# Archive — Previous Approaches

This folder contains earlier implementations that were superseded by the
current `scripts/Phase1-3.cs` pipeline running in Rhino 8 ScriptEditor.

## plugin/
The C# Rhino Plugin approach (`EnvAnalysisCore`). A 3-layer architecture
(CoreLogic → DocumentInterface → Commands) that was replaced by standalone
C# scripts for faster iteration in Rhino 8 ScriptEditor.

## python-scripts/
Python RhinoScript implementations of the pipeline phases and the
standalone Ladybug mass extraction algorithm (voxel-based contour slicing).
Replaced by the C# scripts.

## prototypes/
Earliest experiments (BBox-based verification scripts).

## docs/
Superseded documentation and development decision logs.
