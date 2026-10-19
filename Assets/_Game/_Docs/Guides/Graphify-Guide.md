# Graphify: a queryable map of the project

[graphify](https://github.com/Graphify-Labs/graphify) (Graphify Labs, Apache-2.0) turns our code into a knowledge
graph: every class, method and file, and who calls, uses or references whom. Claude asks the graph
("who calls `ServerSave.SaveTogether`?", "what does changing `PlayerWallet` reach?") instead of grepping and reading
whole files, which saves tokens on every task.

- **Built locally and free:** tree-sitter reads the code. No AI, no API key, nothing leaves the PC.
- **What is in it:** the code of `Assets/_Game` (about 980 files) and the outline (headings) of the `_Docs`.
  `.graphifyignore` at the project root keeps out the vendor packs (about 2,900 more C# files), Unity data files and art.
- **Not committed:** `graphify-out/` is in `.gitignore`; each PC builds its own (under a minute).
- Set up on the owner's PC on 19 Oct 2026: graphify 0.9.92, first graph 17,499 nodes and 55,660 links.

## Install on Windows (once per PC)

Needs Python 3.10+ and uv (`winget install astral-sh.uv`). The official package is **`graphifyy`** (two y's);
other `graphify*` packages are not theirs. Norton inspects HTTPS on this PC, so uv must use the Windows certificate
store (`--system-certs`):

```powershell
uv tool install --system-certs "graphifyy[mcp]"
graphify --version
```

If `graphify` is not found afterwards, open a new terminal (or run `uv tool update-shell` first).

## Hooked into Claude Code (done; repeat only on a new PC)

```powershell
cd "C:\Users\dlaco\Krytical Nova II"
graphify install --project
graphify claude install
```

These wrote:

- `.claude/skills/graphify/`: the `/graphify` skill.
- `.claude/settings.json`: a hook that runs before Claude searches code (Grep, search commands) and reminds it to ask
  the graph first. It only adds a reminder; it never blocks a command. It names the full path of `graphify.exe` on
  the owner's PC.
- `.claude/CLAUDE.md` (points at the skill) and a **graphify** section in `CLAUDE.md`, rewritten for this
  project: which commands work best, and that the docs are still read in full.

## Build the graph

```powershell
graphify extract . --code-only
graphify update .
```

`extract` builds the code graph; `update` adds the docs' outline and writes `GRAPH_REPORT.md` and `graph.html`.
`graph.html` opens in any browser; with this many nodes it shows one dot per group (community) of related code.

The same page is published as the **Nova Code Map**: https://claude.ai/artifact/LR6SFvLVT1Co88bfrX6FZs (a snapshot;
it is republished from `graphify-out/graph.html` when asked, with the title "Nova Code Map" and a phone layout added).

## How much it saves (small test, 19 Oct 2026)

Three questions, measured by how much text comes back to read (about 4 characters per token):

| Question | Search and read | Map | |
| --- | --- | --- | --- |
| Who calls `ServerSave.SaveTogether`? | ~2,300 tokens (grep, then the lines above each call) | ~570 (`explain`) | 4x less |
| What does a change to `PlayerWallet` reach? | ~900 (grep lines) | ~1,100 (`affected`) | no gain |
| How does `PlayerTrade` write its ledger lines? | ~950 (grep, then the method) | ~720 (`explain`), but it only points at the method | still has to read it |

Cost of having it: the `CLAUDE.md` section is about 270 tokens a session, the hook's reminder about 55 per search.
It pays off on "who calls / what uses this" questions and on not opening whole files (`PlayerTrade.cs` alone is about
8,700 tokens); it does not replace reading the code that is being changed.

## Ask it things

Name real types and methods; that is where it shines:

```powershell
graphify explain "SaveTogether"
graphify affected "PlayerWallet"
graphify path "PlayerTrade" "EconomyLedger"
graphify god-nodes
```

- `explain`: one thing, with everything it calls and everything that calls it, with file and line.
- `affected`: everything that would feel a change to it (good before a refactor).
- `path`: how two things connect.
- `god-nodes`: the most connected parts of the code (today: `GmToolsWindow`, `GameFeelBuilder`, `ItemDefinition`).
- `query "<question>"` searches by the words in the question. Put real type names in it; plain-English questions
  come back noisy.

Exit codes for scripts: `0` answer, `3` the name matches several things (they are listed), `4` no match.

## Keep it current

- After code changes: `graphify update .` (local, about a minute).
- After `git pull` or a branch switch: `graphify update .`
- `graphify hook install` would rebuild on every commit and branch switch. It is not on: other sessions commit in
  this same checkout, and a minute per commit adds up. Turn it on later if wanted (`graphify hook uninstall` undoes it).

## Not set up (yet)

- **Names for the groups.** The report calls them "Community 12" and so on. `graphify label .` names them with an
  AI model, but that costs tokens.
- **The meaning of the docs.** `/graphify .` inside Claude Code reads every doc with Claude, which costs tokens
  once per change. Today only the docs' headings are in the graph.

## Privacy

No telemetry. Every query is logged to `~/.cache/graphify-queries.log` (the question and its timing, not the
answer); set `GRAPHIFY_QUERY_LOG_DISABLE=1` to turn that off.

## Remove it

`graphify uninstall` removes the skill, hook and `CLAUDE.md` section (add `--purge` to also delete `graphify-out/`);
then `uv tool uninstall graphifyy`.
