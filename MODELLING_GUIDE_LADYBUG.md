# Modeling Guide: Ladybug (Sun & Radiation)

## 1. Goal
Prepare geometry for direct sunlight hours, incident radiation, and view-based analysis. Ladybug works best with clean, flat, highly-abstracted geometries that eliminate tiny architectural details.

## 2. Geometric Requirements
*   **Direct Inputs:** Native Rhino Breps or Meshes.
*   **Planarity:** Ladybug demands highly simplified, **planar surfaces** for accurate grid projection and fast calculation.
*   **Apertures:** Windows do not need to be "sub-faces"; they can be simple surfaces on separate layers.

## 3. Layer Convention
*   `Target Geometry`: The raw architectural input for simplification.
*   `Analysis::Ladybug_Test_Output`: The simplified 2.5D massing blocks.
*   `Analysis::Ladybug_Test_Contours`: Debugging layer for slice contours.

## 4. Transformation Workflow (The Hybrid Raster-Vector Massing)
To achieve the extreme abstraction required for Ladybug, we use a hybrid **Voxel + RDP (Ramer-Douglas-Peucker)** algorithm that acts as a robust 3D Concave Hull.

### The Algorithm Pipeline:
1.  **High-Resolution Slicing:** The raw model is sliced horizontally at regular intervals (e.g., every `1.0m`).
2.  **Morphological Voxel Closing:** Each slice's disconnected raw curves are mapped to a 2D Voxel Grid. A Dilation + Flood-Fill + Erosion sequence logically bridges gaps and generates a solid watertight footprint.
3.  **Hybrid Vertical Grouping:** Slices are compared vertically. If slice N+1 overlaps slice N by a specified threshold, they are grouped, and their footprints are mathematically **Unioned**. This creates robust Master Footprints that preserve major architectural step-backs.
4.  **RDP Vector Clean-up:** The RDP algorithm traces the jagged voxel boundary and pulls it into perfectly straight vector lines.
5.  **Extrusion:** The cleaned 2D footprint is extruded vertically, resulting in perfectly flat 3D blocks.

### "Goldilocks" Tuning Parameters
These parameters control the balance of abstraction in `LadybugMassExtraction.py`:

| Parameter | Recommended | Function |
| :--- | :--- | :--- |
| `slice_interval` | `1.0m` | Vertical distance between slicing planes. Lower = higher detail but much slower computation. |
| `grid_resolution` | `1.0m` | Voxel pixel size. `1.0m` strongly bridges gaps and simplifies corners. Lowering it hugs diagonals tighter but increases mesh density. |
| `similarity_threshold`| `0.75` | Controls vertical grouping. `0.0` forces the entire building into 1 monolithic block. `0.75` perfectly isolates major geometric step-backs. |
| `smoothing_tolerance` | `1.0m` | The RDP Epsilon. Controls wall straightness. Must be greater than or equal to `grid_resolution`. Set to `1.0m` for clean diagonals; setting it to `0.5m` causes jagged "Minecraft" voxel stairs. |
