using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace EnvAnalysisCore
{
    public class EnvPrepPhase2Command : Command
    {
        public override string EnglishName => "EnvPrepPhase2";

        public enum SemanticRole
        {
            Unknown,
            Wall,
            Floor,
            Roof,
            Aperture,
            Shading,
            Context
        }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            RhinoApp.WriteLine("Starting Phase 2: Semantic Tagging...");

            string inputLayer = "Analysis::Phase1::Ingested";
            string outputRoot = "Analysis::Phase2";

            CleanAnalysisLayers(doc, outputRoot + "::Floors");
            CleanAnalysisLayers(doc, outputRoot + "::Walls");
            CleanAnalysisLayers(doc, outputRoot + "::Apertures");
            CleanAnalysisLayers(doc, outputRoot + "::Shading");
            CleanAnalysisLayers(doc, outputRoot + "::Context");

            var layer = doc.Layers.FindName(inputLayer);
            if (layer == null)
            {
                RhinoApp.WriteLine($"Error: Input layer '{inputLayer}' not found. Run Phase 1 first.");
                return Result.Failure;
            }

            var rhObjs = doc.Objects.FindByLayer(inputLayer);
            if (rhObjs == null || rhObjs.Length == 0)
            {
                RhinoApp.WriteLine($"Error: No objects found on '{inputLayer}'.");
                return Result.Failure;
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
            return Result.Success;
        }

        private SemanticRole CategorizeGeometry(RhinoObject rhObj)
        {
            var geo = rhObj.Geometry;
            string origLayer = rhObj.Attributes.UserDictionary.GetString("OriginalLayer") ?? "";
            string origMat = rhObj.Attributes.UserDictionary.GetString("OriginalMaterial") ?? "";

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
                    var coreRole = SimplificationLogic.ClassifySurface(largestFace);
                    switch(coreRole)
                    {
                        case GeometryRole.Wall: role = SemanticRole.Wall; break;
                        case GeometryRole.Floor: role = SemanticRole.Floor; break;
                        case GeometryRole.Roof: role = SemanticRole.Roof; break;
                    }
                }
            }

            if (role == SemanticRole.Unknown)
            {
                if (dz >= 0.05 && dz <= 0.6 && dx > dz && dy > dz) role = SemanticRole.Floor;
                else if ((dx <= 0.5 || dy <= 0.5) && dz > 1.0) role = SemanticRole.Wall;
                else role = SemanticRole.Shading;
            }

            return role;
        }

        private string GetTargetLayer(string root, SemanticRole role)
        {
            switch (role)
            {
                case SemanticRole.Wall: return root + "::Walls";
                case SemanticRole.Floor: return root + "::Floors";
                case SemanticRole.Aperture: return root + "::Apertures";
                case SemanticRole.Context: return root + "::Context";
                default: return root + "::Shading";
            }
        }

        private void BakeGeometry(RhinoDoc doc, GeometryBase geo, string layerPath, Guid sourceId)
        {
            if (geo == null) return;
            int layerIndex = EnsureLayer(doc, layerPath);

            var attr = new ObjectAttributes();
            attr.LayerIndex = layerIndex;
            if (sourceId != Guid.Empty) attr.UserDictionary.Set("SourceID", sourceId.ToString());
            
            doc.Objects.Add(geo, attr);
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
