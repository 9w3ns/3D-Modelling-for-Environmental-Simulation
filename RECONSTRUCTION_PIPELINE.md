# 2.5D Reconstruction Pipeline for Environmental Simulation

## Objective
Develop a robust, automated pipeline to ingest raw architectural geometry, cull irrelevant details, categorize elements, and rebuild a simplified 2.5D "shrinkwrap" model suitable for Honeybee/Ladybug environmental simulations.

## Phased Development Strategy

To ensure stability, the pipeline will be developed and tested in five distinct phases.

### Phase 1: Ingestion & Base Filtering (Filters 01 & 02)
**Goal:** Clean the raw input and expose all valid geometry while protecting critical simulation elements (like glass).
*   **Step 0 (Block Explosion):** Recursively explode all block instances. **Crucial:** Inherit parent layer and material names so tagging metadata is not lost.
*   **Filter 01:** Purge duplicates, invalid geometries, points, and curves. Keep only Breps and Meshes.
*   **Filter 02 (Detail Culling):** Apply bounding box diagonal and thinness checks (e.g., `< 0.2m`) to remove fins, railings, and hardware. **Crucial Addition (Solidity Check):** Calculate the object's surface area against its bounding box area; if the Solidity Ratio is less than 55% (`< 0.55`), flag it as a hollow frame and discard it to protect solid walls.
*   *Safety Mechanism:* Bypass Filter 02 for any object whose layer or material indicates it is "Glass" or an "Aperture".

### Phase 2: Semantic Tagging (Filter 03)
**Goal:** Categorize surviving geometry into four buckets based on geometric properties.
*   **Floors:** Bounding box Z-dimension is 10-50cm AND the normal vector of its largest face is vertical `(0,0,1)` or `(0,0,-1)`.
*   **Walls:** Bounding box X or Y dimension is 10-30cm AND the normal vectors of its largest faces are horizontal.
*   **Apertures:** Layer contains "Glass/Glazing" OR Material is transparent/glass OR explicitly tagged.
*   **Shading:** Any remaining object that passes Filter 02 but does not meet Floor/Wall/Aperture criteria.

### Phase 3: Shrinkwrap Reconstruction (Filters 04.1 & 04.2)
**Goal:** Rebuild floors and walls to eliminate topological errors and interior intersections.
*   **Floors (4.1):** Extract the top-most horizontal surface of floor-tagged objects to yield a clean 2D plane.
*   **Walls (4.2):** Extract the 2D footprint (bottom outline curves) of all wall-tagged objects. Perform a Boolean Union on these curves to get the outermost continuous region. Extrude this single continuous boundary to the bounding box height to create a clean, watertight exterior shell.

### Phase 4: Aperture & Shading Processing (Filters 04.3 & 04.4)
**Goal:** Ensure apertures are strictly coplanar with the new walls, and shading is computationally light.
*   **Apertures (4.3):** Extract the bounding outline of original windows. Project these curves onto the newly extruded 2.5D walls (from Phase 3) along the window's normal vector. Split the wall surface or create coplanar `PlanarSrf` patches to represent the windows perfectly flush with the wall.
*   **Shading (4.4):** Instead of keeping closed Breps (which slow down Raytracing), extract the largest single 2D face of the shading object and discard the rest of the 3D volume.

### Phase 5: Finalization & Output (Filters 05 & 06)
**Goal:** Structure the rebuilt geometry for Honeybee ingestion.
*   **Filter 05:** Explode the reconstructed shell into individual 2D surfaces (if strictly required) or keep as a closed volume for `HB Zone` automatic parsing.
*   **Filter 06:** Bake the categorized geometry into the official target layers (e.g., `Analysis::Honeybee::Walls`, `Analysis::Honeybee::Apertures`).

## Verification Protocol
After each phase is scripted, we will run it on the live Rhino document (`Layer 01` -> `Target Geometry`) and visually verify the output layer to ensure the logic behaves exactly as expected before proceeding to the next phase.