using System;
using System.Collections.Generic;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace EnvAnalysisCore
{
    /// <summary>
    /// Layer 2: Document Interface (Stateful Bridge)
    /// Responsible for interacting with the Rhino Document, parsing layers, 
    /// and handling blocks. Maps RhinoObjects to AnalysisGeometry.
    /// </summary>
    public class DocumentInterface
    {
        private readonly RhinoDoc _doc;

        public DocumentInterface(RhinoDoc doc)
        {
            _doc = doc;
        }

        /// <summary>
        /// Crawls the document for "Source" geometry based on strict layer conventions.
        /// Handles nested blocks using the "Inherit from Instance" rule.
        /// </summary>
        public List<AnalysisGeometry> IngestSourceGeometry(string rootLayerName)
        {
            var results = new List<AnalysisGeometry>();
            var rootLayer = _doc.Layers.FindName(rootLayerName);
            
            if (rootLayer == null) return results;

            // Find all objects on the root layer and its sub-layers
            var settings = new ObjectEnumeratorSettings
            {
                LayerIndexFilter = rootLayer.Index,
                NormalObjects = true,
                LockedObjects = true,
                ActiveObjects = true
            };

            var rhObjs = _doc.Objects.FindByLayer(rootLayerName);
            if (rhObjs == null) return results;

            foreach (var rhObj in rhObjs)
            {
                ProcessObject(rhObj, results, rootLayerName);
            }

            return results;
        }

        private void ProcessObject(RhinoObject rhObj, List<AnalysisGeometry> results, string contextLayer)
        {
            if (rhObj is InstanceObject blockInstance)
            {
                // Rule: Inherit from Instance
                // All geometry inside this block inherits the role of the instance's layer
                TraverseBlock(blockInstance, results, contextLayer);
            }
            else
            {
                var analysisGeo = MapToAnalysisGeometry(rhObj, contextLayer);
                if (analysisGeo != null) results.Add(analysisGeo);
            }
        }

        private void TraverseBlock(InstanceObject instance, List<AnalysisGeometry> results, string contextLayer)
        {
            var definition = instance.InstanceDefinition;
            if (definition == null) return;

            var transform = instance.InstanceXform;
            var innerObjects = definition.GetObjects();

            foreach (var innerObj in innerObjects)
            {
                // Apply block transformation to geometry
                var geometry = innerObj.Geometry.Duplicate();
                geometry.Transform(transform);

                if (innerObj is InstanceObject nestedInstance)
                {
                    // Recursively handle nested blocks
                    // Note: In a real implementation, we'd need to chain transformations
                    // This is a simplified version for the core logic structure
                    TraverseBlock(nestedInstance, results, contextLayer);
                }
                else
                {
                    results.Add(new AnalysisGeometry
                    {
                        Geometry = geometry,
                        SourceIds = new List<Guid> { innerObj.Id, instance.Id },
                        Type = DetermineTypeFromHeuristics(geometry, contextLayer)
                    });
                }
            }
        }

        private AnalysisGeometry MapToAnalysisGeometry(RhinoObject rhObj, string layerName)
        {
            return new AnalysisGeometry
            {
                Geometry = rhObj.Geometry.Duplicate(),
                SourceIds = new List<Guid> { rhObj.Id },
                Type = DetermineTypeFromHeuristics(rhObj.Geometry, layerName)
            };
        }

        private string DetermineTypeFromHeuristics(GeometryBase geo, string layerName)
        {
            // Placeholder for the "Heuristic Brain"
            // Will eventually use normal analysis to distinguish Walls/Floors/Roofs
            if (layerName.Contains("Buildings")) return "Building";
            if (layerName.Contains("Context")) return "Context";
            return "Unknown";
        }
    }
}
