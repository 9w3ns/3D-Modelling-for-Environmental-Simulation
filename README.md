# Environmental Analysis Geometry Pipeline

This repository contains a deterministic geometry transformation tool for environmental analysis in Rhino/Grasshopper. It is designed to bridge the gap between design-oriented modeling and analysis-ready inputs for Ladybug, Honeybee, Eddy3D, and Vento.

## Project Structure

- `CORE_ARCHITECTURE.md`: Detailed architectural plan for the C# transformation pipeline.
- `MCP_INSTRUCTIONS.md`: Foundational instructions for AI agents (like RhinoMCP) to operate this tool.
- `src/EnvAnalysisCore/`: C# project containing the core transformation logic.

### Modeling Guides
- `MODELLING_GUIDE_LADYBUG.md`: Requirements for Sun & Radiation analysis.
- `MODELLING_GUIDE_HB_RADIANCE.md`: Requirements for Daylight & Glare analysis.
- `MODELLING_GUIDE_HB_ENERGY.md`: Requirements for Thermal & Comfort analysis.
- `MODELLING_GUIDE_CFD.md`: Requirements for Wind & CFD simulation (formerly Context Mesh Simplification).

## Quick Start for AI Agents

If you are an AI agent activated in this workspace, please read `MCP_INSTRUCTIONS.md` to understand your role and the mandatory layer conventions.