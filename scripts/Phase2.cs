using System;
using System.Collections.Generic;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

// ==============================================================================
// PHASE 2: SEMANTIC TAGGING (STANDALONE SCRIPT)
// Run this directly in Rhino 8 ScriptEditor
// ==============================================================================

var doc = RhinoDoc.ActiveDoc;
RhinoApp.WriteLine("Starting Phase 2 (Script Mode): Semantic Tagging...");

string inputLayer = "Analysis::Phase1::Ingested";
string outputRoot = "Analysis::Phase2";

CleanAnalysisLayers(doc, outputRoot + "::Floors");
CleanAnalysisLayers(doc, outputRoot + "::Walls");
CleanAnalysisLayers(doc, outputRoot + "::Apertures");
CleanAnalysisLayers(doc, outputRoot + "::Shading");
CleanAnalysisLayers(doc, outputRoot + "::Context");

int inputLayerIdx = doc.Layers.FindByFullPath(inputLayer, RhinoMath.UnsetIntIndex);
if (inputLayerIdx < 0)
{
    RhinoApp.WriteLine($"Error: Input layer '{inputLayer}' not found. Run Phase 1 first.");
    return;
}

var rhObjs = doc.Objects.FindByLayer(doc.Layers[inputLayerIdx]);
if (rhObjs == null || rhObjs.Length == 0)
{
    RhinoApp.WriteLine($"Error: No objects found on '{inputLayer}'.");
    return;
}

int count = 0;
foreach (var rhObj in rhObjs)
{
    var role = CategorizeGeometry(rhObj);
    string targetLayer = GetTargetLayer(outputRoot, role);
    
    Guid sourceId = Guid.Empty;
    if (rhObj.Attributes.UserDictionary.ContainsKey("SourceID"))
    {
        Guid.TryParse(rhObj.Attributes.UserDictionary.GetString("SourceID"), out sourceId);
    }

    BakeGeometry(doc, rhObj.Geometry, targetLayer, sourceId);
    count++;
}

doc.Views.Redraw();
RhinoApp.WriteLine($"Phase 2 complete. Categorized {count} geometries.");

// ==============================================================================
// HELPER CLASSES AND METHODS
// ==============================================================================

enum SemanticRole
{
    Unknown,
    Wall,
    Floor,
    Roof,
    Aperture,
    Shading,
    Context
}

SemanticRole CategorizeGeometry(RhinoObject rhObj)
{
    var geo = rhObj.Geometry;
    rhObj.Attributes.UserDictionary.TryGetString("OriginalLayer", out string origLayer);
    rhObj.Attributes.UserDictionary.TryGetString("OriginalMaterial", out string origMat);
    origLayer = origLayer ?? "";
    origMat = origMat ?? "";

    if (origLayer.IndexOf("Context", StringComparison.OrdinalIgnoreCase) >= 0)
        return SemanticRole.Context;

    if (origLayer.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0 || 
        origLayer.IndexOf("Glazing", StringComparison.OrdinalIgnoreCase) >= 0 ||
        origMat.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0 || 
        origMat.IndexOf("Transparent", StringComparison.OrdinalIgnoreCase) >= 0)
    {
        return SemanticRole.Aperture;
    }

    var bbox = geo.GetBoundingBox(true);
    if (!bbox.IsValid) return SemanticRole.Unknown;

    double dx = bbox.Max.X - bbox.Min.X;
    double dy = bbox.Max.Y - bbox.Min.Y;
    double dz = bbox.Max.Z - bbox.Min.Z;

    SemanticRole role = SemanticRole.Unknown;

    if (geo is Brep brep && brep.Faces.Count > 0)
    {
        BrepFace largestFace = null;
        double maxArea = -1;
        foreach (var face in brep.Faces)
        {
            var amp = AreaMassProperties.Compute(face);
            if (amp != null && amp.Area > maxArea)
            {
                maxArea = amp.Area;
                largestFace = face;
            }
        }
        if (largestFace != null)
        {
            role = ClassifySurface(largestFace);
        }
    }
    else if (geo is Extrusion ext)
    {
        var b = ext.ToBrep();
        if (b != null && b.Faces.Count > 0)
        {
            BrepFace largestFace = null;
            double maxArea = -1;
            foreach (var face in b.Faces)
            {
                var amp = AreaMassProperties.Compute(face);
                if (amp != null && amp.Area > maxArea)
                {
                    maxArea = amp.Area;
                    largestFace = face;
                }
            }
            if (largestFace != null)
            {
                role = ClassifySurface(largestFace);
            }
        }
    }

    if (role == SemanticRole.Unknown)
    {
        if (dz >= 0.05 && dz <= 0.6 && dx > dz && dy > dz) role = SemanticRole.Floor;
        else if ((dx <= 0.5 || dy <= 0.5) && dz > 1.0) role = SemanticRole.Wall;
        else role = SemanticRole.Shading;
    }

    // OVERRIDE FOR THICK WALL MASSES
    // If a boxy geometry was categorized as a Floor/Roof (because its top face was 
    // the largest), but it is taller than 1.0 meter, it is actually a Wall mass!
    if ((role == SemanticRole.Floor || role == SemanticRole.Roof) && dz > 1.0)
    {
        role = SemanticRole.Wall;
    }

    return role;
}

SemanticRole ClassifySurface(Surface surface)
{
    if (surface == null) return SemanticRole.Unknown;

    Vector3d normal = surface.NormalAt(surface.Domain(0).Mid, surface.Domain(1).Mid);
    double angleToUp = Vector3d.VectorAngle(normal, Vector3d.ZAxis);

    if (angleToUp <= Math.PI / 9) return SemanticRole.Roof;
    if (angleToUp >= 8 * Math.PI / 9) return SemanticRole.Floor;
    if (angleToUp >= 7 * Math.PI / 18 && angleToUp <= 11 * Math.PI / 18) return SemanticRole.Wall;

    return SemanticRole.Wall; // Default to wall for slightly tilted surfaces
}

string GetTargetLayer(string root, SemanticRole role)
{
    switch (role)
    {
        case SemanticRole.Wall: return root + "::Walls";
        case SemanticRole.Floor: return root + "::Floors";
        case SemanticRole.Aperture: return root + "::Apertures";
        case SemanticRole.Roof: return root + "::Roofs";
        case SemanticRole.Shading: return root + "::Shading";
        case SemanticRole.Context: return root + "::Context";
        default: return root + "::Unknown";
    }
}

void BakeGeometry(RhinoDoc d, GeometryBase geo, string layerPath, Guid sourceId)
{
    int layerIndex = EnsureLayer(d, layerPath);
    var attr = new ObjectAttributes();
    attr.LayerIndex = layerIndex;
    if (sourceId != Guid.Empty)
    {
        attr.UserDictionary.Set("SourceID", sourceId.ToString());
    }
    d.Objects.Add(geo, attr);
}

void CleanAnalysisLayers(RhinoDoc d, string path)
{
    int layerIndex = d.Layers.FindByFullPath(path, RhinoMath.UnsetIntIndex);
    if (layerIndex >= 0)
    {
        var layer = d.Layers[layerIndex];
        var objects = d.Objects.FindByLayer(layer);
        if (objects != null)
        {
            foreach (var obj in objects) d.Objects.Delete(obj, true);
        }
    }
}

int EnsureLayer(RhinoDoc d, string path)
{
    int layerIndex = d.Layers.FindByFullPath(path, RhinoMath.UnsetIntIndex);
    if (layerIndex >= 0) return layerIndex;

    string[] parts = path.Split(new[] { "::" }, StringSplitOptions.None);
    int parentIndex = -1;
    string currentPath = "";

    for (int i = 0; i < parts.Length; i++)
    {
        currentPath = i == 0 ? parts[i] : currentPath + "::" + parts[i];
        int existingIndex = d.Layers.FindByFullPath(currentPath, RhinoMath.UnsetIntIndex);

        if (existingIndex >= 0)
        {
            parentIndex = existingIndex;
        }
        else
        {
            var newLayer = new Layer { Name = parts[i], Color = System.Drawing.Color.Gray };
            if (parentIndex != -1) newLayer.ParentLayerId = d.Layers[parentIndex].Id;
            parentIndex = d.Layers.Add(newLayer);
        }
    }
    return parentIndex;
}
