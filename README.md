# 3D Modelling for Environmental Simulation

A deterministic geometry transformation pipeline that converts raw architectural 3D models in **Rhino 8** into simulation-ready inputs for environmental analysis engines: **Ladybug** (sun/radiation), **Honeybee** (energy/daylight), and **CFD solvers** like Vento and Eddy3D (wind flow).

> **This project does NOT contain simulation logic.** It focuses exclusively on geometric preparation — cleaning, classifying, and simplifying 3D models so they meet each simulation engine's strict input requirements. The actual simulation scripts (Grasshopper definitions for radiation, sun hours, thermal loads, etc.) are separate, standardized tools that consume the geometry this pipeline produces.

---

## Table of Contents

- [What Problem Does This Solve?](#what-problem-does-this-solve)
- [How It Works — The Pipeline](#how-it-works--the-pipeline)
- [Prerequisites](#prerequisites)
- [Project Structure](#project-structure)
- [Primary Files (Actively Developed)](#primary-files-actively-developed)
- [Script Manuals](#script-manuals)
  - [Phase1.cs — Ingestion & Filtering](#phase1cs--ingestion--filtering)
  - [Phase2.cs — Semantic Classification](#phase2cs--semantic-classification)
  - [Phase3.cs — Geometry Reconstruction](#phase3cs--geometry-reconstruction)
  - [LadybugMassExtraction.py — Mass Simplification](#ladybugmassextractionpy--mass-simplification)
  - [MODELLING_GUIDE_CFD.md — CFD Preparation Guide](#modelling_guide_cfdmd--cfd-preparation-guide)
- [Layer Convention](#layer-convention)
- [The Goal Matrix](#the-goal-matrix)
- [For AI Agents (MCP)](#for-ai-agents-mcp)
- [Archive & Previous Approaches](#archive--previous-approaches)
- [Contributing](#contributing)

---

## What Problem Does This Solve?

Architectural 3D models are built for visual representation — they contain fine details like mullions, hardware, decorative elements, and complex curved surfaces. Environmental simulation engines require the opposite: **clean, simplified, watertight geometry** with specific topological properties.

| Simulation Engine | What It Needs | Why Raw Models Fail |
|:---|:---|:---|
| **Ladybug** (Radiation/Sun) | Single-surface simplified masses | Too many faces, unnecessary detail, curved surfaces |
| **Honeybee** (Energy/Daylight) | Watertight 2.5D volumes with coplanar apertures | Open geometry, non-manifold edges, misaligned windows |
| **Vento / Eddy3D** (CFD/Wind) | Watertight manifold meshes (STL) | Gaps between buildings, non-manifold geometry, inverted normals |

This pipeline bridges that gap by automating the transformation from design model → analysis-ready geometry.

---

## How It Works — The Pipeline

The transformation runs in **three sequential phases**, each building on the output of the previous:

```
┌─────────────────────────────────────────────────────────────────────┐
│                        RAW RHINO MODEL                              │
│         (Blocks, Breps, Extrusions, Meshes on any layers)          │
└───────────────────────────┬─────────────────────────────────────────┘
                            │
                    ┌───────▼────────┐
                    │    PHASE 1     │  Ingestion & Filtering
                    │   Phase1.cs    │  → Explode blocks, purge small details
                    │                │  → Copy valid geometry to Analysis:: layers
                    └───────┬────────┘
                            │  Output: Analysis::Phase1::Ingested
                    ┌───────▼────────┐
                    │    PHASE 2     │  Semantic Classification
                    │   Phase2.cs    │  → Classify each piece as Wall, Floor,
                    │                │    Roof, Aperture, Shading, or Context
                    └───────┬────────┘
                            │  Output: Analysis::Phase2::Walls, ::Floors, etc.
                    ┌───────▼────────┐
                    │    PHASE 3     │  Reconstruction
                    │   Phase3.cs    │  → Rebuild simplified, watertight solids
                    │                │  → Boolean Union, contour slicing, smoothing
                    └───────┬────────┘
                            │  Output: Analysis::Phase3::Reconstructed::*
                            ▼
              ┌──────────────────────────────┐
              │   SIMULATION-READY GEOMETRY  │
              │  (Ladybug / Honeybee / CFD)  │
              └──────────────────────────────┘
```

**Additionally**, there is a standalone Python script (`LadybugMassExtraction.py`) that performs an independent mass simplification specifically optimized for Ladybug workflows, and a CFD preparation guide (`MODELLING_GUIDE_CFD.md`) that instructs the MCP agent on how to prepare geometry for wind simulation.

---

## Prerequisites

| Requirement | Version | Notes |
|:---|:---|:---|
| **Rhino** | 8+ | Required for ScriptEditor support (C# top-level statements) |
| **RhinoCommon** | Built-in | Ships with Rhino — no separate install needed |
| **Python** (for `.py` scripts) | IronPython (built into Rhino) | Use Rhino's `EditPythonScript` or `RunPythonScript` |
| **Grasshopper** (optional) | Built into Rhino 8 | Only needed if running the downstream simulation definitions |

No external packages or NuGet dependencies are required. All scripts use only RhinoCommon (`Rhino.Geometry`, `Rhino.DocObjects`).

---

## Project Structure

```
3D-Modelling-for-Environmental-Simulation/
│
├── scripts/                              ← ⭐ ACTIVE PIPELINE (start here)
│   ├── Phase1.cs                         ← Ingestion & Filtering
│   ├── Phase2.cs                         ← Semantic Classification
│   ├── Phase3.cs                         ← Geometry Reconstruction
│   ├── cfd_generation.py                 ← CFD mesh generation (used by MCP agent)
│   └── export_cfd_stl.py                 ← CFD STL export helper (used by MCP agent)
│
├── docs/
│   ├── architecture/                     ← System design & technical specs
│   │   ├── CORE_ARCHITECTURE.md          ← Three-layer architecture, traceability
│   │   ├── MCP_INSTRUCTIONS.md           ← AI agent operating manual
│   │   └── RECONSTRUCTION_PIPELINE.md    ← Detailed 5-phase Honeybee pipeline
│   │
│   └── modelling-guides/                 ← Per-engine geometry requirements
│       ├── MODELLING_GUIDE_CFD.md        ← ⭐ Active CFD guide (most tested)
│       ├── MODELLING_GUIDE_LADYBUG.md    ← Ladybug geometry requirements
│       ├── MODELLING_GUIDE_HB_ENERGY.md  ← Honeybee Energy requirements
│       ├── MODELLING_GUIDE_HB_RADIANCE.md← Honeybee Radiance requirements
│       └── CFD_Mesh_Extrusion_Process.md ← CFD mesh extrusion sub-process
│
├── archive/                              ← Previous approaches (reference only)
│   ├── python-scripts/
│   │   ├── LadybugMassExtraction.py      ← ⭐ Standalone mass simplifier
│   │   ├── PipelinePhase1.py             ← Earlier Python Phase 1
│   │   ├── PipelinePhase2.py             ← Earlier Python Phase 2
│   │   ├── PipelinePhase3.py             ← Earlier Python Phase 3
│   │   └── CleanupAndExtract.py          ← Utility script
│   ├── plugin/EnvAnalysisCore/           ← Earlier C# Rhino Plugin approach
│   ├── prototypes/                       ← Earliest experiments
│   └── docs/                             ← Superseded documentation
│
├── references/
│   └── ladybug_radiation_script.gh       ← Reference Grasshopper definition
│
├── 3D Modelling for Environmental
│   Simulation.xlsx                       ← THE GOAL MATRIX (source of truth)
├── GEMINI.md                             ← Agent rules & guardrails
└── README.md                             ← You are here
```

---

## Primary Files (Actively Developed)

These are the files that are **working, tested, and actively maintained**:

| File | Language | Purpose | Status |
|:---|:---|:---|:---|
| `scripts/Phase1.cs` | C# | Extract & filter geometry from the model | ✅ Active |
| `scripts/Phase2.cs` | C# | Classify geometry by building element type | ✅ Active |
| `scripts/Phase3.cs` | C# | Reconstruct simplified watertight solids | ✅ Active |
| `archive/python-scripts/LadybugMassExtraction.py` | Python | Simplify complex masses into polysurfaces | ✅ Active |
| `docs/modelling-guides/MODELLING_GUIDE_CFD.md` | Markdown | MCP agent guide for CFD preparation | ✅ Active |

Everything else in the repository is either documentation, archived previous approaches, or supporting files.

---

## Script Manuals

### Phase1.cs — Ingestion & Filtering

📁 **Location:** `scripts/Phase1.cs`
🔤 **Language:** C# (Rhino 8 ScriptEditor — top-level statements)

#### What It Does

Phase 1 is the **entry point** of the pipeline. It crawls through designated source layers in your Rhino model, extracts all valid 3D geometry (including exploding block instances recursively), filters out small architectural details that would only slow down simulations, and copies the surviving geometry to a clean `Analysis::Phase1::Ingested` layer with full traceability metadata.

#### How It Works — Step by Step

1. **Reads source layers** — Scans objects from three source layer names:
   - `Target Geometry`
   - `Model::Buildings`
   - `Model::Context`
2. **Cleans the output** — Deletes any existing objects on `Analysis::Phase1::Ingested` to ensure a fresh run.
3. **Explodes block instances** — Recursively traverses nested blocks, applying accumulated transforms so each sub-object gets its correct world position.
4. **Purges non-geometry** — Skips Points, PointSets, and Curves (these have no volume for simulation).
5. **Filters small details** — Two-pass area filter removes elements too small to affect simulation:
   - **Height filter:** Objects thinner than 0.10m vertically are dropped (thin mullions, hardware).
   - **Bounding Box area pre-filter:** If the bounding box surface area < 2.0 m², dropped immediately (fast).
   - **True area filter:** If the bounding box was large enough to pass, the actual surface area is computed. If < 2.0 m², dropped.
6. **Bakes results** — Surviving geometry is copied to `Analysis::Phase1::Ingested` with `UserDictionary` metadata:
   - `SourceID`: The GUID of the original parent object.
   - `OriginalLayer`: The layer the object came from.
   - `OriginalMaterial`: The material name (if assigned).

#### How to Run

1. Open your `.3dm` file in **Rhino 8**.
2. Make sure your buildings are on one of the recognized source layers (`Target Geometry`, `Model::Buildings`, or `Model::Context`).
3. Open the **ScriptEditor** (`ScriptEditor` command in Rhino).
4. Open `Phase1.cs`, or paste its contents into a new C# script tab.
5. Click **Run** (▶).
6. Watch the command line for output:
   ```
   Starting Phase 1 (Script Mode): Ingestion and Base Filtering...
   Phase 1 complete. Ingested 247 base geometries.
   Filtered and dropped 83 small details (mullions/hardware).
   ```
7. In the Layers panel, you'll see the new `Analysis::Phase1::Ingested` layer with all valid geometry.

#### Inputs / Outputs

| | Details |
|:---|:---|
| **Input** | Any Rhino `.3dm` file with 3D geometry on `Target Geometry`, `Model::Buildings`, or `Model::Context` layers |
| **Output** | Filtered geometry on `Analysis::Phase1::Ingested` layer |
| **Metadata** | Each object tagged with `SourceID`, `OriginalLayer`, `OriginalMaterial` |

#### Important Notes

- The script is **idempotent** — running it again cleans the output layer and re-processes from scratch.
- Block instances are fully exploded; the original blocks are not modified.
- The 2.0 m² area threshold is tuned for typical architectural models at building scale. Adjust if working with small-scale models.

---

### Phase2.cs — Semantic Classification

📁 **Location:** `scripts/Phase2.cs`
🔤 **Language:** C# (Rhino 8 ScriptEditor — top-level statements)

#### What It Does

Phase 2 takes the filtered geometry from Phase 1 and **classifies every object** into a semantic category: **Wall**, **Floor**, **Roof**, **Aperture**, **Shading**, or **Context**. Classification uses a multi-signal approach combining layer name hints, material names, face normal analysis, and bounding box proportions.

#### How It Works — Step by Step

1. **Reads input** — Loads all objects from `Analysis::Phase1::Ingested`.
2. **Cleans output layers** — Deletes existing objects on all `Analysis::Phase2::*` sub-layers.
3. **Classifies each object** using a priority cascade:
   - **Layer name hints** (highest priority):
     - Layer contains "Context" → `Context`
     - Layer contains "Roof" → `Roof`
     - Layer contains "Floor" or "Slab" → `Floor`
     - Layer contains "Wall" → `Wall`
     - Layer contains "Glass", "Glazing" → `Aperture`
     - Material contains "Glass", "Transparent" → `Aperture`
   - **Face normal analysis** (if layer name is inconclusive):
     - For Brep/Extrusion: computes the area of all faces, classifies each face normal (< 20° from up = Roof, > 160° from up = Floor, else = Wall), then the entire object takes the category with the most surface area.
     - For Mesh: same logic using mesh face normals and triangle areas.
   - **Bounding box proportions** (last resort):
     - Thin and flat (Z: 0.05–0.60m, wide) → `Floor`
     - Narrow and tall (X or Y ≤ 0.5m, Z > 1.0m) → `Wall`
     - Otherwise → `Shading`
4. **Override rules** (post-classification corrections):
   - Thick wall masses: If classified as Floor/Roof but taller than 1.0m → reclassified as `Wall`.
   - Thin shadings: If classified as Floor/Roof but thinner than 0.10m → reclassified as `Shading`.
   - Columns: If classified as Wall but shorter than 1.2m in both horizontal dimensions → reclassified as `Shading`.
   - Parapets: If classified as Wall but shorter than 1.0m tall → reclassified as `Shading`.
5. **Bakes results** — Copies geometry to category-specific layers with `SourceID` preserved.

#### How to Run

1. **Run Phase 1 first** — Phase 2 reads from `Analysis::Phase1::Ingested`.
2. Open `Phase2.cs` in the Rhino 8 **ScriptEditor**.
3. Click **Run** (▶).
4. Check the command line:
   ```
   Starting Phase 2 (Script Mode): Semantic Tagging...
   Phase 2 complete. Categorized 247 geometries.
   ```
5. In the Layers panel, you'll see:
   - `Analysis::Phase2::Walls`
   - `Analysis::Phase2::Floors`
   - `Analysis::Phase2::Roofs`
   - `Analysis::Phase2::Apertures`
   - `Analysis::Phase2::Shading`
   - `Analysis::Phase2::Context`

#### Inputs / Outputs

| | Details |
|:---|:---|
| **Input** | Geometry on `Analysis::Phase1::Ingested` (output of Phase 1) |
| **Output** | Geometry sorted onto `Analysis::Phase2::Walls`, `::Floors`, `::Roofs`, `::Apertures`, `::Shading`, `::Context` |
| **Prerequisite** | Phase 1 must have been run first |

#### Classification Reference

| Category | How It's Detected |
|:---|:---|
| **Floor** | Upward-facing normals + thin horizontal slab shape (0.10–0.60m thick) |
| **Wall** | Horizontal-facing normals + narrow vertical shape (> 1.0m tall, > 1.2m wide) |
| **Roof** | Upward-facing normals at topmost elevation |
| **Aperture** | Layer/material name contains "Glass", "Glazing", "Window", or "Transparent" |
| **Shading** | Catch-all for small elements, columns, parapets, thin elements |
| **Context** | Layer name contains "Context" |

---

### Phase3.cs — Geometry Reconstruction

📁 **Location:** `scripts/Phase3.cs`
🔤 **Language:** C# (Rhino 8 ScriptEditor — top-level statements)

#### What It Does

Phase 3 is the **reconstruction engine** — the most complex script in the pipeline. It takes the classified walls, floors, and roofs from Phase 2 and rebuilds them into **simplified, watertight solids** suitable for simulation. It uses a combination of contour slicing, grid-based rasterization, morphological operations (dilation + flood fill + erosion), and Boolean Union to produce clean massing geometry.

#### How It Works — Step by Step

**Wall Reconstruction (Solid Mass Contour Slicing):**

1. Converts all wall geometry to meshes and joins them into a single unified mesh.
2. Slices the mesh with horizontal planes at 2.0m intervals from bottom to top.
3. For each slice, rasterizes the intersection curves onto a 0.50m-resolution grid.
4. Applies **morphological closing** on the grid:
   - **Dilation**: Expands filled cells by 1 pixel (8-connectivity) to bridge small gaps.
   - **Flood fill**: Marks all exterior cells (connected to grid corner) as empty.
   - **Erosion**: Shrinks solid cells by 1 pixel to restore approximate original boundaries, while keeping internal voids filled.
5. **Vertical merging**: Adjacent slices with ≥ 80% similarity are merged into a single block.
6. Converts each grid block into a closed curve footprint → extrudes to the block's height.
7. **Iterative Boolean Union**: Merges all blocks one-by-one. If a union fails, the pieces are kept separate (never deleted).
8. Applies **RDP smoothing** (Ramer-Douglas-Peucker) to eliminate grid staircase artifacts from the outlines.

**Floor & Roof Reconstruction (Raycast Footprint):**

1. Combines floors and roofs into a single list, sorted by Z elevation.
2. Groups them by elevation using **agglomerative clustering** (0.50m gap tolerance).
3. For each cluster:
   - Converts all geometry to meshes.
   - Fires downward rays on a 0.50m grid from above the cluster.
   - Builds a boolean hit-map of which grid cells contain geometry.
   - Converts hit cells to square curves, boolean-unions them into footprint outlines.
   - Applies RDP smoothing to the outlines.
   - Extrudes footprints to the cluster's maximum thickness.
4. Falls back to bounding box if raycasting produces no results.

#### How to Run

1. **Run Phase 1 and Phase 2 first** — Phase 3 reads from `Analysis::Phase2::*` layers.
2. Open `Phase3.cs` in the Rhino 8 **ScriptEditor**.
3. Click **Run** (▶).
4. Check the command line:
   ```
   Starting Phase 3 (Script Mode): Geometry Reconstruction...
   Reconstructing 142 Walls using Solid Mass Contour Slicing...
   Reconstructing 87 Floors and 12 Roofs using Silhouette-Union...
   Phase 3 complete! Geometry has been reconstructed into watertight solids.
   ```
5. Results appear on:
   - `Analysis::Phase3::Reconstructed::Walls` (orange)
   - `Analysis::Phase3::Reconstructed::Floors` (dark gray)

#### Inputs / Outputs

| | Details |
|:---|:---|
| **Input** | Geometry on `Analysis::Phase2::Walls`, `::Floors`, `::Roofs` (output of Phase 2) |
| **Output** | Simplified watertight Breps on `Analysis::Phase3::Reconstructed::Walls` and `::Floors` |
| **Prerequisite** | Phase 1 and Phase 2 must have been run first |

#### Key Parameters (Hardcoded)

| Parameter | Value | Purpose |
|:---|:---|:---|
| `sliceInterval` | 2.0m | Vertical spacing between wall contour slices |
| `resolution` | 0.50m | Grid cell size for rasterization |
| `similarityThreshold` | 0.80 (80%) | Minimum overlap for merging adjacent slices |
| `rdpTolerance` | 0.75–1.50m | Ramer-Douglas-Peucker smoothing strength |
| Elevation gap tolerance | 0.50m | Maximum Z-gap for grouping floors/roofs |

---

### LadybugMassExtraction.py — Mass Simplification

📁 **Location:** `archive/python-scripts/LadybugMassExtraction.py`
🔤 **Language:** Python (RhinoPython / IronPython)

#### What It Does

This is a **standalone Python script** that simplifies complex building masses into clean, low-face-count polysurfaces optimized for Ladybug radiation analysis. Unlike the Phase 1→2→3 pipeline (which classifies elements by type), this script treats each building as a single mass and produces a **tiered "wedding cake" approximation** — preserving the overall height profile while eliminating internal complexity.

#### How It Works — Step by Step

1. **Gathers geometry** from the `Target Geometry` layer.
2. **Explodes block instances** recursively, inheriting layer and material metadata.
3. **Filters out small details**:
   - Diagonal < 0.4m → dropped
   - Cross-section < 0.2m × 0.2m → dropped
   - Solidity ratio < 55% (hollow frames) → dropped
   - Area < 0.1 m² → dropped
   - **Exception:** Glass/glazing objects bypass all filters.
4. **De-duplicates** using center-point + area signature hashing.
5. Converts all surviving geometry to meshes and joins into a single raycast mesh.
6. **Slices the mesh** horizontally at configurable intervals (default 1.0m).
7. For each slice height:
   - Intersects the mesh with a horizontal plane to get contour curves.
   - **Rasterizes** contours onto a 2D grid.
   - Applies **morphological closing** (Dilation → Flood Fill → Erosion) to fill internal gaps and create solid footprints.
8. **Merges similar slices** vertically — if two adjacent slices are ≥ 80% similar, they merge into a single block.
9. Converts each block's grid to **smoothed polyline curves** (using custom Ramer-Douglas-Peucker implementation).
10. **Extrudes** each footprint to its block height and applies **iterative Boolean Union**.
11. Runs `MergeCoplanarFaces` on the final result to minimize face count.
12. Bakes results to `Analysis::Ladybug_Test_Output` and contour curves to `Analysis::Ladybug_Test_Contours`.

#### How to Run

1. Open your `.3dm` file in **Rhino 7 or 8**.
2. Make sure your buildings are on a layer called `Target Geometry`.
3. Run the `EditPythonScript` command in Rhino to open the Python editor.
4. Open `LadybugMassExtraction.py` or paste its contents.
5. Click **Run**.
6. A **command-line options prompt** appears — you can adjust parameters or press Enter for defaults:

   | Option | Default | Description |
   |:---|:---|:---|
   | `SliceInterval` | 1.0m | Vertical distance between horizontal slices |
   | `GridResolution` | 1.0m | Size of each rasterization grid cell |
   | `SmoothingTol` | 1.0m | RDP smoothing tolerance for outline simplification |
   | `SimilarityThresh` | 0.80 | Minimum grid overlap (0–1) to merge adjacent slices |

7. The script runs and reports progress:
   ```
   Using parameters - Slice: 1.00, Grid: 1.00, Smooth: 1.00, Similarity: 0.80
   Gathering and filtering Target Geometry...
   Slicing from Z=0.0 to 45.2 in 1.0m intervals...
   Tier grouping complete. Found 6 distinct vertical blocks. Extruding and Unioning...
   Phase 3 (Ladybug) Complete in 12.3s.
   ```
8. Results appear on:
   - `Analysis::Ladybug_Test_Output` — Final simplified polysurfaces (orange)
   - `Analysis::Ladybug_Test_Contours` — 2D footprint curves for visual inspection (cyan)

#### Inputs / Outputs

| | Details |
|:---|:---|
| **Input** | Building geometry on the `Target Geometry` layer |
| **Output** | Simplified polysurfaces on `Analysis::Ladybug_Test_Output` |
| **Contours** | 2D footprint curves on `Analysis::Ladybug_Test_Contours` |
| **Parameters** | Configurable via command-line options at runtime |

#### When to Use This vs. Phase 1→2→3

| Scenario | Use This Script | Use Phase 1→2→3 |
|:---|:---|:---|
| Quick Ladybug radiation study | ✅ | |
| Need classified walls/floors/roofs | | ✅ |
| Honeybee energy model preparation | | ✅ |
| Single-mass simplification | ✅ | |
| Detailed element-level control | | ✅ |

---

### MODELLING_GUIDE_CFD.md — CFD Preparation Guide

📁 **Location:** `docs/modelling-guides/MODELLING_GUIDE_CFD.md`
🔤 **Language:** Markdown (documentation)

#### What It Is

This is **not a script** — it is an **operational reference document** that guides the MCP (Model Context Protocol) AI agent through the process of preparing geometry for CFD (Computational Fluid Dynamics) wind simulation using Vento or Eddy3D. It is the most tested and refined modelling guide in the project.

#### What It Covers

The guide documents the complete methodology for converting a "forest" of detailed, individual building meshes into a reduced set of watertight, manifold blocks suitable for volumetric mesh generation:

1. **The Problem** — Why raw urban models fail in CFD (over-calculation in narrow gaps, meshing glitches from jagged edges).

2. **The "10-Meter Gap" Rule** — Buildings within 10m of each other are treated as a single aerodynamic obstacle and merged.

3. **Morphological Closing Technique** (the core algorithm):
   - **Step 1 — Extract True Outlines:** Get the exact 2D perimeter of every mesh.
   - **Step 2 — Dilation (Offset Outwards by 5m):** Expand footprints until neighbors intersect.
   - **Step 3 — Boolean Union:** Merge all expanded, intersecting curves into a single boundary.
   - **Step 4 — Erosion (Offset Inwards by 5m):** Shrink the unified boundary back, keeping internal gaps bridged.

4. **Corner Style Options:**
   - **Rounded** (`CurveOffsetCornerStyle.Round`) — Stable, smooth, CFD-friendly. Preferred.
   - **Sharp** (`CurveOffsetCornerStyle.Sharp`) — Preserves boxy character but prone to spikes.

5. **Technical Guardrails:**
   - Incremental Boolean Union (one-by-one, not all-at-once)
   - Forced CCW orientation before extrusion
   - Z=0 planarity projection for reliable Boolean operations
   - Height tiering (10m bins) for "wedding cake" topographies
   - Area-sorted incremental union for stability

6. **Evolution Log** — Documents how the script evolved through three attempts (BBox Union → True Outlines → Morphological Closing) with failure analysis for each.

#### How to Use It

This guide is consumed in two ways:

**For AI Agents (MCP):**
The MCP agent reads this guide at the start of a CFD preparation session and follows its instructions step-by-step, executing the morphological closing algorithm via `run_python` or `run_csharp` commands in Rhino.

**For Human Users:**
Read it to understand the logic behind CFD geometry simplification. The methodology section explains *why* certain approaches work and others fail, which is valuable if you need to customize the algorithm for unusual urban layouts.

---

## Layer Convention

The pipeline uses a hierarchical `Analysis::` layer schema to organize geometry through each phase:

```
Analysis::
├── Phase1::Ingested          ← Raw filtered geometry (Phase 1 output)
├── Phase2::                  ← Classified geometry (Phase 2 output)
│   ├── Walls
│   ├── Floors
│   ├── Roofs
│   ├── Apertures
│   ├── Shading
│   └── Context
├── Phase3::Reconstructed::   ← Simplified solids (Phase 3 output)
│   ├── Walls
│   └── Floors
├── Ladybug_Test_Output       ← Mass extraction results
├── Ladybug_Test_Contours     ← Mass extraction debug curves
├── Errors::Untransformed     ← Objects that failed transformation
└── Verification              ← Visual debugging layer
```

All layers are created automatically by the scripts if they don't exist.

---

## The Goal Matrix

The file `3D Modelling for Environmental Simulation.xlsx` at the project root is the **definitive Goal Matrix** for this project. It defines exactly what **Geometry Type** and **Level of Detail (LOD)** each simulation engine requires. Every transformation pathway must be engineered to comply with this matrix.

---

## For AI Agents (MCP)

If you are an AI agent activated in this workspace:

1. **Read** `docs/architecture/MCP_INSTRUCTIONS.md` — your role definition and mandatory constraints.
2. **Read** the Goal Matrix (`3D Modelling for Environmental Simulation.xlsx`) to understand target requirements.
3. **Read** `scripts/Phase1.cs`, `scripts/Phase2.cs`, `scripts/Phase3.cs` — the active pipeline implementation.
4. **Read** the relevant modelling guide for the user's requested engine in `docs/modelling-guides/`.
5. **Follow** the layer convention strictly — all geometry must flow through `Analysis::` layers.

---

## Archive & Previous Approaches

The `archive/` folder contains earlier implementations kept for reference:

| Folder | Contents | Why Archived |
|:---|:---|:---|
| `archive/plugin/EnvAnalysisCore/` | Full C# Rhino Plugin (3-layer architecture) | Replaced by standalone scripts for faster iteration |
| `archive/python-scripts/` | Python implementations of all phases + mass extraction | C# scripts are faster and more reliable |
| `archive/prototypes/` | Early verification experiments | Superseded by current pipeline |
| `archive/docs/` | Earlier documentation and decision logs | Superseded by current docs |

---

## Contributing

When modifying the pipeline:

1. **Never delete source geometry** — if a transformation fails, route objects to `Analysis::Errors::Untransformed` rather than silently dropping them ("No Geometry Left Behind" rule).
2. **Preserve traceability** — all output geometry must carry a `SourceID` linking back to the original object.
3. **Handle all geometry types** — logic must explicitly support `Brep`, `Extrusion`, and `Mesh`. Never assume `coercebrep` will always work.
4. **Test with Phase 1 → 2 → 3 in sequence** — each phase depends on the previous one's output.
5. **Read `GEMINI.md`** for the full set of architectural invariants and anti-regression guardrails.

---

*Last updated: June 2026*