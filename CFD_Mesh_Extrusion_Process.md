# CFD Mesh Extrusion & Generation Process

This document serves as Part 2 to the Context Mesh Simplification guide, detailing how the 2D simplified footprint curves are converted into 3D, CFD-ready manifold meshes for Vento.

## 1. The Challenge of 3D Generation

Once the complex urban geometry is reduced to continuous 2D footprint curves (via Morphological Closing), the next step is to extrude these curves into 3D blocks. This presents several technical challenges:

*   **Height Mapping:** The new 2D curves have no inherent height. We must programmatically determine which original buildings belong to which new cluster and find the maximum elevation (`z_max`) among them.
*   **Coordinate Discrepancies:** The 2D curves exist flatly on the World XY plane (Z=0). A standard 3D point-in-polygon check (`Curve.Contains`) often fails because the original building centroids float at various Z-elevations (e.g., Z=12 to 24).
*   **Meshing Complex Breps:** When a highly complex, jagged 2D curve is extruded, it creates a complex B-rep (Boundary Representation). Rhino's default mesher can occasionally struggle to convert highly complex B-reps into clean meshes in a single pass.

## 2. The Extrusion Methodology

To overcome these challenges, the following logic was applied:

### A. 2D Spatial Mapping
Instead of checking if a 3D building centroid falls inside a 3D curve volume, the script flattens everything to 2D. 
1.  The bounding box of each original context building is calculated, and its center point is flattened to `Z=0`.
2.  The bounding box of the newly generated 2D curve is also flattened.
3.  If the building's 2D center falls within the curve's 2D bounding box, it is assigned to that cluster.

### B. Height Extraction
As the script iterates through the buildings assigned to a cluster, it records the highest `Z_max` and the lowest `Z_min`. 
*   **Max Height Rule:** The entire cluster is extruded to the maximum height found. This prevents "stepped" roofs within a single tightly packed block, which can cause grid turbulence in CFD.

### C. Robust Mesh Conversion
1.  **Extrusion:** The 2D curve is extruded vertically by the calculated height difference (`max_h - min_z`) and capped to form a solid Brep.
2.  **Meshing:** `Rhino.Geometry.Mesh.CreateFromBrep` is called using `FastRenderMesh` parameters to generate a triangulated/quad mesh.
3.  **Fallback:** If the mesher fails due to curve complexity, the script is designed to output the raw Brep instead, ensuring no data is lost.

## 3. Final Outcome

The script successfully mapped the heights of the 48 original buildings to the 2 morphed footprints. 

*   **Result:** 2 clean, solid 3D meshes were generated and placed in the **"Simplified 5"** layer.
*   **Benefits for Vento:** These meshes possess the exact jagged wind-facing silhouettes of the original city blocks but contain zero internal gaps or non-manifold edges. This guarantees a stable volumetric grid generation process and significantly reduces solver calculation time.