# Dependency Map: how it is made

The **Dependency Map** shows how the game's scripts depend on each other. It is an artifact page:

**https://claude.ai/artifact/Uvq5NyTWtTvYWNVoo45gdm** (private to the owner)

The written report beside it is `Assets/_Game/_Docs/Reference/Dependency-Map.md`. The plan that works from it is
`Assets/_Game/_Docs/Plans/architecture-decoupling-plan.md`. The `~` at the end of this folder's name makes Unity
ignore it.

## Run it

From the project folder (a few seconds; Unity can stay closed):

```bash
python "Assets/_Game/Tools/Editor/Architecture/Scripts~/dependency_map.py"
```

Each run:
- writes `out/dependency-map.json` and `out/dependency-map.html` beside this file (`out/` is not committed);
- rewrites the tables of the report between its `<!-- map:... -->` markers;
- prints the headline counts.

Then publish `out/dependency-map.html` to the link above with the Artifact tool. From another conversation, pass the
link as `url`, so the page keeps its address.

- `--baseline` also saves today's uphill links and two-way system pairs to `baseline.json` (committed).
- `--check` compares with the baseline and exits with 1 when a new one has appeared. Run it after any change that
  crosses systems.

## What it reads

- **Assemblies:** every `.asmdef` / `.asmref` under `Assets/_Game`.
- **Code:** every runtime and test `.cs` file under `Assets/_Game`, with comments and strings stripped. Editor code
  and the `~` programs (save service, server app, launcher) are left out. Names are resolved against the declared
  classes through each file's namespace and `using` lines.
- **Assets:** every text prefab, scene and `.asset` under `Assets/_Game` and `Assets/Resources`, plus
  `DefaultNetworkPrefabs.asset`. From each it takes:
  - the `m_Script` GUIDs: which script it holds;
  - the other GUIDs: which data assets it points at;
  - its `[SerializeReference]` entries: classes stored by name and assembly.

## The files

- `dependency_map.py`: reads the project and builds everything. Python's standard library only.
- `template.html`: the page, in the Encyclopedia's comic look. The data goes in at `/*__DATA__*/null`.
- `baseline.json`: the ratchet, written by `--baseline`.
