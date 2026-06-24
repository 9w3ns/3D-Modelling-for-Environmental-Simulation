# Environmental Analysis Geometry Pipeline

This repository contains a deterministic geometry transformation tool for environmental analysis in Rhino/Grasshopper. It is designed to bridge the gap between design-oriented modeling and analysis-ready inputs for Ladybug, Honeybee, Eddy3D, and Vento.

## Project Structure

```
├── scripts/                          ← Active pipeline (Rhino 8 ScriptEditor)
│   ├── Phase1.cs                     ← Ingestion & Filtering
│   ├── Phase2.cs                     ← Semantic Classification
│   ├── Phase3.cs                     ← Reconstruction
│   ├── cfd_generation.py             ← CFD mesh generation (MCP)
│   └── export_cfd_stl.py             ← CFD STL export (MCP)
│
├── docs/
│   ├── architecture/                 ← System design & pipeline logic
│   │   ├── CORE_ARCHITECTURE.md
│   │   ├── MCP_INSTRUCTIONS.md
│   │   └── RECONSTRUCTION_PIPELINE.md
│   │
│   └── modelling-guides/             ← Per-engine geometry specs
│       ├── MODELLING_GUIDE_CFD.md    ← ⭐ Active MCP guide
│       ├── MODELLING_GUIDE_LADYBUG.md
│       ├── MODELLING_GUIDE_HB_ENERGY.md
│       ├── MODELLING_GUIDE_HB_RADIANCE.md
│       └── CFD_Mesh_Extrusion_Process.md
│
├── archive/                          ← Previous approaches (kept for reference)
│
├── GEMINI.md                         ← Agent rules & guardrails
├── 3D Modelling for Environmental Simulation.xlsx  ← Goal Matrix
└── README.md
```

### Modelling Guides
- `docs/modelling-guides/MODELLING_GUIDE_LADYBUG.md`: Requirements for Sun & Radiation analysis.
- `docs/modelling-guides/MODELLING_GUIDE_HB_RADIANCE.md`: Requirements for Daylight & Glare analysis.
- `docs/modelling-guides/MODELLING_GUIDE_HB_ENERGY.md`: Requirements for Thermal & Comfort analysis.
- `docs/modelling-guides/MODELLING_GUIDE_CFD.md`: Requirements for Wind & CFD simulation.
- `docs/modelling-guides/CFD_Mesh_Extrusion_Process.md`: CFD mesh extrusion sub-process (mesh output).

## Quick Start for AI Agents

If you are an AI agent activated in this workspace, please read `docs/architecture/MCP_INSTRUCTIONS.md` to understand your role and the mandatory layer conventions.