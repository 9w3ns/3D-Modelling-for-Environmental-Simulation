using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace EnvAnalysisCore
{
    public class EnvPrepPhase3Command : Command
    {
        public override string EnglishName => "EnvPrepPhase3";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            RhinoApp.WriteLine("Starting Phase 3: Shrinkwrap Reconstruction...");

            string inputRoot = "Analysis::Phase2";
            string outputRoot = "Analysis::Phase3";

            CleanAnalysisLayers(doc, outputRoot + "::Floors");
            CleanAnalysisLayers(doc, outputRoot + "::Walls");
            CleanAnalysisLayers(doc, outputRoot + "::Apertures");
            CleanAnalysisLayers(doc, outputRoot + "::Shading");
            CleanAnalysisLayers(doc, outputRoot + "::Context");

            var layerPaths = new string[] 
            { 
                inputRoot + "::Walls", 
                inputRoot + "::Floors", 
                inputRoot + "::Apertures", 
                inputRoot + "::Shading",
                inputRoot + "::Context"
            };

            var clusterDict = new Dictionary<Guid, Dictionary<string, List<GeometryBase>>>();

            foreach (var path in layerPaths)
            {
                var rhObjs = doc.Objects.FindByLayer(path);
                if (rhObjs == null) continue;

                string role = path.Split(new string[] { "::" }, StringSplitOptions.None).Last();

                foreach (var rhObj in rhObjs)
                {
                    Guid sourceId = Guid.Empty;
                    if (rhObj.Attributes.UserDictionary.ContainsKey("SourceID"))
                    {
                        Guid.TryParse(rhObj.Attributes.UserDictionary.GetString("SourceID"), out sourceId);
                    }

                    if (!clusterDict.ContainsKey(sourceId))
                        clusterDict[sourceId] = new Dictionary<string, List<GeometryBase>>();

                    if (!clusterDict[sourceId].ContainsKey(role))
                        clusterDict[sourceId][role] = new List<GeometryBase>();

                    clusterDict[sourceId][role].Add(rhObj.Geometry.Duplicate());
                }
            }

            if (clusterDict.Count == 0)
            {
                RhinoApp.WriteLine($"Error: No Phase 2 geometry found under '{inputRoot}'.");
                return Result.Failure;
            }

            foreach (var kvp in clusterDict)
            {
                var clusterId = kvp.Key;
                var roles = kvp.Value;

                var walls = roles.ContainsKey("Walls") ? roles["Walls"] : new List<GeometryBase>();
                var floors = roles.ContainsKey("Floors") ? roles["Floors"] : new List<GeometryBase>();
                var apertures = roles.ContainsKey("Apertures") ? roles["Apertures"] : new List<GeometryBase>();
                var shading = roles.ContainsKey("Shading") ? roles["Shading"] : new List<GeometryBase>();
                var context = roles.ContainsKey("Context") ? roles["Context"] : new List<GeometryBase>();

                if (context.Count > 0)
                {
                    BakeGeometry(doc, context, outputRoot + "::Context", clusterId);
                }

                // Floor Mass Extraction using Wall Geometry
                if (walls.Count > 0 && floors.Count > 0)
                {
                    BoundingBox floorBox = BoundingBox.Empty;
                    foreach (var f in floors) floorBox.Union(f.GetBoundingBox(true));

                    if (floorBox.IsValid)
                    {
                        double zMax = floorBox.Max.Z;
                        double zMin = floorBox.Min.Z;
                        double thickness = zMax - zMin;

                        if (thickness > 0.01)
                        {
                            BoundingBox wallBox = BoundingBox.Empty;
                            foreach (var w in walls) wallBox.Union(w.GetBoundingBox(true));

                            Curve footprint = ComputeRaycastFootprint(walls, wallBox);

                            if (footprint != null && footprint.IsClosed)
                            {
                                BoundingBox crvBox = footprint.GetBoundingBox(true);
                                footprint.Translate(new Vector3d(0, 0, zMax - crvBox.Min.Z));
                                
                                var extrusion = Extrusion.Create(footprint, -thickness, true);
                                if (extrusion != null)
                                {
                                    var floorBrep = extrusion.ToBrep();
                                    if (floorBrep != null)
                                    {
                                        BakeGeometry(doc, new List<GeometryBase> { floorBrep }, outputRoot + "::Floors", clusterId);
                                    }
                                }
                            }
                        }
                    }
                }
                else if (floors.Count > 0)
                {
                    BakeGeometry(doc, floors, outputRoot + "::Floors", clusterId);
                }

                if (walls.Count > 0) BakeGeometry(doc, walls, outputRoot + "::Walls", clusterId);
                if (apertures.Count > 0) BakeGeometry(doc, apertures, outputRoot + "::Apertures", clusterId);
                if (shading.Count > 0) BakeGeometry(doc, shading, outputRoot + "::Shading", clusterId);
            }

            doc.Views.Redraw();
            RhinoApp.WriteLine("Phase 3 complete. Reconstructed geometry baked.");
            return Result.Success;
        }

        private Curve ComputeRaycastFootprint(IEnumerable<GeometryBase> geometries, BoundingBox bbox)
        {
            double resolution = 0.5;
            int xSteps = (int)Math.Ceiling((bbox.Max.X - bbox.Min.X) / resolution) + 2;
            int ySteps = (int)Math.Ceiling((bbox.Max.Y - bbox.Min.Y) / resolution) + 2;
            
            bool[,] grid = new bool[xSteps, ySteps];
            double startZ = bbox.Max.Z + 1.0;

            var searchMeshes = new List<Mesh>();
            foreach (var geo in geometries)
            {
                if (geo is Mesh m) searchMeshes.Add(m);
                else if (geo is Brep b) searchMeshes.AddRange(Mesh.CreateFromBrep(b, MeshingParameters.FastRenderMesh) ?? new Mesh[0]);
                else if (geo is Extrusion e) searchMeshes.Add(e.GetMesh(MeshType.Any));
            }
            if (searchMeshes.Count == 0) return null;

            var raycastMesh = new Mesh();
            foreach (var m in searchMeshes) raycastMesh.Append(m);

            for (int i = 0; i < xSteps; i++)
            {
                for (int j = 0; j < ySteps; j++)
                {
                    double x = bbox.Min.X + (i * resolution);
                    double y = bbox.Min.Y + (j * resolution);
                    
                    Ray3d ray = new Ray3d(new Point3d(x, y, startZ), Vector3d.ZAxis * -1);
                    double hit = Rhino.Geometry.Intersect.Intersection.MeshRay(raycastMesh, ray);
                    
                    if (hit >= 0.0) grid[i, j] = true;
                }
            }

            return TraceGridBoundary(grid, bbox.Min.X, bbox.Min.Y, resolution, bbox.Min.Z);
        }

        private Curve TraceGridBoundary(bool[,] grid, double startX, double startY, double resolution, double baseZ)
        {
            int xSteps = grid.GetLength(0);
            int ySteps = grid.GetLength(1);
            var cellRects = new List<Curve>();

            for (int i = 0; i < xSteps; i++)
            {
                for (int j = 0; j < ySteps; j++)
                {
                    if (grid[i, j])
                    {
                        double x = startX + (i * resolution);
                        double y = startY + (j * resolution);
                        
                        var pts = new Point3d[]
                        {
                            new Point3d(x - resolution/2, y - resolution/2, baseZ),
                            new Point3d(x + resolution/2, y - resolution/2, baseZ),
                            new Point3d(x + resolution/2, y + resolution/2, baseZ),
                            new Point3d(x - resolution/2, y + resolution/2, baseZ),
                            new Point3d(x - resolution/2, y - resolution/2, baseZ)
                        };
                        cellRects.Add(new PolylineCurve(pts));
                    }
                }
            }

            if (cellRects.Count == 0) return null;
            if (cellRects.Count == 1) return cellRects[0];

            var unioned = Curve.CreateBooleanUnion(cellRects, 0.001);
            if (unioned != null && unioned.Length > 0)
            {
                double maxArea = -1;
                Curve bestOutline = null;
                foreach (var crv in unioned)
                {
                    var amp = AreaMassProperties.Compute(crv);
                    if (amp != null && amp.Area > maxArea)
                    {
                        maxArea = amp.Area;
                        bestOutline = crv;
                    }
                }
                return bestOutline;
            }

            return null;
        }

        private void BakeGeometry(RhinoDoc doc, IEnumerable<GeometryBase> geometries, string layerPath, Guid sourceId)
        {
            int layerIndex = EnsureLayer(doc, layerPath);

            foreach (var geo in geometries)
            {
                if (geo != null)
                {
                    var attr = new ObjectAttributes();
                    attr.LayerIndex = layerIndex;
                    if (sourceId != Guid.Empty) attr.UserDictionary.Set("SourceID", sourceId.ToString());
                    doc.Objects.Add(geo, attr);
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
