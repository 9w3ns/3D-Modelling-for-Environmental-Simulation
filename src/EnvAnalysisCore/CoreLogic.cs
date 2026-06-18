using System;
using Rhino.Geometry;
using System.Collections.Generic;

namespace EnvAnalysisCore
{
    /// <summary>
    /// Supported simulation engines. Each has unique geometric requirements 
    /// as defined in the '3D Modelling for Environmental Simulation.xlsx' Goal Matrix.
    /// </summary>
    public enum SimulationTarget
    {
        Ladybug,
        HoneybeeRadiance,
        HoneybeeEnergy,
        Eddy3D,
        Vento
    }

    public enum GeometryRole
    {
        Unknown,
        Wall,
        Floor,
        Roof,
        Aperture,
        Context
    }

    /// <summary>
    /// Pure Logic Core: Simplification Algorithms
    /// Stateless methods for planarizing geometry for Ladybug/Honeybee.
    /// </summary>
    public static class SimplificationLogic
    {
        /// <summary>
        /// Orchestrates simplification based on the target simulation engine.
        /// Accepts a cluster of geometries (e.g., all parts of a building block) to process together.
        /// </summary>
        public static GeometryBase ProcessCluster(List<GeometryBase> cluster, SimulationTarget target)
        {
            if (cluster == null || cluster.Count == 0) return null;

            switch (target)
            {
                case SimulationTarget.Ladybug:
                    return PlanarizeForLadybug(cluster);
                case SimulationTarget.HoneybeeRadiance:
                    return PlanarizeForRadiance(cluster); // Pass the whole list
                case SimulationTarget.HoneybeeEnergy:
                    return PrepareForEnergy(cluster); // Pass the whole list
                default:
                    return cluster[0];
            }
        }

        private static Brep PlanarizeForLadybug(List<GeometryBase> cluster)
        {
            if (cluster == null || cluster.Count == 0) return null;

            // Get combined bounding box for the entire cluster
            BoundingBox bbox = BoundingBox.Empty;
            foreach (var geo in cluster)
            {
                bbox.Union(geo.GetBoundingBox(true));
            }

            if (!bbox.IsValid) return null;

            double height = bbox.Max.Z - bbox.Min.Z;
            if (height <= 0.01) return null;

            // Use the robust Raycast Voxelization method on the entire cluster
            Curve footprint = GetRaycastFootprint(cluster, bbox);

            if (footprint != null && footprint.IsClosed)
            {
                var extrusion = Extrusion.Create(footprint, height, true);
                if (extrusion != null)
                {
                    return extrusion.ToBrep();
                }
            }

            // Absolute fallback
            return Brep.CreateFromBox(bbox);
        }

        /// <summary>
        /// Robust Hybrid Logic: Raycast Voxelization.
        /// Casts a grid of rays downward to determine the exact 2D footprint,
        /// bypassing issues with fragmented or non-manifold architectural models.
        /// </summary>
        private static Curve GetRaycastFootprint(IEnumerable<GeometryBase> geometries, BoundingBox bbox)
        {
            double resolution = 0.5; // 0.5m grid resolution for the footprint

            int xSteps = (int)Math.Ceiling((bbox.Max.X - bbox.Min.X) / resolution) + 2;
            int ySteps = (int)Math.Ceiling((bbox.Max.Y - bbox.Min.Y) / resolution) + 2;
            
            bool[,] grid = new bool[xSteps, ySteps];

            // Setup raycast origins slightly above the max Z
            double startZ = bbox.Max.Z + 1.0;

            // We must convert everything to Meshes for fast ray intersection
            var searchMeshes = new List<Mesh>();
            foreach (var geo in geometries)
            {
                if (geo is Mesh m) searchMeshes.Add(m);
                else if (geo is Brep b) searchMeshes.AddRange(Mesh.CreateFromBrep(b, MeshingParameters.FastRenderMesh) ?? new Mesh[0]);
            }
            if (searchMeshes.Count == 0) return null;

            var raycastMesh = new Mesh();
            foreach (var m in searchMeshes) raycastMesh.Append(m);

            // Cast Rays
            for (int i = 0; i < xSteps; i++)
            {
                for (int j = 0; j < ySteps; j++)
                {
                    double x = bbox.Min.X + (i * resolution);
                    double y = bbox.Min.Y + (j * resolution);
                    
                    Ray3d ray = new Ray3d(new Point3d(x, y, startZ), Vector3d.ZAxis * -1);
                    double hit = Rhino.Geometry.Intersect.Intersection.MeshRay(raycastMesh, ray);
                    
                    if (hit >= 0.0)
                    {
                        grid[i, j] = true; // Solid
                    }
                }
            }

            // Convert binary grid to boundary curve (Marching Squares approach simplified)
            return TraceGridBoundary(grid, bbox.Min.X, bbox.Min.Y, resolution, bbox.Min.Z);
        }

        private static Curve TraceGridBoundary(bool[,] grid, double startX, double startY, double resolution, double baseZ)
        {
            // Simplified grid boundary extraction.
            // We create a square curve for every 'true' cell and union them.
            // Because these are perfectly aligned coplanar rectangles, 
            // Curve.CreateBooleanUnion is highly stable and fast, unlike unioning random architectural meshes.
            
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
                // Return the largest outline
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

        private static Brep PlanarizeForRadiance(List<GeometryBase> cluster)
        {
            // Radiance Goal: Planar Brep | Low (Simplified, flat)
            return PlanarizeForLadybug(cluster);
        }

        private static Brep PrepareForEnergy(List<GeometryBase> cluster)
        {
            // Energy Goal: Closed Brep (Watertight) | Low (Simplified)
            return PlanarizeForLadybug(cluster);
        }

        private static Brep PlanarizeBrep(Brep brep)
        {
            // Simple Planarization: ensure each face is flat by fitting a plane to its vertices
            foreach (var face in brep.Faces)
            {
                if (!face.IsPlanar())
                {
                    // Logic to force planarity (e.g. by rebuilding the face from its 2D boundary)
                }
            }
            return brep;
        }

        /// <summary>
        /// Analyzes a surface to determine its role (Wall, Floor, Roof) based on its normal vector.
        /// </summary>
        public static GeometryRole ClassifySurface(Surface surface)
        {
            if (surface == null) return GeometryRole.Unknown;

            // Get the normal at the center of the surface
            Vector3d normal = surface.NormalAt(surface.Domain(0).Mid, surface.Domain(1).Mid);
            double angleToUp = Vector3d.VectorAngle(normal, Vector3d.ZAxis);

            // Heuristics:
            // 0 - 20 degrees: Roof (Facing Up)
            // 160 - 180 degrees: Floor (Facing Down)
            // 70 - 110 degrees: Wall (Vertical)
            if (angleToUp <= Math.PI / 9) return GeometryRole.Roof;
            if (angleToUp >= 8 * Math.PI / 9) return GeometryRole.Floor;
            if (angleToUp >= 7 * Math.PI / 18 && angleToUp <= 11 * Math.PI / 18) return GeometryRole.Wall;

            return GeometryRole.Wall; // Default to wall for slightly tilted surfaces
        }

        /// <summary>
        /// Implements the "Co-planar & Contained" rule to identify apertures within a parent wall.
        /// </summary>
        public static bool IsApertureInWall(BrepFace potentialAperture, BrepFace parentWall)
        {
            // 1. Check if co-planar
            if (!AreFacesCoplanar(potentialAperture, parentWall)) return false;

            // 2. Check if contained within boundary
            return IsContained(potentialAperture, parentWall);
        }

        private static bool AreFacesCoplanar(BrepFace f1, BrepFace f2)
        {
            if (!f1.TryGetPlane(out Plane p1) || !f2.TryGetPlane(out Plane p2)) return false;
            
            double angle = Vector3d.VectorAngle(p1.Normal, p2.Normal);
            if (angle > 0.01 && Math.Abs(angle - Math.PI) > 0.01) return false;

            double dist = Math.Abs(p1.DistanceTo(p2.Origin));
            return dist < 0.01;
        }

        private static bool IsContained(BrepFace child, BrepFace parent)
        {
            var childBox = child.GetBoundingBox(true);
            var parentBox = parent.GetBoundingBox(true);
            return parentBox.Contains(childBox);
        }

        /// <summary>
        /// Track 2: Aggressive Abstraction.
        /// Returns a single 6-sided Brep box encapsulating all input geometry.
        /// </summary>
        public static Brep CreateBoundingVolume(IEnumerable<GeometryBase> inputs)
        {
            BoundingBox bbox = BoundingBox.Empty;
            foreach (var input in inputs)
            {
                bbox.Union(input.GetBoundingBox(true));
            }

            if (!bbox.IsValid) return null;
            return bbox.ToBrep();
        }
    }

    /// <summary>
    /// Pure Logic Core: CFD Meshing Algorithms
    /// Stateless methods for generating watertight meshes for wind analysis.
    /// </summary>
    public static class MeshingLogic
    {
        public static Mesh GenerateWatertightMesh(IEnumerable<GeometryBase> inputs, double offsetDistance = 0.5)
        {
            var combinedMesh = new Mesh();
            var meshesToProcess = new List<Mesh>();

            foreach (var input in inputs)
            {
                if (input is Mesh m) meshesToProcess.Add(m);
                else if (input is Brep b) meshesToProcess.AddRange(Mesh.CreateFromBrep(b, MeshingParameters.Default));
                else if (input is Extrusion e) meshesToProcess.Add(e.GetMesh(MeshType.Any));
            }

            if (meshesToProcess.Count == 0) return combinedMesh;

            // Morphological Closing: Dilation -> Union -> Erosion
            // 1. Dilation (Offset Mesh Outward)
            var dilatedMeshes = new List<Mesh>();
            foreach (var m in meshesToProcess)
            {
                var offset = m.Offset(offsetDistance, true);
                if (offset != null) dilatedMeshes.Add(offset);
            }

            // 2. Union (Boolean Union of dilated meshes)
            var unitedMesh = new Mesh();
            if (dilatedMeshes.Count > 0)
            {
                unitedMesh = Mesh.CreateBooleanUnion(dilatedMeshes)?[0] ?? new Mesh();
            }

            // 3. Erosion (Offset Result Inward)
            var finalMesh = unitedMesh.Offset(-offsetDistance, true) ?? unitedMesh;

            return finalMesh;
        }
    }

    /// <summary>
    /// Pure Logic Core: Adjacency Algorithms
    /// Stateless methods for intersecting zones and matching thermal boundaries.
    /// </summary>
    public static class AdjacencyLogic
    {
        public static void SolveAdjacency(IEnumerable<Brep> zones)
        {
            // Logic to intersect adjacent Breps and match faces
        }
    }

    /// <summary>
    /// Stateless data structure for internal geometry representation.
    /// Does not depend on RhinoDoc or RhinoObject.
    /// </summary>
    public class AnalysisGeometry
    {
        public GeometryBase Geometry { get; set; }
        public string Type { get; set; } // Wall, Roof, Aperture, Context
        public string ZoneName { get; set; }
        
        /// <summary>
        /// Traceability: List of original RhinoObject IDs that formed this geometry.
        /// Used for the "No Geometry Left Behind" Coverage Audit.
        /// </summary>
        public List<Guid> SourceIds { get; set; } = new List<Guid>();
    }
}
