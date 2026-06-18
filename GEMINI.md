# Gemini CLI Adapter

Use the shared repository documentation as the source of truth:

1. `README.md` - Project overview and quick start.
2. `CORE_ARCHITECTURE.md` - Technical architecture and pipeline phases.
3. `MCP_INSTRUCTIONS.md` - Role definition and mandatory layer conventions.
4. `MODELLING_GUIDE_*.md` - Engine-specific geometric requirements (Ladybug, Honeybee, CFD).
5. `RECONSTRUCTION_PIPELINE.md` - The detailed 5-phase logic for the 2.5D Honeybee geometry reconstruction.

## Project Scope Mandate
**Exclusion of Simulation Logic:** The scope of this project is strictly limited to the **3D modelling transformation and preparation** of simulation-ready geometry. Do NOT attempt to build or modify Grasshopper simulation logic (radiation, sun hours, thermal loads), as these are handled by existing standardized scripts. Focus exclusively on geometric optimization, layer compliance, and manifold verification.

**The Goal Matrix (Source of Truth):** 
The file `3D Modelling for Environmental Simulation.xlsx` is the **definitive Goal Matrix** for this project. Every transformation pathway (Ladybug, Honeybee, CFD, etc.) must be engineered to produce the specific **Geometry Type** and **Level of Detail (LOD)** defined in this matrix for the chosen simulation engine. "All roads lead to the Matrix" – the success of a transformation is measured by its compliance with these specifications.

At the start of every session, read `MCP_INSTRUCTIONS.md` and consult the `3D Modelling for Environmental Simulation.xlsx` matrix.
Also read `src/EnvAnalysisCore/CoreLogic.cs` to understand the current implementation of the stateless Pure Logic Core.

## System Integrity & Regression Guardrails

To prevent regressions in the geometry transformation pipeline:

1.  **Architectural Isolation**:
    *   **Layer 1: Pure Logic Core (`EnvAnalysisCore`)**: MUST remain stateless and only depend on `Rhino.Geometry`. No document manipulation is allowed here.
    *   **Layer 2: Document Interface**: Responsible for layer parsing and document I/O.
    *   **Layer 3: Commands/Agents**: The orchestration layer called by the user or MCP Agent.
2.  **Validation**:
    *   **Statelessness**: Ensure that any logic added to `EnvAnalysisCore` does not reference `RhinoDoc` or `RhinoObject`.
    *   **Layer Convention**: Always verify that input geometry follows the `Analysis::...` schema defined in `MCP_INSTRUCTIONS.md`.
4.  **Coverage Audit (The Loop Check)**:
    *   **No Geometry Left Behind**: Every source object identified in the Ingestion phase must be accounted for in the output.
    *   **Gap Analysis**: If an object fails to transform, it must be flagged on `Analysis::Errors::Untransformed` rather than being silently ignored.
    *   **Traceability**: Use Rhino UserText to link simplified outputs back to their original `SourceID`.

## Logic Change & Error Handling Strategies

### 1. Pure Geometry Transformation (Stateless)
To ensure the pipeline is deterministic and testable:
- **Logic**: All transformations (Planarize, SolveAdjacency, CFD Mesh) are implemented as static methods in the `EnvAnalysisCore` namespace. They take raw geometry as input and return modified geometry.
- **Verification**: Any new algorithm must be verified with pure C# unit tests using simulated geometry.

### 2. Document Ingestion (Bridge)
To safely bring Rhino geometry into the pipeline:
- **Logic**: A separate Document Interface Layer (to be built) will crawl the `Analysis::` layers and map them to `AnalysisGeometry` objects for processing.
- **Error Handling**: Missing or malformed layers should be reported via an `Analysis::Verification` sub-layer in the Rhino document.

### 3. CFD Mesh Generation (Morphological Closing)
To create a simplified, watertight urban simulation environment:
- **Logic**: Use a Dilation-Union-Erosion sequence to bridge aerodynamic gaps between buildings.
- **Corner Styles**: Rounded corners (`RG.Round`) are preferred for stability and smooth meshing; Sharp corners (`RG.Sharp`) preserve boxy character but require robust error handling.
- **Incremental Union**: Implement one-by-one Boolean Union to bypass math failures in high-density overlapping spikes.
- **Orientation Control**: Force perimeter curves to Counter-Clockwise (CCW) orientation before extrusion to ensure upward growth.
- **Unique ID Export**: All STL exports must follow the naming convention `GeometryforCFD_[FileName]_[ID].stl` to prevent accidental file duplication or replacement.

---
*Rationale and historical decisions live in `CORE_ARCHITECTURE.md`.*
