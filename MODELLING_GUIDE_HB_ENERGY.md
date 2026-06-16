# Modeling Guide: Honeybee Energy (Thermal Loads)

## 1. Goal
Prepare watertight thermal zones for EnergyPlus annual loads, peak loads, and comfort analysis.

## 2. Geometric Requirements
*   **Watertight Volumes:** Each zone must be a closed, manifold Brep.
*   **Adjacency Matching:** Shared walls between two zones must be perfectly co-planar and matched (intersected) to enable heat transfer calculation.
*   **Low Complexity:** Minimize the number of surfaces. Curved walls should be represented by a few flat segments.

## 3. Layer Convention
*   `Analysis::Energy::Zones::[ZoneName]::Enclosure`
*   `Analysis::Energy::Shading`: Context and local shading.

## 4. Transformation Workflow
1.  **Solve Adjacency:** Run `-EnvSolveAdjacency` to split faces where zones touch. This is the most critical step.
2.  **Simplify Volumes:** Remove small details (moldings, mullions) that do not affect thermal mass.
3.  **Normal Direction:** Ensure all face normals point **outwards** from the zone volume.
4.  **Verification:** Run a "Watertight Check" command to identify gaps larger than 0.01m.
