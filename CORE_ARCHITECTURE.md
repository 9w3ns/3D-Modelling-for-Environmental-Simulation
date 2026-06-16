# Environmental Analysis Geometry Pipeline (C# Plugin)

## 1. Objective & Scope
The objective is to build a deterministic C# Rhino Plugin that bridges the gap between design-oriented 3D modeling and the specific geometric requirements of environmental simulation engines (Ladybug, Honeybee, Eddy3D, Vento). 

Furthermore, this tool is designed to be **MCP-Agent friendly**. It will serve as the underlying engine allowing a RhinoMCP assistant to autonomously prep and transform geometry based on user prompts.

## 2. Core Architecture: Pure Logic & Adapter Pattern

The plugin is structured to separate stateless geometric algorithms from the Rhino Document state. This ensures the system is deterministic, testable, and robust when orchestrated by an MCP Agent.

### Layer 1: Pure Logic Core (Stateless Algorithms)
This is the heart of the plugin. It contains the deterministic algorithms to transform geometry.
*   **Dependency**: Only `Rhino.Geometry`.
*   **Constraint**: No knowledge of `RhinoDoc`, `RhinoObject`, or layers.
*   **Components**:
    *   **Simplifier (For Ladybug/Honeybee)**: Logic to planarize complex/curved Breps.
    *   **Volume Solver (For Honeybee)**: Intersection & adjacency algorithms to create matched boundary faces.
    *   **Mesher (For CFD)**: Logic to boolean union and mesh context for wind analysis.

### Layer 2: Document Interface Layer (Stateful Bridge)
This layer acts as the bridge between the Rhino environment and the Pure Logic Core.
*   **Responsibility**: Parsing layers (Ingestion), extracting geometry, and baking results back to the document.
*   **Convention Schema**: Standardized layer structure (e.g., `Analysis::Context`, `Analysis::Zones::[ZoneName]::Walls`).

### Layer 3: Command & Agent Adapters (Orchestration)
The entry point for users and the MCP Agent.
*   **Rhino Commands**: Exposes the pipeline via command-line execution (e.g., `-EnvPrepPlanarize`).
*   **Workflow Integration**: The MCP Agent reads instruction files and calls these commands in sequence.

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