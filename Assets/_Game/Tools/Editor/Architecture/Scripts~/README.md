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

## Checking a refactor without the open Editor

Two more scripts here let a refactor be checked in a worktree while the owner's Editor stays open on the main folder:

- `compile_check.py` compiles every first-party assembly of a checkout with `dotnet` (about a minute). It borrows the
  project files Unity wrote in the main folder and swaps in the checkout's own scripts, worked out from its
  `.asmdef` / `.asmref` files, so moved and new scripts count. `--server` compiles the runtime assemblies the way the
  dedicated server does.
- `run_editmode_tests.py` runs the EditMode tests in a batch Unity, below normal priority and through the
  Accelerator, and reads out the results. The first run in a worktree imports the project; later runs are quick.

Every run also checks the map against the compiler: each link it found in code must be allowed by the references the
assemblies declare (a link none allows would not compile, so the map misread a name), and each declared reference
between systems with no type named in code is listed. Those are either unused, and can go, or needed for a type reached
through another assembly's member without its name being written (error CS0012 when removed): find out with
`compile_check.py` after removing them. The page shows them dotted.

The page's views: **Map** (Explore: hover or click a system for its links; Ripple: what a change to a system can reach,
wave by wave, or what it needs; Path: the shortest chain of uses between two systems; layers can be hidden, Ctrl +
wheel zooms), **Matrix** (who uses whom as a grid: a clean stack keeps every square above the diagonal), Problems,
Systems, Hubs, Design data, Tests and How to read.

`dependency_map.py --check` also guards the assets:
- a script that prefabs, scenes or data assets hold must keep its `.meta` GUID and a class named like its file;
- a class that `[SerializeReference]` entries name must keep its namespace and assembly, or carry `[MovedFrom]`.

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
- `baseline.json`: the ratchet, written by `--baseline`: the uphill links, the two-way pairs and the scripts that
  assets hold.
- `compile_check.py`, `run_editmode_tests.py`: see above.
- `kn_check.py`, `organization_check.py`, `known-findings.json`: the gate, below.

## The gate before a commit: kn_check

```bash
python "Assets/_Game/Tools/Editor/Architecture/Scripts~/kn_check.py"
```

`--quick` (the default, under 2 minutes, Unity can stay closed) runs the checks whose files changed against
`origin/main` and names each one that failed (`--all` runs all four whatever changed):
the file-path rules of Validate Project Organization (`organization_check.py`, a Python copy of
`ProjectOrganizationRules`), `dependency_map.py --check` (the report it rewrites is put back), `compile_check.py` and
`compile_check.py --server`. `--only organization,dependencies` runs some of them. Findings that were there before a
check joined the gate are in `known-findings.json` with their reason: they do not fail it, a new one does, and so does
a listed one that has gone (take it off the list). When all four pass, `Logs/kn-check-stamp.json` records the git tree
of the files at that moment, and `--stamp-ok` says whether the files are still the same. Every run also prints the
change's risk level from `risk-tiers.json` (high, medium or low, and what that level needs before the PR goes in, as in
`_Docs/Guides/Review-Rules.md`); `--risk` prints only that.

`kn_commit_hook.py` is a Claude Code `PreToolUse` hook for Bash (switched on in `.claude/settings.json` only with
the Master's OK). On `git commit` or `gh pr create` in a checkout of this project it asks `kn_check.py --state` (about
2 seconds) whether the last passing run still matches the files. It says nothing when it does; otherwise the session
gets one warning line with the reason and the line to run. It refuses the command only for a high-risk change whose
last full run failed on exactly these files. `kn_check` keeps that last run in `Logs/kn-check-last.json`. The plan is
`_Docs/Plans/code-evals-and-checks-plan.md`.
