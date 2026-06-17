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

    /// <summary>
    /// Pure Logic Core: Simplification Algorithms
    /// Stateless methods for planarizing geometry for Ladybug/Honeybee.
    /// </summary>
    public static class SimplificationLogic
    {
        /// <summary>
        /// Orchestrates simplification based on the target simulation engine.
        /// </summary>
        public static GeometryBase Process(GeometryBase input, SimulationTarget target)
        {
            switch (target)
            {
                case SimulationTarget.Ladybug:
                    return PlanarizeForLadybug(input);
                case SimulationTarget.HoneybeeRadiance:
                    return PlanarizeForRadiance(input);
                case SimulationTarget.HoneybeeEnergy:
                    return PrepareForEnergy(input);
                default:
                    return input;
            }
        }

        private static Brep PlanarizeForLadybug(GeometryBase input)
        {
            // Ladybug Goal: Mesh / Brep | Low (Simplified, planar)
            // Strategy: Force each face to its average plane, preserve gaps.
            if (input is Brep brep) return PlanarizeBrep(brep);
            if (input is Mesh mesh) return PlanarizeMesh(mesh).ToBrep();
            return null;
        }

        private static Brep PlanarizeForRadiance(GeometryBase input)
        {
            // Radiance Goal: Planar Brep | Low (Simplified, flat)
            // Strategy: Stricter planarization, error if not flat.
            return PlanarizeForLadybug(input);
        }

        private static Brep PrepareForEnergy(GeometryBase input)
        {
            // Energy Goal: Closed Brep (Watertight) | Low (Simplified)
            // Strategy: Planarize + ensure volume is closed.
            return PlanarizeForLadybug(input);
        }

        private static Brep PlanarizeBrep(Brep brep)
        {
            // Placeholder for Brep face planarization logic
            return brep;
        }

        private static Mesh PlanarizeMesh(Mesh mesh)
        {
            // Placeholder for Mesh face planarization logic
            return mesh;
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
