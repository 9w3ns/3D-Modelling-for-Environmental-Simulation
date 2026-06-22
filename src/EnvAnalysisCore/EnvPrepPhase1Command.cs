using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace EnvAnalysisCore
{
    public class EnvPrepPhase1Command : Command
    {
        public override string EnglishName => "EnvPrepPhase1";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            RhinoApp.WriteLine("Starting Phase 1: Ingestion and Base Filtering...");

            string buildingsLayer = "Model::Buildings";
            string contextLayer = "Model::Context";
            string targetLayer = "Target Geometry";
            string outputRoot = "Analysis::Phase1::Ingested";

            CleanAnalysisLayers(doc, outputRoot);

            var sourceClusters = Phase1_IngestGeometry(doc, new[] { buildingsLayer, contextLayer, targetLayer });
            if (sourceClusters.Count == 0)
            {
                RhinoApp.WriteLine("Error: No geometry found on source layers.");
                return Result.Failure;
            }

            int count = 0;
            foreach (var cluster in sourceClusters)
            {
                BakePhase1Geometry(doc, cluster.Value, outputRoot, cluster.Key);
                count += cluster.Value.Count;
            }

            doc.Views.Redraw();
            RhinoApp.WriteLine($"Phase 1 complete. Ingested {count} base geometries.");
            return Result.Success;
        }

        private class RawGeometryInfo
        {
            public GeometryBase Geometry;
            public string LayerName;
            public string MaterialName;
            public Guid SourceId;
        }

        private Dictionary<Guid, List<RawGeometryInfo>> Phase1_IngestGeometry(RhinoDoc doc, string[] layers)
        {
            var clusters = new Dictionary<Guid, List<RawGeometryInfo>>();
            
            foreach (var layerName in layers)
            {
                var layer = doc.Layers.FindName(layerName);
                if (layer == null) continue;

                var rhObjs = doc.Objects.FindByLayer(layerName);
                if (rhObjs == null) continue;

                foreach (var rhObj in rhObjs)
                {
                    var list = new List<RawGeometryInfo>();
                    ProcessObjectForIngestion(doc, rhObj, rhObj.Id, layerName, list);
                    if (list.Count > 0) clusters[rhObj.Id] = list;
                }
            }
            return clusters;
        }

        private void ProcessObjectForIngestion(RhinoDoc doc, RhinoObject rhObj, Guid parentId, string layerName, List<RawGeometryInfo> results)
        {
            if (rhObj is InstanceObject blockInstance)
            {
                TraverseBlock(doc, blockInstance, Transform.Identity, parentId, layerName, results);
            }
            else
            {
                string matName = null;
                if (rhObj.Attributes.MaterialIndex >= 0)
                {
                    var mat = doc.Materials[rhObj.Attributes.MaterialIndex];
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

        private void TraverseBlock(RhinoDoc doc, InstanceObject instance, Transform parentTransform, Guid parentId, string layerName, List<RawGeometryInfo> results)
        {
            var definition = instance.InstanceDefinition;
            if (definition == null) return;

            var currentTransform = parentTransform * instance.InstanceXform;
            foreach (var innerObj in definition.GetObjects())
            {
                if (innerObj is InstanceObject nestedInstance)
                {
                    TraverseBlock(doc, nestedInstance, currentTransform, parentId, layerName, results);
                }
                else
                {
                    var geometry = innerObj.Geometry.Duplicate();
                    geometry.Transform(currentTransform);

                    string matName = null;
                    if (innerObj.Attributes.MaterialIndex >= 0)
                    {
                        var mat = doc.Materials[innerObj.Attributes.MaterialIndex];
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

        private void BakePhase1Geometry(RhinoDoc doc, List<RawGeometryInfo> infos, string layerPath, Guid clusterId)
        {
            int layerIndex = EnsureLayer(doc, layerPath);

            foreach (var info in infos)
            {
                if (info.Geometry != null)
                {
                    var attr = new ObjectAttributes();
                    attr.LayerIndex = layerIndex;
                    attr.UserDictionary.Set("SourceID", clusterId.ToString());
                    if (info.LayerName != null) attr.UserDictionary.Set("OriginalLayer", info.LayerName);
                    if (info.MaterialName != null) attr.UserDictionary.Set("OriginalMaterial", info.MaterialName);
                    doc.Objects.Add(info.Geometry, attr);
                }
            }
        }

        private void CleanAnalysisLayers(RhinoDoc doc, string path)
        {
            int layerIndex = doc.Layers.FindByFullPath(path, RhinoMath.UnsetIntIndex);
            if (layerIndex >= 0)
            {
                var layer = doc.Layers[layerIndex];
                var objects = doc.Objects.FindByLayer(layer);
                if (objects != null)
                {
                    foreach (var obj in objects) doc.Objects.Delete(obj, true);
                }
            }
        }

        private int EnsureLayer(RhinoDoc doc, string path)
        {
            int layerIndex = doc.Layers.FindByFullPath(path, RhinoMath.UnsetIntIndex);
            if (layerIndex >= 0) return layerIndex;

            string[] parts = path.Split(new[] { "::" }, StringSplitOptions.None);
            int parentIndex = -1;
            string currentPath = "";

            for (int i = 0; i < parts.Length; i++)
            {
                currentPath = i == 0 ? parts[i] : currentPath + "::" + parts[i];
                int existingIndex = doc.Layers.FindByFullPath(currentPath, RhinoMath.UnsetIntIndex);

                if (existingIndex >= 0)
                {
                    parentIndex = existingIndex;
                }
                else
                {
                    var newLayer = new Layer { Name = parts[i], Color = System.Drawing.Color.Gray };
                    if (parentIndex != -1) newLayer.ParentLayerId = doc.Layers[parentIndex].Id;
                    parentIndex = doc.Layers.Add(newLayer);
                }
            }
            return parentIndex;
        }
    }
}
