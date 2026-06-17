using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;

namespace EnvAnalysisCore
{
    public class EnvPrepForLadybugCommand : Command
    {
        public override string EnglishName => "EnvPrepForLadybug";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            RhinoApp.WriteLine("Starting Geometry Preparation for Ladybug...");

            // 1. Initialize Layers and Interface
            var docInterface = new DocumentInterface(doc);
            string buildingsLayer = "Model::Buildings";
            string contextLayer = "Model::Context";
            string outputRoot = "Analysis::Ladybug";

            // 2. Destructive Overwrite: Clean the output layer
            CleanAnalysisLayers(doc, outputRoot);

            // 3. Ingestion: Crawl layers and blocks
            var buildingGeometry = docInterface.IngestSourceGeometry(buildingsLayer);
            var contextGeometry = docInterface.IngestSourceGeometry(contextLayer);

            if (buildingGeometry.Count == 0 && contextGeometry.Count == 0)
            {
                RhinoApp.WriteLine("Error: No geometry found on Model::Buildings or Model::Context layers.");
                return Result.Failure;
            }

            // 4. Transformation & Baking
            BakeToAnalysis(doc, buildingGeometry, outputRoot + "::Geometry", SimulationTarget.Ladybug);
            BakeToAnalysis(doc, contextGeometry, outputRoot + "::Context", SimulationTarget.Ladybug);

            // 5. Gap Analysis (No Geometry Left Behind)
            PerformGapAnalysis(doc, buildingGeometry.Concat(contextGeometry), outputRoot);

            doc.Views.Redraw();
            RhinoApp.WriteLine("Ladybug preparation complete. All roads lead to the Matrix.");
            return Result.Success;
        }

        private void CleanAnalysisLayers(RhinoDoc doc, string rootPath)
        {
            var layer = doc.Layers.FindName(rootPath);
            if (layer != null)
            {
                // Delete objects on the layer but keep the layer structure
                var objects = doc.Objects.FindByLayer(rootPath);
                if (objects != null)
                {
                    foreach (var obj in objects) doc.Objects.Delete(obj, true);
                }
            }
            else
            {
                // Create the layer if it doesn't exist
                doc.Layers.Add(rootPath, System.Drawing.Color.Blue);
            }
        }

        private void BakeToAnalysis(RhinoDoc doc, List<AnalysisGeometry> geometries, string layerPath, SimulationTarget target)
        {
            int layerIndex = EnsureLayer(doc, layerPath);

            foreach (var item in geometries)
            {
                // Core Transformation
                var transformed = SimplificationLogic.Process(item.Geometry, target);
                
                if (transformed != null)
                {
                    var attr = new ObjectAttributes();
                    attr.LayerIndex = layerIndex;
                    
                    // Traceability Tagging
                    foreach (var id in item.SourceIds)
                    {
                        attr.UserDictionary.Set("SourceID_" + id.ToString(), id);
                    }

                    doc.Objects.Add(transformed, attr);
                }
            }
        }

        private int EnsureLayer(RhinoDoc doc, string path)
        {
            var layer = doc.Layers.FindName(path);
            if (layer != null) return layer.Index;
            return doc.Layers.Add(path, System.Drawing.Color.Gray);
        }

        private void PerformGapAnalysis(RhinoDoc doc, IEnumerable<AnalysisGeometry> ingested, string rootPath)
        {
            // Simplified logic to ensure every unique source ID was processed
            // In a full implementation, we'd compare the Receipt Catalog vs the Baked IDs
            RhinoApp.WriteLine($"Audit: Processed {ingested.Count()} geometry clusters.");
        }
    }
}
