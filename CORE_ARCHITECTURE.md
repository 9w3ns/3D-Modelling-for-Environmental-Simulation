# Environmental Analysis Geometry Pipeline (C# Plugin)

## 1. Objective & Scope
The objective is to build a deterministic C# Rhino Plugin that bridges the gap between design-oriented 3D modeling and the specific geometric requirements of environmental simulation engines (Ladybug, Honeybee, Eddy3D, Vento). 

Furthermore, this tool is designed to be **MCP-Agent friendly**. It will serve as the underlying engine allowing a RhinoMCP assistant to autonomously prep and transform geometry based on user prompts.

### C. Coverage Audit & Traceability
To prevent accidental data loss during automated transformation, the system implements a "No Geometry Left Behind" loop check.

#### 1. Ingestion Catalog (The Receipt)
Before any transformation begins, the **Document Interface Layer** creates a hash-set of every unique `RhinoObject.Id` from the targeted source layers (e.g., `Context 1`). This is the "Expected Catalog."

#### 2. Traceability Tagging
During the **Core Engine** phase, every newly generated piece of geometry (simplified block, tier, etc.) must be tagged with the `SourceID` of the architectural object(s) it represents.
*   **Implementation**: Store the original GUID in the `UserText` of the baked object.

#### 3. Gap Analysis (The Loop Check)
After the final bake, the system performs a comparison:
*   **Loop**: Iterate through the "Expected Catalog" and verify that every ID is referenced by at least one object on the `Analysis::...` output layers.
*   **Exception Handling**: Any ID in the catalog that is *not* represented in the final output is flagged as a "Processing Gap."

#### 4. Automated Error Routing
Objects that fail the transformation loop are automatically moved to:
*   `Analysis::Errors::Untransformed`: This allows the MCP Agent or the user to manually inspect messy topology or retry with a "Robust Fallback" (e.g., simple bounding box extrusion).


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