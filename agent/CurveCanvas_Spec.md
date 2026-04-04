# Technical Specification: CurveCanvas (2.5D Level Editor)

**Project Type:** Standalone Godot 4.x Addon / pure C# Data Module
**Target Perspective:** 2.5D (3D World Graphics, Gameplay locked strictly to Z=0 Plane)
**Dependencies:** None (Outputs generic JSON data consumable by any physics controller)

## 1. Project Overview
CurveCanvas is a standalone level authoring tool designed for momentum-based 2.5D games. It utilizes a "draw-to-ride" interface optimized for touch and mouse. The editor outputs lightweight, engine-agnostic JSON files representing physical curves and object coordinates, completely decoupled from any specific character controller or biome.

## 2. Repository Architecture
The tool is fully contained as a drop-in Godot Plugin.
* **`src/AuthoringCore/`**: Pure C# data models (Spline math extensions, Scenery/Action coordinates, Layer definitions, JSON serialization).
* **`addons/curve_canvas/`**: Godot implementation (`Curve3D`, Procedural `ArrayMesh` generation, Editor UI, Touch/Mouse Raycasting, Sandbox State Machine).

## 3. Core Modules

### Module A: The 2.5D Flow Generator (Spline & Procedural Mesh)
Handles the macro-terrain generation without relying on slow CSG nodes.
* **Z=0 Planar Projection:** Touch/mouse inputs do not raycast against complex 3D terrain. The `Camera3D` casts a ray that intersects a mathematical infinite plane at `Z = 0`.
* **Native Catmull-Rom Smoothing:** Raw projected `Vector3` points are fed directly into Godot's native `Curve3D` using Catmull-Rom interpolation to generate a continuous, mathematically flawless slope.
* **Procedural `SurfaceTool` Mesh (No CSG):** The plugin uses a C# script wrapping Godot's `SurfaceTool` to dynamically generate an `ArrayMesh` along the `Curve3D`. This provides massive performance gains during live-editing, allows for custom UV mapping, and generates an optimized static `ConcavePolygonShape3D` for physics.

### Module B: The Action Snapper (Gameplay Objects)
Handles the placement of interactive gameplay objects (kickers, rails, bumpers).
* **Action Plane Constraint:** Interactive objects can *only* exist on the Action Plane (`Z = 0`). 
* **Tangent Snapping:** When an object is dragged, it snaps to the nearest `Curve3D` point. The editor reads the curve's tangent and automatically rotates the object so ramps and rails perfectly match the downhill slope.

### Module C: Smart Workflow Tools
Automates repetitive tasks to maintain the designer's flow state.
* **Context-Aware Surfacing (Friction Modifiers):** The editor evaluates spline tangents in real-time. If a slope segment exceeds a steepness threshold or loops, the `SurfaceTool` automatically applies a specific physical/visual material (e.g., "High Grip" or "Ice") to that geometry segment.
* **Scenery Depth Planes (Parallax Layers):** Non-gameplay props (trees, neon pillars, HD-2D Billboard Sprites) are mathematically prevented from being placed on the Action Plane. They automatically snap to predefined Background/Foreground planes (e.g., `Z = -10` or `Z = +5`) and read the `Curve3D` to match the track's Y-elevation.
* **Procedural Prop Brushes:** Users can drag a "Prop Brush" across the screen. The editor uses a noise function to scatter entities along the Scenery Planes, following the curve of the track automatically.
* **Camera Action Triggers:** Designers can place invisible trigger boxes on the Action Plane and visually link them to a "Dynamic Camera Point" in 3D space to frame massive jumps.

### Module D: Serialization & Export
Ensures tracks are highly portable and usable by any game engine or controller.
* **Data Model:** The editor serializes a tiny JSON payload containing:
  1. Array of `Vector2` points (X, Y) for the main spline (Z is assumed 0).
  2. Array of `ActionObject` structs (ID, offset distance along the spline).
  3. Array of `SceneryObject` structs (ID, X/Y position, Z-Plane index).
  4. Array of `EventTriggers` (Camera pans, friction modifiers).
* **Reconstruction Engine:** A standalone loader script that parses the JSON and rebuilds the `Path3D`, the custom `ArrayMesh`, and object instances at runtime in under a second.
* **Level Storage Convention:** All exported, downloaded, or curated levels must live under `user://Levels/`. This keeps authored content inside the writable app-data sandbox (portable across platforms) and gives the runtime/browser a single canonical directory to scan (future subfolders such as `user://Levels/Downloaded` are allowed, but `user://Levels/` is the root).

### Module E: The Sandbox State Machine (Seamless Play/Edit)
The editor operates within a single, unified sandbox scene to eliminate loading times between authoring and testing.
* **State 1: Architect Mode**
  * **Camera:** Free-roaming 3D Editor Camera.
  * **Simulation:** The host KCC node is paused (`ProcessMode = Node.ProcessModeEnum.Disabled`) and visually hidden. Physics ticking is suspended.
  * **Interaction:** Spline drawing, object placement, and property tweaking are active.
* **State 2: Action Mode (Live Test)**
  * **Trigger:** User presses the "Play" toggle.
  * **Initialization:** 1. The editor finds the nearest `Curve3D` point to the user's cursor ("Test From Here").
	2. The KCC is teleported to that coordinate, given a baseline velocity, and unpaused.
	3. The Action Camera (`Camera3D` attached to the KCC) becomes the active viewport.
  * **Interaction:** UI tools vanish; standard game inputs are routed directly to the KCC.
* **State 3: The Revert Hook & Trajectory Ghost**
  * **Execution:** The Action Camera deactivates, returning the viewport precisely to where the Architect Camera was left. The KCC is immediately hidden.
  * **The Ghost Trail:** During Action Mode, the KCC records its coordinates. Upon reverting, these are plotted into a visual `Line3D` or Ribbon Trail, allowing the designer to see the exact arc of their previous jump to fine-tune landing ramps.

### Module F: Transform & UV Mapping Mechanics
Strictly defines how 2D textures and 3D transforms map to the procedural curve.
* **Procedural UV Mapping:**
  * The `U` coordinate spans the fixed width of the track cross-section.
  * The `V` coordinate must be calculated as `Curve3D.GetBakedLength()` at the current vertex, divided by a configurable `TextureScale` constant. This ensures materials tile seamlessly and never stretch.
* **Spatial Projection:**
  * The editor uses `Curve3D.GetClosestPoint()` or interpolates X-coordinates against the baked curve array to find the track's absolute `Y` elevation at any given lateral position.
  * **Scenery Planes:** Objects on Z-offset planes set their position to `Vector3(Cursor.X, TrackElevation.Y, DepthPlane.Z)`. Billboard modes on `Sprite3D` nodes are preserved.
  * **Action Plane:** Trick objects (Z=0) read the curve tangent at their position and apply a Basis rotation aligned with the downward slope.

### Module F: Asset Registry (The Palette)
Centralized palette describing every placeable asset.
* **CurveCanvasRegistry Resource:** Holds authoritative mappings for editor tooling.
* **Prefab Dictionary:** Maps `ObjectID (string)` → `PackedScene` for instantiating gameplay/prop prefabs (ramps, trees, etc.).
* **Thumbnail Dictionary:** Maps `ObjectID (string)` → `Texture2D` thumbnails used to render buttons/previews in the UI.
* **Authoring Integration:** `ActionObjectSnapper` (and any placement tool) must query this registry to spawn the correct object whenever a designer selects an ID.

### Module G: Advanced Spline Manipulation (CRUD UX)
Viewport UX so designers can iterate on curves non-destructively.
* **Select:** Perform screen-space raycasts from the Architect Camera. Detect curve control points under the cursor, highlight the current selection.
* **Move:** Provide gizmo handles (constrained to Z=0) so dragging updates the point position while maintaining planar rules.
* **Insert:** Allow clicking on the track mesh between two points to subdivide the curve and add a new control point in-place.
* **Delete:** `Backspace/Delete` removes the selected point, then immediately re-bakes the curve and rebuilds the mesh.

### Module H: Undo/Redo Integration
Protect designer flow with native undo support.
* Route every state change (add/move/delete points, placing/removing Action Objects, etc.) through `EditorUndoRedoManager`.
* Ensure each action calls `CreateAction()`, `AddDoMethod()`, `AddUndoMethod()`, and `CommitAction()` so Ctrl+Z/Cmd+Z works for all authoring operations.

### Module I: Level Meta-Data & Boundaries
Gameplay-critical metadata for a usable level.
* **Spawn Point:** Special ActionObject ID that defines the HostCharacter's starting `Transform3D` when entering Action Mode.
* **Goal Line:** Special ActionObject ID marking completion; emits "Level Complete" when the HostCharacter crosses it.
* **Kill Z-Plane:** Global float (e.g., `Y = -100`). If the HostCharacter drops below, the State Manager instantly resets them to the Spawn Point to avoid infinite falls.

### Module J: Live Runtime Editing (Co-Op/Active Mode)
**Concept:** The ability to manipulate splines and place Action Objects while the `HostCharacter` simulation is actively ticking.
**Requirements:**
* **Input Multiplexing:** The editor cursor and raycasting logic must be decoupled from the `ArchitectCamera` so they can function seamlessly through the `ActionCamera` during gameplay.
* **Async Collision Generation:** When a curve point is modified during `EditorState.Action`, the `TrackMeshGenerator` must rebuild its `ConcavePolygonShape3D` physics collider on a background thread to prevent gameplay frame drops.
* **Runtime Instantiation:** The `ActionObjectSnapper` must be capable of querying the `CurveCanvasRegistry` and spawning prefabs dynamically into the active SceneTree without requiring an editor-only `[Tool]` context.
