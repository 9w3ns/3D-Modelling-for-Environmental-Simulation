# Integrated Workflow: Phase 1 (Geometry Ingestion & Filtering)

This document tracks all the architectural decisions, pipeline strategies, and bug fixes applied to the 3D Modelling Pipeline during Phase 1 development.

## 1. Deconstruction of the Monolithic Command
**Decision:** We split the original `EnvPrepPhase1To3Command.cs` into three distinct phases (`Phase1`, `Phase2`, and `Phase3`).
**Rationale:** A monolithic script makes it impossible to visually debug errors. By baking the geometry at the end of each phase to a dedicated `Analysis::PhaseX` layer tree, the user can verify the outputs, identify broken geometry, and safely iterate without losing data.

## 2. Shift to Rhino 8 ScriptEditor for Rapid Iteration
**Decision:** We transitioned from compiling `.rhp` plugins to using standalone C# scripts (`scripts/Phase1.cs`) loaded directly via Rhino 8's native `ScriptEditor`.
**Rationale:** Reloading a `.rhp` file requires constantly restarting Rhino due to AppDomain locking. Running raw C# scripts allows for instant, live execution of code changes (just pressing "Play") while keeping the entire codebase in C#, bypassing the need to translate back and forth from Python.

## 3. The Nested Block Accumulation Fix
**Issue:** Extracted geometry from nested blocks was spawning at the world origin `(0,0,0)` with massive scale mismatches.
**Decision:** We updated the `TraverseBlock` recursive function to accept and pass down a `Transform parentTransform`.
**Rationale:** When iterating through nested blocks, the local `InstanceXform` of a child block must be multiplied by its parent's transform matrix (`parentTransform * instance.InstanceXform`) so that the real-world scale, rotation, and position are accumulated perfectly before the geometry is duplicated.

## 4. Nested Layer Hierarchy Generation
**Issue:** The 9,000+ output geometries were being dumped onto the `Default` layer instead of `Analysis::Phase1::Ingested`.
**Decision:** Replaced the standard `doc.Layers.FindName()` method with `doc.Layers.FindByFullPath(path, RhinoMath.UnsetIntIndex)`.
**Rationale:** Rhino's basic `FindName` does not support the `::` nested path syntax natively. Searching by the exact full path ensures that the Phase 1, 2, and 3 layer trees are properly built, organized, and colored.

## 5. Bounding Box Area Filter (Option C)
**Issue:** Phase 1 was ingesting thousands of incredibly detailed sub-elements (like 2cm x 1.7cm x 89.5cm window mullions and `0.02m` thick shading louvers) that crash environmental simulation engines and ruin analysis meshing.
**Decision:** Implemented a highly performant **Surface Area Filter** directly into the Phase 1 Ingestion loop. Any geometry with an estimated Bounding Box Surface Area of less than `2.0 m²` is immediately dropped.
**Rationale:** Computing true `AreaMassProperties` on 9,000+ objects crashes Rhino. The Bounding Box Area `2.0 * ((dx*dy) + (dy*dz) + (dx*dz))` is O(1) mathematically and acts as a perfect proxy to purge complex hardware, mullions, and thin decorative louvers, while preserving structural floor plates and walls.
