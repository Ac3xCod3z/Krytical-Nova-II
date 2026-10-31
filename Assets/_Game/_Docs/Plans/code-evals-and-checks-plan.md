# Code evals and checks plan

**Status: agreed 30 Oct 2026 (the owner's picks are at the end). One PR per step; step 1 is built (`kn_check --all`
in about 1 minute after `compile_check` moved to a single dotnet run); step 2 is next.**

The aim: a change is proved to work before anyone calls it done, so a small slip is caught in the same session that
made it instead of turning up later in a build, on the test server or in a player's save. The idea comes from a video
about how the Claude Code team checks agent-written code (AI LABS, "Anthropic Just Released A New System For Using
Claude Code", https://www.youtube.com/watch?v=S49gppZl8H0). Its method is summarised first, then each idea is mapped
to a check for this project. Only what the project does not already have is added.

## The video's method, in short

1. **Prove it, don't say it.** An agent that says "done" has often missed part of the ask. Before a change goes
   anywhere it must run something that shows it works (tests, a screenshot against the design) and show the result.
2. **Evals before building.** Each feature's requirements are turned into a fixed list of pass/fail checks ("evals")
   that are strict enough for bad work to fail them. The owner reviews the evals first; nothing is built until they
   are approved.
3. **A gate on "done".** A hook stops the agent from marking the task finished until its evals pass.
4. **A second pair of eyes.** The agent that built a change agrees with itself, so a separate reviewer with a fresh
   context reads every change. Its rules (what counts as a real failure, how bad each kind is, what to ignore) are
   written in a review file that is linked from CLAUDE.md so every session loads it.
5. **Build, verify, review, in rounds, with limits.** A builder, a verifier (runs the evals) and a reviewer (looks for
   what the evals missed) loop until everything passes. Without limits they loop for ever and mostly re-find old
   problems, so: after a few rounds the builder must rethink the approach instead of patching; after round one the
   reviewer looks only at what the last fix changed; and every finding names its kind, so the builder fixes that kind
   everywhere, not just in one spot.
6. **Learn from repeats.** When a review misses a bug that turns up later, or keeps flagging something harmless, a
   one-line rule goes into a "learned rules" section of the review file (only with the owner's yes).
7. **Check harder where it hurts more.** Each change is sorted by one question: what happens to a player if it
   breaks? Looks wrong = low; a feature stops working but nothing is lost = medium; someone loses money or items,
   gets locked out, or sees another player's data = high. High-risk work gets stricter evals and a deeper review.
8. **A fresh first-time user.** An agent that never saw the code uses the product like a new player, on a throwaway
   database, and reports where people would get stuck.
9. **Fast undo.** A branch per feature and a saved working version at each step, so a bad change can be found
   (replay the bug, check the guess) and rolled back in minutes.

## What Krytical Nova II already has

| Check | What it catches | How it runs today |
| --- | --- | --- |
| `compile_check.py` | any assembly that does not compile (client) | dotnet, about a minute, Unity can stay closed |
| `compile_check.py --server` | code the dedicated server cannot compile | same |
| `dependency_map.py --check` | uphill links, two-way system pairs, scripts assets hold losing their GUID or class, `[SerializeReference]` classes moved without `[MovedFrom]` | Python, seconds |
| `run_editmode_tests.py` | about 1,500 EditMode tests (24 test assemblies) | batch Unity in a worktree, minutes after the first import |
| Validate Project Organization | files outside `_Game/<System>`, type buckets, missing asmdefs | Editor menu only |
| Check Icons | skills without an own icon, icons used twice | Editor menu only |
| Check Saves Against This Build | a save version the build cannot read | Editor menu, before a release, on a copy of the real saves |
| `/code-review` | bugs in a diff, by a separate agent | by hand, no project rules |
| Git branches, PRs, worktrees | fast undo | every change already lands as a PR |

What is missing, measured against the video:

- **No single gate.** The checks are run one by one from memory; a session can skip one and still say "done".
- **The test run is never green.** The last full EditMode run reported (DOTS clone, 30 Oct) had 5 known content
  failures, so a red result is normal and a sixth, new failure looks the same as the old five.
- **Two validators only run in an open Editor** (Validate Project Organization, Check Icons), so worktree sessions and
  scripts cannot run them.
- **Nothing stops "done" or a commit before the checks pass.** The only hooks are graphify's and Git LFS's.
- **No project review rules.** `/code-review` does not know this game's real failures (server authority, unregistered
  network prefabs, saves that change meaning, Creds side doors).
- **No risk sorting.** A change to a tooltip and a change to `ServerSave.SaveTogether` get the same checks.
- **No PlayMode tests and no continuous integration** (no `.github/workflows`). Neither is proposed now: PlayMode needs
  a Unity slot per run on this PC, and a cloud runner would need a Unity licence and would pull LFS files from the paid
  budget.

## The plan: each idea, one check

Each row says what it checks, when it runs and how it fails. Letters match the questions at the end.

**Checking as we go, not gates (owner, 30 Oct 2026).** The checks must never grind work to a halt. `--quick` stays
under 2 minutes and skips every check whose files did not change; the commit hook only warns, with the failing check
and a one-line fix hint, and never blocks (at most it blocks a high-risk change whose `--quick` failed); a low-risk
change gets no new mandatory step. The owner does not review every PR: the Master reviews each one before it goes in
and the owner sees only "reviewed, safe to merge" or the problems.

### A. One gate command (ideas 1 and 3)

`Assets/_Game/Tools/Editor/Architecture/Scripts~/kn_check.py`, beside the scripts it calls. Python's standard library
only, like them.

- **What it checks.** `--quick`: `dependency_map.py --check`, `compile_check.py`, `compile_check.py --server`, and the
  organization rules (B). `--full`: the quick checks, then `run_editmode_tests.py` (only the test assemblies of the
  systems the change touches, or all of them with `--all`), then Check Icons (B).
- **When it runs.** `--quick` before a commit (under 2 minutes, no Unity). It looks at the files changed against
  `origin/main` and skips what they cannot affect: the organization rules always run (under a second), the dependency
  check only when scripts, assemblies, prefabs, scenes, assets or `.meta` files changed, the compile only when code
  changed, the server compile only when runtime code (not `Editor/` or `Tests/`) changed. A docs-only change runs in
  about a second. `--all` runs everything. `--full` before a PR on medium- and high-risk changes, in a Unity slot the
  Master hands out.
- **How it fails.** Exits 1 and prints one line per failed check with the command to re-run it alone. On success it
  writes `Logs/kn-check-stamp.json` (the time, the checks run and skipped, and the git tree of the files) for C.

**Known failures list.** `Scripts~/known-test-failures.json` names the content tests that fail today on main (recorded in that PR), each with the
reason and the date. `kn_check` passes when exactly those fail and fails on any other failure, and also when a listed
test starts passing (so the list shrinks instead of going stale). The list is a ratchet like `baseline.json`.

### B. The Editor-only validators run from a script (idea 1)

Validate Project Organization and Check Icons each get a static `RunBatch()` method that a batch Unity can call with
`-executeMethod`, writes its findings to `Logs/`, and exits 1 on a finding. The menus stay as they are. The organization
rules that are pure file-path rules are also checked in Python inside `kn_check --quick`, so the quick tier needs no
Unity. Check Saves Against This Build stays a release step (it needs a copy of the real saves); `kn_check` only reminds
of it when a file implementing `ISaveParticipant` or `IMigratingSave` changed.

### C. A gate on commits (idea 3)

A Claude Code `PreToolUse` hook in `.claude/settings.json` (next to graphify's) that looks at Bash commands. When the
command is `git commit` or `gh pr create`, it runs `kn_check.py --stamp-ok` (a hash of the files, about 2 seconds, not a
re-run of the checks). If the stamp is missing or the files changed since, the command still goes ahead, and the hook
adds a warning naming the failed or missing check and a one-line fix hint ("run kn_check.py --quick"). It blocks
only when the change is high risk (D) and its last `--quick` failed. The owner's own commits from a terminal are not
touched (it is a Claude Code hook, not a git hook). It is switched on last, after the Master is told.

### D. Risk tiers decide how hard to check (idea 7)

`kn_check` sorts the changed files by path with a table in `Scripts~/risk-tiers.json`:

| Tier | What breaking it does to a player | Paths (first draft) | Needed before a PR |
| --- | --- | --- | --- |
| High | loses items, Creds or a save, is locked out, or another player's data leaks | `Gameplay/Persistence`, `Gameplay/Economy`, `Gameplay/Wallet`, `Gameplay/Social` (trade, Crews), the save service, accounts and GM roles, `Networking` authority code | `--full` with all tests, the Master's `/code-review high`, a second-client test |
| Medium | a feature stops working, nothing is lost | other `Gameplay/*`, `World`, `Networking`, `Rendering` | `--full` for the touched systems, the Master's `/code-review medium` |
| Low | something looks wrong | `UI` look, `GameFeel`, `Audio`, `Icons`, docs, art | `--quick` only, nothing new; the Master's `/code-review low` |

The highest tier any file falls in is the change's tier; `kn_check` prints it and the PR description states it.

### E. Project review rules for `/code-review` (ideas 4 and 6)

`Assets/_Game/_Docs/Guides/Review-Rules.md` (a doc under `_Docs`, so it joins the Library), linked from CLAUDE.md so
every session and `/code-review` read it. It holds:

- **Real failures, with severity.** High: a client deciding what the server should decide; an economy change saved
  with separate saves instead of `ServerSave.SaveTogether`; Creds moved outside `PlayerWallet`; an item movement with
  no `EconomyLedger` line; a save meaning change with no `IMigratingSave` step; a load that resets a save it cannot
  read; anything that points at the real database or ports 7777/7778/7801/8090. Medium: a server-spawned prefab not in
  `GameplayPrefabsSetup.SpawnedPrefabs`; a player system not in `PlayerSystemsSetup`; an uphill or two-way link; a
  tuning number in code instead of a settings asset; a moved script without its `.meta`; a renamed serialized field
  without `[FormerlySerializedAs]`. Low: per-frame allocations in hot paths, player-facing text that is not the
  Krytical Nova term, a gamepad glyph in a prompt.
- **What to ignore:** vendor folders, generated files, formatting.
- **How to report:** one line per finding with its kind, file:line and the tier from D.
- **Who reviews:** the Master, on every PR before it goes in, with `/code-review` at the PR's risk level against this
  file. The owner gets one short line: "reviewed, safe to merge", or the problems found.
- **Learned rules** (empty to start): a one-line rule is added when a review missed a bug that came back, or kept
  flagging something harmless, and only on the owner's yes.

### F. Evals written before building a system (idea 2)

The plans are already live checklists. A plan for a new system gains an **Evals** section before any code: each line
is one pass/fail check and names the test, the `kn_check` tier or the manual step that proves it ("two clients see the
same Crew roster after a server restart: `CrewRosterTests` + second-client test"). The owner approves the evals with
the plan's other picks. This is a rule in CLAUDE.md and a heading in the plan template, not a new tool.

### G. Round limits for review loops (idea 5)

Written into Review-Rules.md, for any session that runs a build/check/review loop with subagents: at most three
rounds, then stop and rethink the approach (or ask the Master) instead of patching; after round one review only what the
last fix changed and do not repeat earlier findings; every finding names its kind and the fix covers that kind
everywhere. No new skill.

### Later, not proposed now

- **A first-time-player agent (idea 8).** An agent with no code access playing the measure server on the test
  database. Worth doing once the Bots measure server can be driven by a script; it needs a Unity slot and the
  isolated-play ports, so it is its own plan.
- **A bisect helper (idea 9).** `git bisect run python kn_check.py --quick` already works by hand once A exists; a skill
  can come later if it is used.
- **PlayMode tests and cloud CI.** See "What is missing".

## Build order (one PR each)

The commit gate (C) goes last, after `kn_check` has proved itself, and the Master is told before it is switched on,
so no session is blocked in the middle of its work by surprise.

- [x] 1. A without the test step: `kn_check.py --quick`, the Python organization rules (`organization_check.py`), the
  known-findings list and the stamp. No Unity needed.
- [ ] 2. The `--full` tier with `known-test-failures.json` (needs a Unity slot to record today's failures).
- [ ] 3. B: the batch entry points of Validate Project Organization and Check Icons (needs a Unity slot to run them once).
- [x] 4. E and G: `Guides/Review-Rules.md` (real failures by severity with where each was seen, round limits,
  learned rules); the Master adds its link in CLAUDE.md.
- [ ] 5. D: the risk tiers (the table above, then a run over the last 20 merged PRs to see whether the sorting looks right).
- [ ] 6. F: the Evals heading and the CLAUDE.md line.
- [ ] 7. C: the commit hook (warns; blocks only a high-risk change whose `--quick` failed), last, after telling the Master.

## The owner's picks (30 Oct 2026)

1. **The gate command (A):** a) one `kn_check.py` with a quick and a full tier and a known-failures list.
2. **Stopping "done" too early (C):** a) a hook on `git commit` / `gh pr create` that checks the stamp, not a re-run.
   Adjusted the same day: it warns with the failing check and a fix hint and never blocks, except a high-risk change
   whose `--quick` failed. Rolled out last, after telling the Master.
3. **Review rules (E, G):** a) Review-Rules.md linked from CLAUDE.md, and `/code-review` on every PR at the change's
   risk level. Adjusted the same day: the Master runs the review on each PR; the owner does not check every PR and
   sees only "reviewed, safe to merge" or the problems.
4. **Risk tiers (D):** a) sorted by path in `kn_check`, which picks the tests and review depth.
5. **Speed (owner, same day):** `--quick` under 2 minutes, skipping what did not change; no new mandatory step for a
   low-risk change.

The other options offered were: for A, only a known-failures list or nothing new; for C, a hook running the gate at the
end of every turn or a CLAUDE.md rule only; for E, `/code-review` only on high risk or no review file; for D, a tier
picked by hand or one level for everything.
