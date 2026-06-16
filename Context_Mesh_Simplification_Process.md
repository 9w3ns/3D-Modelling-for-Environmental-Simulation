# Context Mesh Simplification for CFD (Vento)

This document outlines the logic and automated execution process for simplifying complex urban context geometries (meshes) for use in Vento CFD simulations.

## 1. The Problem

Urban context models often contain high-density, irregularly spaced buildings. When imported directly into a volumetric mesh generator like Vento, these details cause issues:
*   **Over-calculation:** Small gaps (e.g., alleys < 5m) force the CFD solver to generate extremely fine grid cells in areas that don't significantly impact the macro wind flow.
*   **Glitches:** Jagged edges, non-manifold geometry, or varying heights within tight clusters can cause meshing errors or solver instability.

**The Goal:** Convert a "forest" of detailed, individual building meshes into a reduced set of water-tight, manifold blocks that preserve the general wind-blocking profile (footprint and height) while eliminating internal complexity and narrow gaps.

## 2. Methodology & Understanding

Based on the manual example provided ("Simplified 1" derived from "Context 3"), the simplification process relies on three core principles:

### A. Spatial Aggregation (The "10-Meter Gap" Rule)
Buildings located within a specific threshold (initially 5m, later increased to 10m) of each other are treated as a single aerodynamic obstacle. The air in these narrow gaps is assumed to stagnate or have negligible impact on the broader simulation, so the buildings are merged to prevent unnecessary grid refinement.

### B. Silhouette Tracing (Vertex-Edge Logic)
Instead of replacing a cluster of buildings with a generic bounding box (which alters the wind-facing profile and deflection angles), the simplified mass must follow the outermost vertices and edges of the original cluster. This acts like a "shrink-wrap" or envelope around the buildings.

### C. Vertical Zoning (Height Tiering)
Within a spatial cluster, if the heights of the buildings vary significantly (e.g., a difference > 10m), the mass is split into vertical tiers. This ensures a 30m tower next to a 5m shed isn't merged into a single 30m block, preserving the "skimming flow" over rooftops. *(Note: This was implemented in the code but bypassed in the final 2D curve generation to focus on the footprint logic).*

## 3. Execution & Evolution of the Script

The automation of this process evolved through several iterations in Rhino (via Python/RhinoCommon) to achieve the final result.

### Attempt 1: Bounding Box Union (Failed)
*   **Approach:** Extract the bounding box of every mesh, cluster them by center-to-center distance, and perform a Boolean Union on the bounding rectangles.
*   **Failure:** Bounding boxes are axis-aligned. This destroyed the true orientation of rotated buildings and created artificial, boxy masses that did not match the user's vertex-edge tracing requirement.

### Attempt 2: True Mesh Outlines (Failed to Bridge Gaps)
*   **Approach:** Use `Mesh.GetOutlines(Plane.WorldXY)` to extract the exact 2D footprint of every building. Group them based on vertex-to-vertex proximity (< 10m).
*   **Failure:** While the grouping was correct, `Curve.CreateBooleanUnion` failed to merge them because the physical footprints did not intersect or touch. The script identified 48 buildings belonging to 2 clusters, but output 48 separate curves.

### Attempt 3: Morphological Closing (Success)
To physically bridge the 10m gaps between the true mesh outlines, a "Morphological Closing" technique was applied:
1.  **Extract True Outlines:** Get the exact 2D perimeter of every mesh.
2.  **Offset Outwards (Dilation):** Offset every curve outwards by 5m (half the gap limit). This physically expands the footprints until neighboring buildings intersect.
3.  **Boolean Union:** Union all the expanded, intersecting curves. This creates a single, continuous boundary encompassing the cluster.
4.  **Offset Inwards (Erosion):** Offset the new, unified boundary inwards by 5m. This returns the outer perimeter to the original building edges while keeping the internal gaps bridged.

## 4. Final Result

The Morphological Closing script successfully processed the 48 individual building meshes in "Context 3" and merged them into **exactly 2 unified footprint curves** in the "Simplified 4" layer. 

These curves perfectly match the scale and orientation of the user's manual simplification, providing a clean, flat (World XY), vertex-accurate boundary ready to be extruded into simple, CFD-friendly blocks.