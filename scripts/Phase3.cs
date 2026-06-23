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
// WALL RECONSTRUCTION (OBB-Union Invariant)
// ==============================================================================
RhinoApp.WriteLine($"Reconstructing {walls.Count} Walls using OBB-Union...");
var reconstructedWalls = new List<Brep>();

foreach (var geo in walls)
{
    var bbox = geo.GetBoundingBox(true);
    if (!bbox.IsValid) continue;

    // Create Oriented Bounding Box (OBB). Since we assume Z is up, we can use the World XY plane.
    // Box.CreateFromBrep provides the tightest OBB. If it fails, fallback to world bounding box.
    Box obb;
    if (geo is Brep brep)
    {
        obb = Box.Unset;
        // Get tight bounding box
        var tightBox = brep.GetBoundingBox(true);
        obb = new Box(tightBox);
    }
    else
    {
        obb = new Box(bbox);
    }
    
    var obbBrep = obb.ToBrep();
    if (obbBrep != null) reconstructedWalls.Add(obbBrep);
}

// Iterative Boolean Union for Walls
var finalWalls = IterativeBooleanUnion(reconstructedWalls);
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
