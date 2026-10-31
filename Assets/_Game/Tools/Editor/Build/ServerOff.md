# What may be off on a dedicated server, and why

The server has no screen, no sound and no shaders. A few things are therefore switched off **on the server only**. This is
the complete list. Anything else that is off in a map is a mistake, and the publish gate stops the publish and says which.

| What | Where it is switched off | Why |
| --- | --- | --- |
| Components in the `VolumetricLights` and `VolumetricFogAndMist2` namespaces | The server build's own copy of each scene (`ServerSceneStripper`), and at load in a running server (`DedicatedServerOptimizer`) | A server ships no shaders. The lamps' light cones would try to make a material every frame and throw. |
| UI Toolkit documents (`UIDocument`) | The server build's own copy of each scene (`ServerSceneStripper`), and at load in a running server (`DedicatedServerOptimizer`) | The UI assembly is not in a server build; laying out text looks for system fonts, which needs a shader. |
| vSync, target frame rate, audio listener | A running server only (`DedicatedServerOptimizer.ApplyHeadlessSettings`) | Headless: 30 Hz tick, nothing to draw or hear. |
| Audio and visuals-only components (`AmbienceEmitter`, music players, `BeatRing`, ...) | At run time in a batch-mode run, never saved | Nothing plays. They switch themselves off when they wake; they are not `ExecuteAlways`, so a build never wakes them. |
| The sky presenter (`EnvironmentPresenter`) | Only in a **running** headless server (`Application.isPlaying`) | It is **not** switched off when a build opens a map in edit mode. That was the 25 Oct 2026 bug (build 12): a presenter saved switched off is a black, frozen sky on every client. |

## Rules

- Switch-offs happen on the server's copy or at run time. Never in a scene file, a prefab, or a game's map bundle.
- A game build is always a Player build (`ClientBuildGuard`): `UNITY_SERVER` code never compiles into the game.
- The code list is `ServerOffList.RenderOnlyNamespaces`. A new server-only switch-off is added here, in the table above,
  and in `ServerOffListTests`, in one change.
- The publish gate (`BundleAuditGate`) reads the list: in a game's bundle **nothing** may be off that is on in its scene,
  so even the entries above must be on.
