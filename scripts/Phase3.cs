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
// FLOOR & ROOF RECONSTRUCTION (Grouping & Silhouette Union)
// ==============================================================================
RhinoApp.WriteLine($"Reconstructing {floors.Count} Floors and {roofs.Count} Roofs using Silhouette-Union...");

var horizontalGeos = new List<GeometryBase>();
horizontalGeos.AddRange(floors);
horizontalGeos.AddRange(roofs);

// Group by Z elevation (Agglomerative Clustering, 0.50m tolerance)
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
        
        // Find the absolute max Z of the current cluster so far
        double clusterMaxZ = currentCluster.Max(g => g.GetBoundingBox(true).Max.Z);
        
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
    double zLevel = cluster.Max(g => g.GetBoundingBox(true).Max.Z);
    var geosAtLevel = cluster;
    
    BoundingBox levelBox = BoundingBox.Empty;
    foreach (var g in geosAtLevel) levelBox.Union(g.GetBoundingBox(true));
    
    // Silhouette Extraction (Mathematical Projection)
    var silhouettes = GetExactFootprint(geosAtLevel, zLevel);
    if (silhouettes != null && silhouettes.Count > 0)
    {
        foreach (var sil in silhouettes)
        {
            // Extrude down by standard floor thickness (0.3m)
            var extrusion = Extrusion.Create(sil, -0.3, true);
            if (extrusion != null)
            {
                var b = extrusion.ToBrep();
                if (b != null) finalHorizontals.Add(b);
            }
        }
    }
    else
    {
        // Fallback: Use BoundingBox
        var fallbackBrep = Brep.CreateFromBox(levelBox);
        if (fallbackBrep != null) finalHorizontals.Add(fallbackBrep);
    }
}

// Iterative Boolean Union for Floors
var finalSlabs = IterativeBooleanUnion(finalHorizontals);
BakeGeometry(doc, finalSlabs, outputRoot + "::Floors", System.Drawing.Color.DarkGray);

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

List<Curve> GetExactFootprint(IEnumerable<GeometryBase> geometries, double zLevel)
{
    var projCurves = new List<Curve>();
    var zPlane = new Plane(new Point3d(0, 0, zLevel), Vector3d.ZAxis);
    var xform = Transform.PlanarProjection(zPlane);

    foreach (var geo in geometries)
    {
        Brep b = null;
        if (geo is Brep brep) b = brep;
        else if (geo is Extrusion ext) b = ext.ToBrep();
        
        if (b != null)
        {
            foreach (var face in b.Faces)
            {
                // Only take horizontal faces to form the footprint
                var n = face.NormalAt(face.Domain(0).Mid, face.Domain(1).Mid);
                if (Math.Abs(n.Z) > 0.8) // Mostly horizontal
                {
                    var loop = face.OuterLoop.To3dCurve();
                    if (loop != null)
                    {
                        loop.Transform(xform);
                        projCurves.Add(loop);
                    }
                }
            }
        }
    }

    if (projCurves.Count == 0) return null;

    // Union all projected curves into clean footprints
    var unioned = Curve.CreateBooleanUnion(projCurves, 0.01);
    if (unioned != null && unioned.Length > 0)
    {
        var validOutlines = new List<Curve>();
        foreach (var crv in unioned)
        {
            if (crv.IsClosed) validOutlines.Add(crv);
        }
        return validOutlines;
    }
    
    return null;
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
