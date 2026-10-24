---
name: fast-reload
description: Use EVERY time code is changed in Krytical Nova II (any .cs edit, while or before testing). Decides whether the change can go into the running Play session with Fast Script Reload (hot reload, no domain reload) or must wait for a normal compile, checks Play mode first, and never refreshes Unity while the owner is playing.
---

# Fast Script Reload: code changes in and out of Play

The owner (20 Oct 2026): "if we can update things in play mode we should, to help speed up development; make a skill
that understands Fast Script Reload and use it any time you make code." Earlier the same day I refreshed Unity while
the owner was in Play, which threw them out of Play to recompile. This skill exists so that never happens again.

Fast Script Reload (FSR, `Assets/FastScriptReload/`, its manual in `Documentation/documentation.md`) compiles a changed
.cs file on its own and swaps the new method bodies into the running game (Harmony detours). Nothing reloads, nothing
restarts: the scene, the network session and the character stay as they are.

## Step 1, always: is the Editor playing?

```bash
python ".Codex/skills/fast-reload/scripts/hot_reload.py" --state
```

- `EDIT`: not playing. Edit, then compile the normal way (`AssetDatabase.Refresh()` from an eval, wait for the
  compile, read the console). Hot reload does not apply.
- `PLAYING`: the owner is testing. **Never call `AssetDatabase.Refresh()`, never request a compile, never stop Play.**
  Go to step 2.
- `BUSY` (compiling) or no answer: wait and ask again; do nothing else to the Editor meanwhile.

Asset auto refresh is "Enabled Outside Playmode" (`kAutoRefreshMode` 2): a file written while playing is not compiled
by Unity until Play stops, so writing the file is safe; only an explicit refresh hurts.

## Step 2 (playing): can this change be hot-reloaded?

**Yes, hot-reload it** when every changed file only changes things like these:
- the body of an existing method, property getter/setter, constructor or lambda inside it (numbers, conditions,
  calls, text, layout values, logging, a fixed bug);
- a new **private** method that only the changed code calls;
- a new **private, non-serialized** field used only inside the changed class (FSR's experimental added-field support is
  on here, its default; it starts at its default value, not at what a running object would have had, and outside
  classes cannot reach it);
- a `MonoBehaviour`, plain class or static class that is **not generic** (a non-generic class deriving from a generic
  base is fine).

**No, it waits for Play to stop** when any of these is true:
- a new **serialized** or public field, a field other classes use, a removed field, a changed field type, a new
  auto-property, or a new **type** (class, struct, enum, interface, nested type);
- a new **public / internal / protected** method, or a changed method signature others call;
- **generic** methods or classes (FSR skips them);
- the changed code reaches an **`internal`** member of its own assembly, or a **`private protected`** one, or a class
  implementing an **internal interface**;
- many **nested** classes or structs in the file (likely compile errors; e.g. `MenuDrawer.Item`); a **partial** class
  only with care (FSR's partial-class support is on but experimental: try it, and if it fails it waits);
- a new **assembly reference** or `.asmdef` change, a package change, a new `using` of an assembly not referenced yet;
- **Netcode**: anything with `[Rpc]`, `[ServerRpc]`, `[ClientRpc]`, `NetworkVariable` or `INetworkSerializable` in the
  changed code (Netcode's code generation does not run on the reloaded copy, so RPCs would silently stop working);
- **attributes or Unity entry points that already ran**: `[RuntimeInitializeOnLoadMethod]`, `[InitializeOnLoad]`,
  static constructors, `Awake`/`Start`/`OnEnable` of objects already alive, a coroutine already running (the old code
  keeps running; the new code only applies to the next call). These may be hot-reloaded only if the owner will re-enter
  the screen or call path, and I say so;
- **Editor code** (`Editor/` folders, `[MenuItem]`, builders, inspectors): not part of the Play session; normal compile;
- **data, scenes, prefabs, ScriptableObjects, shaders, UXML/USS**: not code; FSR does nothing for them.

When unsure, it waits: a wrong hot reload is worse than a short wait.

## Step 3a (playing, hot-reloadable): push it in

1. Edit the file(s) (CRLF line endings kept).
2. Queue them with FSR and wait for its verdict:
   ```bash
   python ".Codex/skills/fast-reload/scripts/hot_reload.py" "Assets/_Game/<System>/Runtime/<File>.cs"
   ```
   - `HOT-RELOADED`: live now. Tell the owner what changed and what to try, in the running session.
   - `FSR FAILED` (exit 1): FSR could not compile it (one of the limits above). The file is still correct on disk and
     compiles normally when Play stops; tell the owner it applies after Play.
   - `NO VERDICT` (exit 3): read the console; say so plainly.
3. The normal compile still happens by itself when the owner stops Play; tests and commits wait for it.

## Step 3b (playing, not hot-reloadable)

Write the change to disk (safe: no refresh), tell the owner it needs Play to stop, and wait for them. Never stop Play
myself and never refresh.

## Known in this project

- Settings as found on 20 Oct 2026 (Editor preferences, all FSR defaults): auto hot-reload on, batched every 1 s;
  added-field and partial-class support on (experimental); editor-mode hot reload off; file watcher UnityDefault.
- FSR's own file watcher never picks up edits here; the script queues files by hand
  (`FastScriptReloadManager.Instance.AddFileChangeToProcess`, then `TriggerReloadForChangedFiles`). **Never set the
  file watcher to CustomPolling** (it spawned ~980 threads and froze the Editor once).
- FSR stays at `Assets/FastScriptReload` (Unity refuses to move it); two vendor lines are patched for Unity 6000.6
  (marked `// KN:`).
- Each hot reload adds a small assembly to memory; after very many in one session, stop Play once to clear them.
- Runtime evals in Play are still out (no domain reload: leftovers persist); this script only queues files with FSR.
- Unity's Editor returns a stand-in object for a missing component: never `GetComponent<T>() ?? AddComponent<T>()`;
  use `TryGetComponent` (cost the owner a broken footer on 20 Oct 2026).
