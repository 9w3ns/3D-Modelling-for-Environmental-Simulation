# Modeling Guide: Ladybug (Sun & Radiation)

## 1. Goal
Prepare geometry for direct sunlight hours, incident radiation, and view-based analysis.

## 2. Geometric Requirements
*   **Direct Inputs:** Native Rhino Breps or Meshes.
*   **Planarity:** While Ladybug handles complex surfaces, **planar surfaces** are preferred for faster calculation and cleaner visualization.
*   **Apertures:** Windows do not need to be "sub-faces"; they can be simple surfaces on separate layers.

## 3. Layer Convention
*   `Analysis::Ladybug::Geometry`: Primary surfaces for analysis (e.g., building facade).
*   `Analysis::Ladybug::Context`: Shading objects (trees, neighboring buildings).

## 4. Transformation Workflow
1.  **Simplify:** Convert curved NURBS building skins into planar segments using `-EnvPrepPlanarize`.
2.  **Clean:** Remove internal partitions that do not contribute to external shading or radiation.
3.  **Verify:** Ensure no surfaces are perfectly coincident (overlapping), as this causes "fighting" in radiation maps.
