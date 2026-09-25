# Unity Roguelike UI and Performance Fix

Replace your existing file:

Assets/RoguelikePrototype/Scripts/ProceduralDungeonPrototype.cs

with the patched version in this package.

Changes:
- UI font size increased from 16 to 28.
- CanvasScaler configured for different resolutions.
- Text area enlarged.
- Text shadow added for visibility.
- Debug UI refresh throttled to 0.15 seconds instead of every frame.
- Removed repeated BFS reachability check from UI refresh.

Controls remain:
- WASD / Arrow Keys = Move
- R = Regenerate dungeon
- T = Run 100 dungeon evaluation tests


## Low-lag patch notes

This version reduces delayed movement by:

- disabling VSync override in code and targeting 120 FPS;
- setting Unity physics timestep to 60 Hz;
- applying player Rigidbody2D velocity immediately in Update and FixedUpdate;
- enabling Rigidbody2D interpolation and NeverSleep;
- snapping the camera to the player by default;
- reducing debug UI refresh frequency;
- disabling per-row CSV console spam by default.

If the game still feels laggy inside the Unity Editor, test a Windows/Mac build because the Editor can be slower than a standalone build.
