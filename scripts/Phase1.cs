

using System;
using System.Collections.Generic;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

// ==============================================================================
// PHASE 1: INGESTION AND BASE FILTERING (STANDALONE SCRIPT)
// Run this directly in Rhino 8 ScriptEditor
// ==============================================================================

var doc = RhinoDoc.ActiveDoc;
RhinoApp.WriteLine("Starting Phase 1 (Script Mode): Ingestion and Base Filtering...");

string[] inputLayers = new[] { "Target Geometry", "Model::Buildings", "Model::Context" };
string outputRoot = "Analysis::Phase1::Ingested";

CleanAnalysisLayers(doc, outputRoot);

var sourceClusters = Phase1_IngestGeometry(doc, inputLayers);
if (sourceClusters.Count == 0)
{
    RhinoApp.WriteLine("Error: No geometry found on source layers.");
    return;
}

int count = 0;
int droppedCount = 0;
foreach (var cluster in sourceClusters)
{
    var validGeometries = new List<RawGeometryInfo>();
    foreach(var info in cluster.Value)
    {
        if (info.Geometry == null) continue;
        
        // -------------------------------------------------------------
        // NEW FILTER: Purge detailed/small elements (Area < 2.0 m2)
        // -------------------------------------------------------------
        var bbox = info.Geometry.GetBoundingBox(true);
        if (!bbox.IsValid) continue;
        
        double dx = bbox.Max.X - bbox.Min.X;
        double dy = bbox.Max.Y - bbox.Min.Y;
        double dz = bbox.Max.Z - bbox.Min.Z;
        
        // Use Bounding Box surface area as a fast pre-filter. 
        // Bounding Box Area is always >= True Area.
        double bboxArea = 2.0 * ((dx * dy) + (dy * dz) + (dx * dz));
        
        if (bboxArea < 2.0) 
        {
            droppedCount++;
            continue; // Fast drop!
        }
        
        // If the bounding box is large (e.g. a diagonally oriented pipe, or a hollow frame), 
        // we must compute the True Area to see if it's actually small.
        double trueArea = bboxArea;
        if (info.Geometry is Brep b) trueArea = Rhino.Geometry.AreaMassProperties.Compute(b)?.Area ?? bboxArea;
        else if (info.Geometry is Extrusion e) trueArea = Rhino.Geometry.AreaMassProperties.Compute(e.ToBrep())?.Area ?? bboxArea;
        else if (info.Geometry is Mesh m) trueArea = Rhino.Geometry.AreaMassProperties.Compute(m)?.Area ?? bboxArea;
        else if (info.Geometry is Surface s) trueArea = Rhino.Geometry.AreaMassProperties.Compute(s)?.Area ?? bboxArea;

        if (trueArea < 2.0)
        {
            droppedCount++;
            continue; // True drop!
        }
        
        validGeometries.Add(info);
    }
    
    BakePhase1Geometry(doc, validGeometries, outputRoot, cluster.Key);
    count += validGeometries.Count;
}

doc.Views.Redraw();
RhinoApp.WriteLine($"Phase 1 complete. Ingested {count} base geometries.");
if (droppedCount > 0) RhinoApp.WriteLine($"Filtered and dropped {droppedCount} small details (mullions/hardware).");

// ==============================================================================
// HELPER CLASSES AND METHODS
// ==============================================================================

class RawGeometryInfo
{
    public GeometryBase Geometry;
    public string LayerName;
    public string MaterialName;
    public Guid SourceId;
}

Dictionary<Guid, List<RawGeometryInfo>> Phase1_IngestGeometry(RhinoDoc d, string[] layers)
{
    var clusters = new Dictionary<Guid, List<RawGeometryInfo>>();
    
    foreach (var layerName in layers)
    {
        int lIdx = d.Layers.FindByFullPath(layerName, RhinoMath.UnsetIntIndex);
        if (lIdx < 0) continue;

        var rhObjs = d.Objects.FindByLayer(d.Layers[lIdx]);
        if (rhObjs == null) continue;

        foreach (var rhObj in rhObjs)
        {
            var list = new List<RawGeometryInfo>();
            ProcessObjectForIngestion(d, rhObj, rhObj.Id, layerName, list);
            if (list.Count > 0) clusters[rhObj.Id] = list;
        }
    }
    return clusters;
}

void ProcessObjectForIngestion(RhinoDoc d, RhinoObject rhObj, Guid parentId, string layerName, List<RawGeometryInfo> results)
{
    if (rhObj is InstanceObject blockInstance)
    {
        TraverseBlock(d, blockInstance, Transform.Identity, parentId, layerName, results);
    }
    else
    {
        string matName = null;
        if (rhObj.Attributes.MaterialIndex >= 0)
        {
            var mat = d.Materials[rhObj.Attributes.MaterialIndex];
            if (mat != null) matName = mat.Name;
        }
        
        results.Add(new RawGeometryInfo
        {
            Geometry = rhObj.Geometry.Duplicate(),
            LayerName = layerName,
            MaterialName = matName,
            SourceId = parentId
        });
    }
}

void TraverseBlock(RhinoDoc d, InstanceObject instance, Transform parentTransform, Guid parentId, string layerName, List<RawGeometryInfo> results)
{
    var definition = instance.InstanceDefinition;
    if (definition == null) return;

    var currentTransform = parentTransform * instance.InstanceXform;
    foreach (var innerObj in definition.GetObjects())
    {
        if (innerObj is InstanceObject nestedInstance)
        {
            TraverseBlock(d, nestedInstance, currentTransform, parentId, layerName, results);
        }
        else
        {
            var geometry = innerObj.Geometry.Duplicate();
            geometry.Transform(currentTransform);

            string matName = null;
            if (innerObj.Attributes.MaterialIndex >= 0)
            {
                var mat = d.Materials[innerObj.Attributes.MaterialIndex];
                if (mat != null) matName = mat.Name;
            }

            results.Add(new RawGeometryInfo
            {
                Geometry = geometry,
                LayerName = layerName,
                MaterialName = matName,
                SourceId = parentId
            });
        }
    }
}

void BakePhase1Geometry(RhinoDoc d, List<RawGeometryInfo> infos, string layerPath, Guid clusterId)
{
    int layerIndex = EnsureLayer(d, layerPath);

    foreach (var info in infos)
    {
        if (info.Geometry != null)
        {
            var attr = new ObjectAttributes();
            attr.LayerIndex = layerIndex;
            attr.UserDictionary.Set("SourceID", clusterId.ToString());
            if (info.LayerName != null) attr.UserDictionary.Set("OriginalLayer", info.LayerName);
            if (info.MaterialName != null) attr.UserDictionary.Set("OriginalMaterial", info.MaterialName);
            d.Objects.Add(info.Geometry, attr);
        }
    }
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
