# Ladybug Mass Extraction Pipeline

This document describes the step-by-step logic and the algorithms implemented in the `LadybugMassExtraction.py` script. The script converts arbitrary, potentially messy or non-manifold architectural models into clean, stepped 2.5D solid geometry optimized for Ladybug environmental simulations.

## Adjustable Parameters

When running the script, you are prompted to adjust four key thresholds that dictate the resolution and simplicity of the output. 

| Parameter | Default | Description |
| :--- | :--- | :--- |
| **SliceInterval** | `1.0` | The vertical distance (in meters) between horizontal cuts. A smaller value captures finer vertical architectural steps but increases processing time. |
| **GridResolution** | `1.0` | The 2D cell size of the voxel footprint. A coarser grid (e.g., `2.0`) simplifies the building mass significantly, while a finer grid (e.g., `0.5`) captures nuanced recesses but relies heavily on Boolean logic. |
| **SmoothingTolerance** | `1.0` | Controls the aggressiveness of the Ramer-Douglas-Peucker (RDP) curve simplification. Higher values yield simpler, boxier footprints with fewer vertices. Lower values cause the footprint to tightly trace the jagged grid edges. |
| **SimilarityThreshold** | `0.80` | A ratio (0.0 to 1.0) dictating how much two adjacent vertical grid slices must overlap to be merged into a single extruded block. `0.80` means an 80% match triggers a merger. Lowering this groups more floors together into flat vertical facades. |

---

## Step-by-Step Logic

### Phase 1: Preparation & Ingestion
1. **Layer Setup:** The script ensures `Analysis::Ladybug_Test_Output` and `Analysis::Ladybug_Test_Contours` exist, wiping any old generated geometry to ensure a clean slate.
2. **Geometry Extraction:** 
   - Reads objects from the `Target Geometry` layer.
   - Traverses nested block instances (`InstanceReferenceGeometry`) to extract raw child geometry.
   - Converts Breps and Extrusions into fast render meshes.
3. **Filtering & Deduplication:** 
   - Identifies objects like glass/windows using layer name checks.
   - Discards microscopic debris (diagonal < `0.4m`) and highly sparse, wire-like bounding boxes to avoid processing noise (unless overridden by the glass check).
   - Generates a "signature" (center coordinate + area) to ignore exact duplicates stacked on top of each other.

### Phase 2: Slicing & Grid Generation
1. **Global Grid Setup:** A bounding box is computed around all valid meshes. A global 2D coordinate grid is established with a padding of 3 cells in every direction to allow for morphological expansion.
2. **Dual-Slicing:** Moving up from `Z_Min` to `Z_Max` by the `SliceInterval`, the script slices the mesh twice per interval (at `bottom + 0.1m` and `top - 0.1m`). This ensures short platform edges or thin floors aren't missed by a slice passing perfectly above or below them.
3. **Grid Mapping:** The resulting intersection curves are overlaid onto the 2D grid. Any grid cell intersected by a curve is marked as "Solid" (`1`).

### Phase 3: Morphological Closing (Eroded Grid Algorithm)
Architectural slices often yield hollow rings (walls) rather than solid floorplates. To convert rings to solid plates:
1. **Dilation:** Solid cells are expanded outward by 1 cell in all 8 directions. This bridges tiny gaps in the wall geometry.
2. **Flood Fill:** A Breadth-First Search (BFS) is deployed from the `[0, 0]` corner (the outer void). Any empty cell reachable from the outside is marked as "Exterior" (`2`).
3. **Erosion:** The script shrinks the solid mass back down by 1 cell, but crucially, any internal cell *not* reached by the exterior Flood Fill remains marked as solid. This perfectly fills the internal holes, creating a solid 2D building footprint.

### Phase 4: Tier Grouping (Similarity Check)
To avoid creating thousands of tiny 1-meter tall extrusions stacked on each other:
1. The script compares the solid cells of the current footprint slice with the previous slice.
2. If the overlap is greater than or equal to the `SimilarityThreshold` (e.g., `80%`), it abandons creating a new tier and simply extends the height (`Z_End`) of the previous tier.
3. The grids are mathematically "Unioned" to ensure the combined tier's footprint envelops all variations across its height.

### Phase 5: Smoothing & 3D Assembly
1. **Footprint Generation:** The grouped solid grid cells are converted into coplanar square curves. `Curve.CreateBooleanUnion` combines these into a continuous jagged outline.
2. **RDP Smoothing:** The Ramer-Douglas-Peucker algorithm simplifies the jagged, stair-stepped grid outline into clean architectural straight lines, guided by the `SmoothingTolerance`.
3. **Baking Contours:** The 2D footprint outlines are baked to the `Ladybug_Test_Contours` layer for visual verification.
4. **Extrusion:** The smoothed 2D footprint is extruded upwards by the tier's total grouped height, creating 3D Brep blocks.
5. **Final Unioning:** The stacked 3D Breps are iteratively boolean-unioned together into a single, cohesive polysurface mass. Coplanar faces are merged to reduce polygon counts before being baked to the `Ladybug_Test_Output` layer.
