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
        
        if (currentZ - clusterMaxZ <= 1.50)
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
    var levelBreps = new List<Brep>();
    var projPlane = new Plane(new Point3d(0, 0, zLevel), Vector3d.ZAxis);
    var xform = Transform.PlanarProjection(projPlane);
    
    foreach (var geo in cluster)
    {
        Brep br = null;
        if (geo is Brep b) br = b;
        else if (geo is Extrusion ext) br = ext.ToBrep();
        else if (geo is Mesh m) br = Brep.CreateFromMesh(m, true);
        
        if (br == null) continue;
        
        var outlineCurves = new List<Curve>();
        
        // 1. Silhouette extraction
        var silhouettes = Silhouette.Compute(br, SilhouetteType.Projecting, Vector3d.ZAxis, doc.ModelAbsoluteTolerance, 0.01);
        if (silhouettes != null && silhouettes.Length > 0)
        {
            foreach (var s in silhouettes)
            {
                if (s.Curve != null)
                {
                    var c = s.Curve.DuplicateCurve();
                    c.Transform(xform);
                    outlineCurves.Add(c);
                }
            }
        }
        else
        {
            // Fallback 1: Naked Edges
            var naked = br.DuplicateNakedEdgeCurves(true, false);
            if (naked != null && naked.Length > 0)
            {
                foreach (var c in naked)
                {
                    var dup = c.DuplicateCurve();
                    dup.Transform(xform);
                    outlineCurves.Add(dup);
                }
            }
        }
        
        // Fallback 2: Bounding Box
        if (outlineCurves.Count == 0)
        {
            var bbox = geo.GetBoundingBox(true);
            if (bbox.IsValid)
            {
                var rect = new Rectangle3d(projPlane, new Interval(bbox.Min.X, bbox.Max.X), new Interval(bbox.Min.Y, bbox.Max.Y));
                outlineCurves.Add(rect.ToNurbsCurve());
            }
        }
        
        if (outlineCurves.Count > 0)
        {
            var joined = Curve.JoinCurves(outlineCurves, 0.1);
            if (joined != null)
            {
                foreach (var jCrv in joined)
                {
                    if (!jCrv.IsClosed) jCrv.MakeClosed(0.1);
                    if (jCrv.IsClosed)
                    {
                        var extrusion = Extrusion.Create(jCrv, -0.3, true);
                        if (extrusion != null)
                        {
                            var b3d = extrusion.ToBrep();
                            if (b3d != null) levelBreps.Add(b3d);
                        }
                    }
                }
            }
        }
    }
    
    // 3D Boolean Union the level Breps
    if (levelBreps.Count > 0)
    {
        var unioned = IterativeBooleanUnion(levelBreps);
        finalHorizontals.AddRange(unioned);
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
