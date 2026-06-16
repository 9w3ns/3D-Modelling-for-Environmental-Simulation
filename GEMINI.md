# Gemini CLI Adapter

Use the shared repository documentation as the source of truth:

1. `README.md` - Project overview and quick start.
2. `CORE_ARCHITECTURE.md` - Technical architecture and pipeline phases.
3. `MCP_INSTRUCTIONS.md` - Role definition and mandatory layer conventions.
4. `MODELLING_GUIDE_*.md` - Engine-specific geometric requirements (Ladybug, Honeybee, CFD).

At the start of every session, read `MCP_INSTRUCTIONS.md`.
Also read `src/EnvAnalysisCore/CoreLogic.cs` to understand the current implementation state of the transformation engine.

## System Integrity & Regression Guardrails

To prevent regressions in the geometry transformation pipeline:

1.  **Pipeline Isolation**:
    *   **Phase 1: Ingestion**: Logic for parsing layers and identifying `AnalysisObject` types.
    *   **Phase 2: Core Engine**: Deterministic algorithms (Planarize, SolveAdjacency, CFD Meshing).
    *   **Phase 3: Adapters**: Export logic for specific engines (HBJSON, STL).
2.  **Validation**:
    *   **Layer Convention**: Always verify that input geometry follows the `Analysis::...` schema defined in `MCP_INSTRUCTIONS.md`.
    *   **Geometric Integrity**: Ensure that transformations for Ladybug/Honeybee result in planar surfaces, and CFD transformations result in watertight manifold meshes.
3.  **BIM/Layer Identity**:
    *   The `Analysis::Zones::[ZoneName]::[Element]` naming convention is a standard requirement for Honeybee volume solving.

## Logic Change & Error Handling Strategies

### 1. Layer Ingestion & AnalysisObject Tagging
To ensure the pipeline knows how to treat different architectural elements:
- **Logic**: Use a recursive layer crawler to extract geometry and wrap them in a data structure that carries the `TargetEngine` and `AnalysisRole`.
- **Error Handling**: If an object is on an unknown layer, it should be ignored or reported as an error in an `Analysis::Verification` sub-layer.

### 2. Planarization (Ladybug/Honeybee)
To ensure complex design geometry is compatible with energy/daylight simulation engines:
- **Logic**: Convert curved or non-planar Breps into the nearest best-fit plane.
- **Verification**: After running `-EnvPrepPlanarize`, verify that every face in the resulting Brep returns `true` for `face.IsPlanar()`.

### 3. Volume Solving & Adjacency (Honeybee)
To create valid thermal zones with shared boundaries:
- **Logic**: Use `-EnvSolveAdjacency` to intersect overlapping surfaces between adjacent zones. This ensures that internal walls are perfectly matched, which is critical for accurate energy transfer.
- **Watertightness**: Each `Zone` must form a closed volume. Use `Brep.IsSolid` for verification.

### 4. CFD Mesh Generation (Vento/Eddy3D)
To create a single, continuous simulation environment for wind analysis:
- **Logic**: Perform a boolean union of all context buildings and the target building to create a watertight mesh.
- **Export**: Use `-EnvExportSTL` to save the resulting mesh. The STL must be manifold and have no naked edges.

---
*Rationale and historical decisions live in `CORE_ARCHITECTURE.md`.*
