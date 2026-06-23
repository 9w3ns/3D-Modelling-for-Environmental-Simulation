using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

// ==============================================================================
// PHASE 3: RECONSTRUCTION (STANDALONE SCRIPT)
// Run this directly in Rhino 8 ScriptEditor
// ==============================================================================

var doc = RhinoDoc.ActiveDoc;
RhinoApp.WriteLine("Starting Phase 3 (Script Mode): Geometry Reconstruction...");

string inputRoot = "Analysis::Phase2";
string outputRoot = "Analysis::Phase3::Reconstructed";

CleanAnalysisLayers(doc, outputRoot + "::Floors");
CleanAnalysisLayers(doc, outputRoot + "::Walls");
CleanAnalysisLayers(doc, outputRoot + "::Roofs");

// 1. Gather Phase 2 Geometries
var walls = GetGeometriesFromLayer(doc, inputRoot + "::Walls");
var floors = GetGeometriesFromLayer(doc, inputRoot + "::Floors");
var roofs = GetGeometriesFromLayer(doc, inputRoot + "::Roofs");

// ==============================================================================
// WALL RECONSTRUCTION (Solid Mass Contour Slicing)
// ==============================================================================
RhinoApp.WriteLine($"Reconstructing {walls.Count} Walls using Solid Mass Contour Slicing...");
var finalWalls = new List<Brep>();

if (walls.Count > 0)
{
    var searchMeshes = new List<Mesh>();
    foreach (var geo in walls)
    {
        if (geo is Mesh m) searchMeshes.Add(m);
        else if (geo is Brep b) searchMeshes.AddRange(Mesh.CreateFromBrep(b, MeshingParameters.FastRenderMesh) ?? new Mesh[0]);
        else if (geo is Extrusion e) searchMeshes.AddRange(Mesh.CreateFromBrep(e.ToBrep(), MeshingParameters.FastRenderMesh) ?? new Mesh[0]);
    }

    var wallMesh = new Mesh();
    foreach (var m in searchMeshes) wallMesh.Append(m);

    if (wallMesh.IsValid)
    {
        var bbox = wallMesh.GetBoundingBox(true);
        double zMin = bbox.Min.Z;
        double zMax = bbox.Max.Z;
        double sliceInterval = 2.0;
        
        double zCurr = zMin;
        double similarityThreshold = 0.60;
        double resolution = 0.50;
        
        double globalXMin = bbox.Min.X - resolution * 3;
        double globalXMax = bbox.Max.X + resolution * 3;
        double globalYMin = bbox.Min.Y - resolution * 3;
        double globalYMax = bbox.Max.Y + resolution * 3;
        
        int xStepsTotal = (int)Math.Ceiling((globalXMax - globalXMin) / resolution) + 1;
        int yStepsTotal = (int)Math.Ceiling((globalYMax - globalYMin) / resolution) + 1;
        
        var blocks = new List<Tuple<int[,], double, double>>();

        while (zCurr < zMax)
        {
            double zTop = Math.Min(zCurr + sliceInterval, zMax);
            
            // Slice the mesh twice per meter to ensure we catch everything
            var intersectCurves = new List<PolylineCurve>();
            double[] sliceHeights = new double[] { zCurr + 0.1, zTop - 0.1 };
            
            foreach (double hz in sliceHeights)
            {
                if (hz >= zMax) continue;
                var plane = new Plane(new Point3d(0, 0, hz), Vector3d.ZAxis);
                var crvArr = Rhino.Geometry.Intersect.Intersection.MeshPlane(wallMesh, plane);
                if (crvArr != null)
                {
                    foreach (var c in crvArr) intersectCurves.Add(c.ToPolylineCurve());
                }
            }
            
            if (intersectCurves.Count > 0)
            {
                var grid = GenerateErodedGrid(intersectCurves, globalXMin, globalYMin, xStepsTotal, yStepsTotal, resolution);
                
                if (blocks.Count > 0 && GridsAreSimilar(blocks[blocks.Count - 1].Item1, grid, xStepsTotal, yStepsTotal, similarityThreshold))
                {
                    var last = blocks[blocks.Count - 1];
                    UnionGrids(last.Item1, grid, xStepsTotal, yStepsTotal);
                    blocks[blocks.Count - 1] = new Tuple<int[,], double, double>(last.Item1, last.Item2, zTop);
                }
                else
                {
                    blocks.Add(new Tuple<int[,], double, double>(grid, zCurr, zTop));
                }
            }
            zCurr += sliceInterval;
        }

        var finalBlocks = new List<Brep>();
        
        foreach (var b in blocks)
        {
            var footprints = GetSolidWallFootprintFromGrid(b.Item1, globalXMin, globalYMin, xStepsTotal, yStepsTotal, resolution, b.Item2, doc.ModelAbsoluteTolerance);
            foreach (var fp in footprints)
            {
                var extrusion = Extrusion.Create(fp, b.Item3 - b.Item2, true);
                if (extrusion != null)
                {
                    var b3d = extrusion.ToBrep();
                    if (b3d != null) finalBlocks.Add(b3d);
                }
            }
        }

        finalWalls = IterativeBooleanUnion(finalBlocks);
    }
}

BakeGeometry(doc, finalWalls, outputRoot + "::Walls", System.Drawing.Color.Orange);

// ==============================================================================
// FLOOR & ROOF RECONSTRUCTION (Clustering + Silhouette Union)
// ==============================================================================
RhinoApp.WriteLine($"Reconstructing {floors.Count} Floors and {roofs.Count} Roofs using Silhouette-Union...");

var horizontalGeos = new List<GeometryBase>();
horizontalGeos.AddRange(floors);
horizontalGeos.AddRange(roofs);

// Group by Z elevation (Agglomerative Clustering, 1.50m gap tolerance)
var sortedGeos = horizontalGeos.OrderBy(g => g.GetBoundingBox(true).Max.Z).ToList();
var elevationClusters = new List<List<GeometryBase>>();

if (sortedGeos.Count > 0)
{
    var currentCluster = new List<GeometryBase> { sortedGeos[0] };
    elevationClusters.Add(currentCluster);

    for (int i = 1; i < sortedGeos.Count; i++)
    {
        var geo = sortedGeos[i];
        double currentZ = geo.GetBoundingBox(true).Max.Z;
        
        // Gap to the previous highest element in the current cluster
        double clusterMaxZ = currentCluster[currentCluster.Count - 1].GetBoundingBox(true).Max.Z;
        
        if (currentZ - clusterMaxZ <= 0.50)
        {
            currentCluster.Add(geo);
        }
        else
        {
            currentCluster = new List<GeometryBase> { geo };
            elevationClusters.Add(currentCluster);
        }
    }
}

var finalHorizontals = new List<Brep>();

foreach (var cluster in elevationClusters)
{
    double baseZ = cluster.Min(g => g.GetBoundingBox(true).Min.Z);
    double maxThickness = cluster.Max(g => {
        var box = g.GetBoundingBox(true);
        return box.Max.Z - box.Min.Z;
    });
    
    if (maxThickness < 0.10) maxThickness = 0.30; // Fallback for perfectly planar meshes
    
    BoundingBox levelBox = BoundingBox.Empty;
    foreach (var g in cluster) levelBox.Union(g.GetBoundingBox(true));
    
    var allFootprints = GetRaycastFootprint(cluster, levelBox, baseZ, doc.ModelAbsoluteTolerance);
    
    if (allFootprints != null && allFootprints.Count > 0)
    {
        foreach (var fp in allFootprints)
        {
            var extrusion = Extrusion.Create(fp, maxThickness, true); // +Z extrusion
            if (extrusion != null)
            {
                var b3d = extrusion.ToBrep();
                if (b3d != null)
                {
                    b3d.MergeCoplanarFaces(RhinoMath.DefaultAngleTolerance);
                    finalHorizontals.Add(b3d);
                }
            }
        }
    }
    else
    {
        // Fallback: Use BoundingBox if totally failed (e.g. empty meshes)
        var fallbackBrep = Brep.CreateFromBox(levelBox);
        if (fallbackBrep != null) finalHorizontals.Add(fallbackBrep);
    }
}

BakeGeometry(doc, finalHorizontals, outputRoot + "::Floors", System.Drawing.Color.DarkGray);

doc.Views.Redraw();
RhinoApp.WriteLine("Phase 3 complete! Geometry has been reconstructed into watertight solids.");

// ==============================================================================
// HELPER METHODS
// ==============================================================================

List<Brep> IterativeBooleanUnion(List<Brep> breps)
{
    if (breps.Count == 0) return new List<Brep>();
    if (breps.Count == 1) return breps;

    var currentUnion = new List<Brep> { breps[0] };
    
    for (int i = 1; i < breps.Count; i++)
    {
        var result = Brep.CreateBooleanUnion(new List<Brep> { currentUnion[0], breps[i] }, 0.01);
        if (result != null && result.Length == 1)
        {
            // Successfully merged into a single solid
            result[0].MergeCoplanarFaces(RhinoMath.DefaultAngleTolerance);
            currentUnion[0] = result[0];
        }
        else
        {
            // If union fails or they are disjoint (result.Length > 1), keep it separate
            currentUnion.Add(breps[i]);
        }
    }
    return currentUnion;
}

List<GeometryBase> GetGeometriesFromLayer(RhinoDoc d, string layerPath)
{
    var list = new List<GeometryBase>();
    int idx = d.Layers.FindByFullPath(layerPath, RhinoMath.UnsetIntIndex);
    if (idx < 0) return list;
    
    var objs = d.Objects.FindByLayer(d.Layers[idx]);
    if (objs != null)
    {
        foreach (var obj in objs) list.Add(obj.Geometry.Duplicate());
    }
    return list;
}



void BakeGeometry(RhinoDoc d, List<Brep> breps, string layerPath, System.Drawing.Color color)
{
    int layerIndex = EnsureLayer(d, layerPath, color);
    var attr = new ObjectAttributes();
    attr.LayerIndex = layerIndex;
    foreach (var b in breps) d.Objects.Add(b, attr);
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

int EnsureLayer(RhinoDoc d, string path, System.Drawing.Color color)
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
            var newLayer = new Layer { Name = parts[i], Color = color };
            if (parentIndex != -1) newLayer.ParentLayerId = d.Layers[parentIndex].Id;
            parentIndex = d.Layers.Add(newLayer);
        }
    }
    return parentIndex;
}

List<Curve> GetRaycastFootprint(IEnumerable<GeometryBase> geometries, BoundingBox bbox, double zLevel, double tolerance)
{
    double resolution = 0.50; // Grid resolution
    int xSteps = (int)Math.Ceiling((bbox.Max.X - bbox.Min.X) / resolution) + 2;
    int ySteps = (int)Math.Ceiling((bbox.Max.Y - bbox.Min.Y) / resolution) + 2;
    bool[,] grid = new bool[xSteps, ySteps];

    double startZ = bbox.Max.Z + 1.0;
    var searchMeshes = new List<Mesh>();
    foreach (var geo in geometries)
    {
        if (geo is Mesh m) searchMeshes.Add(m);
        else if (geo is Brep b) searchMeshes.AddRange(Mesh.CreateFromBrep(b, MeshingParameters.FastRenderMesh) ?? new Mesh[0]);
        else if (geo is Extrusion e) searchMeshes.AddRange(Mesh.CreateFromBrep(e.ToBrep(), MeshingParameters.FastRenderMesh) ?? new Mesh[0]);
    }
    if (searchMeshes.Count == 0) return new List<Curve>();

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

    var cellRects = new List<Curve>();
    for (int i = 0; i < xSteps; i++)
    {
        for (int j = 0; j < ySteps; j++)
        {
            if (grid[i, j])
            {
                double x = bbox.Min.X + (i * resolution);
                double y = bbox.Min.Y + (j * resolution);
                var pts = new Point3d[]
                {
                    new Point3d(x - resolution/2.0, y - resolution/2.0, zLevel),
                    new Point3d(x + resolution/2.0, y - resolution/2.0, zLevel),
                    new Point3d(x + resolution/2.0, y + resolution/2.0, zLevel),
                    new Point3d(x - resolution/2.0, y + resolution/2.0, zLevel),
                    new Point3d(x - resolution/2.0, y - resolution/2.0, zLevel)
                };
                cellRects.Add(new PolylineCurve(pts));
            }
        }
    }

    if (cellRects.Count == 0) return new List<Curve>();

    var unioned = Curve.CreateBooleanUnion(cellRects, tolerance);
    if (unioned == null || unioned.Length == 0) return new List<Curve>();

    // RDP Smoothing (Ramer-Douglas-Peucker)
    var smoothedCurves = new List<Curve>();
    double rdpTolerance = resolution * 1.5; // Enough to flatten the zig-zags completely
    foreach (var uCrv in unioned)
    {
        if (uCrv.TryGetPolyline(out Polyline pl))
        {
            pl.ReduceSegments(rdpTolerance);
            smoothedCurves.Add(new PolylineCurve(pl));
        }
        else
        {
            smoothedCurves.Add(uCrv);
        }
    }

    return smoothedCurves;
}

int[,] GenerateErodedGrid(List<PolylineCurve> polylines, double xMin, double yMin, int xSteps, int ySteps, double resolution)
{
    int[,] grid = new int[xSteps, ySteps];

    foreach (var pl in polylines)
    {
        if (pl.TryGetPolyline(out Polyline poly))
        {
            int segmentCount = poly.SegmentCount;
            for (int k = 0; k < segmentCount; k++)
            {
                var line = poly.SegmentAt(k);
                int samples = (int)(line.Length / (resolution / 4.0)) + 1;
                for (int s = 0; s <= samples; s++)
                {
                    double t = samples > 0 ? (double)s / samples : 0.0;
                    var pt = line.PointAt(t);
                    int gx = (int)((pt.X - xMin) / resolution);
                    int gy = (int)((pt.Y - yMin) / resolution);
                    if (gx >= 0 && gx < xSteps && gy >= 0 && gy < ySteps)
                    {
                        grid[gx, gy] = 1;
                    }
                }
            }
        }
    }

    int[,] gridDilated = new int[xSteps, ySteps];
    for (int i = 0; i < xSteps; i++)
        for (int j = 0; j < ySteps; j++)
            gridDilated[i, j] = grid[i, j];

    int[] dx = { 0, 0, 1, -1, 1, -1, 1, -1 };
    int[] dy = { 1, -1, 0, 0, 1, -1, -1, 1 };

    for (int i = 1; i < xSteps - 1; i++)
    {
        for (int j = 1; j < ySteps - 1; j++)
        {
            if (grid[i, j] == 1)
            {
                for (int d = 0; d < 8; d++)
                {
                    gridDilated[i + dx[d], j + dy[d]] = 1;
                }
            }
        }
    }

    Queue<Tuple<int, int>> q = new Queue<Tuple<int, int>>();
    q.Enqueue(new Tuple<int, int>(0, 0));
    gridDilated[0, 0] = 2;

    while (q.Count > 0)
    {
        var curr = q.Dequeue();
        int cx = curr.Item1;
        int cy = curr.Item2;

        for (int d = 0; d < 8; d++)
        {
            int nx = cx + dx[d];
            int ny = cy + dy[d];
            if (nx >= 0 && nx < xSteps && ny >= 0 && ny < ySteps)
            {
                if (gridDilated[nx, ny] == 0)
                {
                    gridDilated[nx, ny] = 2;
                    q.Enqueue(new Tuple<int, int>(nx, ny));
                }
            }
        }
    }

    int[,] gridEroded = new int[xSteps, ySteps];
    for (int i = 0; i < xSteps; i++)
        for (int j = 0; j < ySteps; j++)
            gridEroded[i, j] = gridDilated[i, j];

    for (int i = 1; i < xSteps - 1; i++)
    {
        for (int j = 1; j < ySteps - 1; j++)
        {
            if (gridDilated[i, j] != 2)
            {
                for (int d = 0; d < 8; d++)
                {
                    if (gridDilated[i + dx[d], j + dy[d]] == 2)
                    {
                        gridEroded[i, j] = 2;
                        break;
                    }
                }
            }
        }
    }

    return gridEroded;
}

bool GridsAreSimilar(int[,] g1, int[,] g2, int xs, int ys, double thresh)
{
    int match = 0, total1 = 0, total2 = 0;
    for (int i = 0; i < xs; i++)
    {
        for (int j = 0; j < ys; j++)
        {
            bool s1 = (g1[i, j] != 2);
            bool s2 = (g2[i, j] != 2);
            if (s1) total1++;
            if (s2) total2++;
            if (s1 && s2) match++;
        }
    }
    int m = Math.Max(total1, total2);
    if (m == 0) return true;
    return ((double)match / m) >= thresh;
}

void UnionGrids(int[,] dest, int[,] src, int xs, int ys)
{
    for (int i = 0; i < xs; i++)
        for (int j = 0; j < ys; j++)
            if (src[i, j] != 2) dest[i, j] = 1;
}

List<Curve> GetSolidWallFootprintFromGrid(int[,] grid, double xMin, double yMin, int xSteps, int ySteps, double resolution, double zLevel, double tolerance)
{
    var cellRects = new List<Curve>();
    for (int i = 0; i < xSteps; i++)
    {
        for (int j = 0; j < ySteps; j++)
        {
            if (grid[i, j] != 2)
            {
                double x = xMin + (i * resolution);
                double y = yMin + (j * resolution);
                var pts = new Point3d[]
                {
                    new Point3d(x, y, zLevel),
                    new Point3d(x + resolution, y, zLevel),
                    new Point3d(x + resolution, y + resolution, zLevel),
                    new Point3d(x, y + resolution, zLevel),
                    new Point3d(x, y, zLevel)
                };
                cellRects.Add(new PolylineCurve(pts));
            }
        }
    }

    if (cellRects.Count == 0) return new List<Curve>();

    var unioned = Curve.CreateBooleanUnion(cellRects, tolerance);
    if (unioned == null || unioned.Length == 0) return new List<Curve>();

    // RDP Smoothing
    var smoothedCurves = new List<Curve>();
    double rdpTolerance = resolution * 1;
    foreach (var bCrv in unioned)
    {
        if (bCrv.TryGetPolyline(out Polyline plRdp))
        {
            plRdp.ReduceSegments(rdpTolerance);
            smoothedCurves.Add(new PolylineCurve(plRdp));
        }
        else
        {
            smoothedCurves.Add(bCrv);
        }
    }

    return smoothedCurves;
}
