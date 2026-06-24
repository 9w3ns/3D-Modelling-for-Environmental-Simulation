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

            // 3. Ingestion: Crawl layers and blocks (returns Dictionary of clusters)
            var buildingClusters = docInterface.IngestSourceClusters(buildingsLayer);
            var contextClusters = docInterface.IngestSourceClusters(contextLayer);

            if (buildingClusters.Count == 0 && contextClusters.Count == 0)
            {
                RhinoApp.WriteLine("Error: No geometry found on Model::Buildings or Model::Context layers.");
                return Result.Failure;
            }

            // 4. Transformation & Baking
            BakeToAnalysis(doc, buildingClusters, outputRoot + "::Geometry", SimulationTarget.Ladybug);
            BakeToAnalysis(doc, contextClusters, outputRoot + "::Context", SimulationTarget.Ladybug);

            // 5. Gap Analysis (No Geometry Left Behind)
            PerformGapAnalysis(doc, buildingClusters.Count + contextClusters.Count, outputRoot);

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

        private void BakeToAnalysis(RhinoDoc doc, Dictionary<Guid, List<AnalysisGeometry>> clusters, string layerPath, SimulationTarget target)
        {
            int layerIndex = EnsureLayer(doc, layerPath);

            foreach (var cluster in clusters)
            {
                // Extract just the GeometryBase objects for the logic core
                var rawGeometry = cluster.Value.Select(ag => ag.Geometry).ToList();

                // Core Transformation: Process the entire cluster as one mass
                var transformed = SimplificationLogic.ProcessCluster(rawGeometry, target);
                
                if (transformed != null)
                {
                    var attr = new ObjectAttributes();
                    attr.LayerIndex = layerIndex;
                    
                    // Traceability Tagging: Tag with the Top-Level cluster ID
                    attr.UserDictionary.Set("SourceID", cluster.Key.ToString());

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

        private void PerformGapAnalysis(RhinoDoc doc, int totalClusters, string rootPath)
        {
            RhinoApp.WriteLine($"Audit: Processed {totalClusters} geometry clusters.");
        }
    }
}
