# Environmental Analysis Geometry Pipeline (C# Plugin)

## 1. Objective & Scope
The objective is to build a deterministic C# Rhino Plugin that bridges the gap between design-oriented 3D modeling and the specific geometric requirements of environmental simulation engines (Ladybug, Honeybee, Eddy3D, Vento). 

Furthermore, this tool is designed to be **MCP-Agent friendly**. It will serve as the underlying engine allowing a RhinoMCP assistant to autonomously prep and transform geometry based on user prompts.

## 2. Core Architecture: Pipeline & Adapter Pattern

The plugin will be structured into three main phases, allowing data to flow from raw design layers to analysis-ready outputs.

### Phase 1: Ingestion (Layer Convention Parser)
Raw design geometry will be identified using a strict layer-based convention.
*   **Convention Schema:** Define a standardized layer structure (e.g., `Analysis::Context`, `Analysis::Zones::[ZoneName]::Walls`, `Analysis::Shading`).
*   **Ingestion Logic:** C# methods to recursively scan the layer table, extract objects, and wrap them in an internal data structure (e.g., `AnalysisObject`) that tags their intended role (Aperture, Wall, Context, Roof).

### Phase 2: Core Geometry Engine (Deterministic Transformations)
This is the heart of the plugin, containing deterministic algorithms to alter the geometry.
*   **Simplifier (For Ladybug/Honeybee Energy):** 
    *   Converts complex/curved Breps into simplified planar surfaces.
    *   Reduces polygon count on contextual meshes while preserving bounding volume.
*   **Volume Solver (For Honeybee):**
    *   Ensures that sets of planar surfaces form closed, watertight Breps (Honeybee Rooms).
    *   **Intersection & Adjacency:** Algorithm to intersect adjacent zones to create perfectly matched internal boundary faces (crucial for accurate energy transfer).
*   **Mesher (For CFD - Vento / Eddy3D):**
    *   Generates uniform, watertight meshes from Breps.
    *   Handles boolean unions of context buildings to create a single continuous ground/context mesh for wind analysis.

### Phase 3: Target Adapters (Output Generation)
Formats the processed geometry for the specific engine.
*   **Direct Geometry Adapter (Ladybug / Eddy3D):** Outputs cleaned native Rhino Breps/Meshes directly to the Rhino document or Grasshopper.
*   **Honeybee Adapter (Radiance / Energy):** Packages the simplified planar Breps into structured data that aligns with `hb_room` requirements (potentially outputting HBJSON or direct SDK objects).
*   **Export Adapter (Vento):** Handles the robust export of watertight meshes to `.stl` format with appropriate unit scaling and coordinate origins.

## 3. MCP Assistant Modeller Integration

To allow an LLM (via RhinoMCP) to act as an assistant modeller, the plugin must be accessible and self-documenting.

*   **Command/API Exposure:** Every core phase (Ingestion, Planarize, SolveAdjacency, ExportSTL) must be exposed as a distinct Rhino Command (e.g., `-EnvPrepPlanarize Layer="Analysis::Walls"`). This allows the MCP agent to orchestrate the pipeline via command-line execution.
*   **Agent Documentation (`/docs/mcp/`):** The repository will contain specific markdown files acting as instructions for the agent.
    *   `mcp-capabilities.md`: What transformations the agent can perform.
    *   `mcp-workflow-[engine].md`: Step-by-step guides for the agent (e.g., "To prep for Vento: 1. Ingest layers. 2. Run Watertight Mesher. 3. Run STL Export.").
*   **Workflow:** The user asks: *"Prep this model for a Vento wind analysis."* -> The MCP reads the markdown workflow -> Calls the required C# commands in sequence -> Returns the final `.stl` path to the user.

## 4. Implementation Phases

*   **Phase A: Foundation & Ingestion.** Setup C# Plugin boilerplate. Define the internal `AnalysisObject` class. Write the layer-parsing logic. Write the `mcp-capabilities.md` base file.
*   **Phase B: Ladybug & Simplification.** Implement the Planarization algorithms. Expose commands. Test with direct Sun/Radiation workflows.
*   **Phase C: Honeybee & Adjacency.** Implement the complex volume intersection and adjacency-matching algorithms. Test export to HBJSON or Grasshopper transfer.
*   **Phase D: CFD & Meshing.** Implement watertight boolean union and meshing logic. Implement `.stl` export for Vento.

## 5. Verification
*   **Unit Tests:** Pure C# unit tests for the geometry algorithms (e.g., asserting that `Planarize(curvedWall)` results in a flat surface).
*   **Visual Validation:** Generate temporary visualization layers (e.g., highlighting mismatched adjacencies in red) so the human user or the MCP agent can verify the transformation before running the heavy simulation.