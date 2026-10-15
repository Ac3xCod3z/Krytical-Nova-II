# Encyclopedia of Nova: how the page is made

The **Encyclopedia of Nova** is the owner's page for reading and reviewing the whole game: every system with what is
built and what is left, every term explained, the open questions, and every doc under `Assets/_Game/_Docs` in a
Library. It is an artifact page:

**https://claude.ai/artifact/JdLA18R3XmfEvAu9c7sAeB** (same link as the old Systems Build List page)

The `~` at the end of this folder's name makes Unity ignore it, so nothing here is imported as an asset.

## What it is built from

- `Reference/12Sky/01_Systems_Build_List.md` is the source of the page's own chapters: the master build list (one
  strip per row), "What is left" (one panel per numbered point), the open questions, the glossary (five columns; the
  last, "What it is for", must say what the thing does for a player and whether it is built), the variance notes, the
  candidates and the change log.
- Every other `.md` file under `_Docs` (Design, Plans, Reference, ProjectOrganization) is put whole into the page's
  Library and rendered in its reader. A new doc appears there by itself on the next build.

Links are made by the builder, not written by hand:

| Written in a doc | Opens |
| --- | --- |
| `row 12`, `rows 54, 57 to 61` | that system's strip |
| a doc's path in backticks, as any doc writes it (`Plans/hostiles-plan.md`, `../../Plans/online-roadmap.md`, `05_Economy_And_Social.md`) | that doc |
| the same followed by `section 7`, `sections 6 and 6b`, a section code like `6g`, or `, "Serums"` | that doc at that heading |
| `gear spec section 4` / `Gear spec sections 6 and 6b` | the gear spec at that section |
| `design 07 / 08` | `Design/07_…` and `Design/08_…` |
| `Lore Bible` | `Design/Lore-Bible.md` |
| the 12Sky help pages of a system | that page of the transcription |
| a markdown link to another doc inside a doc | that doc, at the anchor if it has one |

## After every task that lands

1. Update `01_Systems_Build_List.md`: the system's Status and its "In the repo" / "Left to build" columns, "What is
   left", a glossary row (with its "What it is for") for every new term, the open questions, and one line at the end
   of the Change log.
2. Build the page from the project folder:

   ```bash
   python "Assets/_Game/_Docs/Encyclopedia~/build_encyclopedia.py"
   ```

   It writes `out/encyclopedia.html` beside this file (`out/` is not committed) and prints the counts. It stops with a
   message when a row or a glossary line is malformed (a glossary row without its fifth column, for instance).
3. Publish `out/encyclopedia.html` to the artifact link above (the Artifact tool, `url` = that link). Other sessions
   publish to it too: read the live artifact first, as the tool asks, but rebuild from the docs rather than patching
   the live page, since the docs are the source.

## The files

- `build_encyclopedia.py`: reads the docs, builds the page. No packages beyond Python's own.
- `template.html`: the page (the comic look shared with the GM Command Reference: Bangers, Barlow, ink panels, a
  halftone ground) with `{{…}}` placeholders. The markdown in the reader is rendered by `marked` from cdnjs.
