# Modeling Guide: Honeybee Radiance (Daylight & Glare)

## 1. Goal
Prepare `HB Rooms` and `HB Surfaces` for spatial daylight autonomy (sDA), annual sunlight exposure (ASE), and glare metrics.

## 2. Geometric Requirements
*   **Planar Surfaces:** All surfaces MUST be planar. Radiance calculation performance degrades with non-planar surfaces.
*   **Room-Based Logic:** Geometry should be grouped into Rooms to allow for accurate sensor grid generation.
*   **Apertures:** Windows must be hosted by a parent wall surface.

## 3. Layer Convention
*   `Analysis::Honeybee::Zones::[RoomName]::Walls`
*   `Analysis::Honeybee::Zones::[RoomName]::Apertures`
*   `Analysis::Honeybee::Context`: External obstructions.

## 4. Transformation Workflow
1.  **Extract Floors:** Use the Floor surfaces to generate sensor grids (offset ~0.75m).
2.  **Planarize:** Ensure all walls and apertures are perfectly flat.
3.  **Parent-Child Linking:** Use the `-EnvLinkApertures` command to associate window surfaces with their respective walls.
4.  **Verification:** Check that sensor grids do not bleed through wall boundaries.
