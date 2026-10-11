# 12Sky / Twelve Sky 2 reference pack

Reference material for Krytical Nova's systems, exported from the Claude doc "12Sky Systems Transcription & Krytical Nova Build List" (4 Oct 2026).

| File | What it is |
| --- | --- |
| `01_Systems_Build_List.md` | Master Glossary (Twelve Sky 2 term → Krytical Nova term → what the code calls it today), the 62-row build list with per-system Status, "What is left" (the open work in the order it unblocks the most), variance notes, ranked candidates, open questions. **This is the renaming scheme and the checklist.** |
| `02_Gear_System_Spec.md` | Concrete spec for Tempering, Fusion, Tuning, Slotting + Cores, Epic Mk, Ascension and Nova Standings (section 9, as built), set names and colour themes, auxiliary slots, data model, build order. |
| `03_Full_Transcription.md` | All 31 pages of the 12Sky in-game help, transcribed in Krytical Nova terms with the reference term in brackets, each with its screenshot. |
| `04_Weapon_Classes.md` | The nine weapon classes (three per faction), the ten-slot skill structure, all 90 skill names, weapon meshes per class, paired weapons and where each is carried, Discipline, starter kit, GM gear commands, and what is built. |
| `05_Economy_And_Social.md` | Saving, Creds and the economy model, item pickup, Vault and Deep Vault, Trade, Kiosks, Contacts, Crews, namecards and the emote wheel **as built**: the rules, every tunable number, who may deal with whom, keys, commands, what is not built. |
| `pages~/12sky_page_NN.jpg` | The 31 original screenshots. The `~` suffix makes Unity ignore the folder, so they are not imported as textures. |

## Rules for Claude Code

1. **Names.** Code, assets and UI text use the Krytical Nova term from the Glossary. The TS2 term appears in docs only, in parentheses, so the owner can read by the name he knows. Existing identifiers (`NpcRole.Blacksmith`, `MaxChi`, `Pill_*`…) may keep their enum / field names with a Krytical Nova display name, unless a row in the Glossary or the build list says to rename outright.
2. **Status.** When a system lands, update its Status in `01_Systems_Build_List.md` (`Complete` / `Partial` / `To build` / `Candidate`) and tick it in the checklist view. Do not delete rows.
3. **Economy and social.** `05_Economy_And_Social.md` is the "as built" record: keep it current when one of those systems changes, and put new numbers in a settings asset (`EconomySettings`, `CrewSettings`, `NovaSettings`, `EmoteWheelSettings`), not in code.
4. **Gear.** Follow `02_Gear_System_Spec.md` sections 11–12 in order; numbers marked *(proposed)* are tunables in ScriptableObjects, not constants in code.
5. **Design docs.** `../../Design/*.md` still use the TS2 vocabulary (Reinforcement, Combination, Refinement, CP, Silver). They are the long-form reference; when they conflict with these files on a name, these files win.
6. **Rename depth.** For systems that already exist, only what a player reads or types changes (owner's decision, 4 Oct 2026): identifiers, asset file names, enum values, saved keys and GM command names stay, and old slash commands keep working beside the new ones. New code uses the Krytical Nova name from the start.
