# Art Direction Correction: Western Comic (Documentation Only)

## Objective
Replace the Wuxia / Eastern-fantasy art-direction framing in project documentation with a Western-comic direction, keeping Twelve Sky 2 as a mechanics reference only and leaving all code, assets and UI strings untouched.

## Changes
- 'Assets/Plans/mmorpg-architecture-plan.md' - Tone / Art Direction line and Wuxia wording
- 'Assets/Plans/katana-melee-combat-plan.md' - Tone / Art Direction line and Wuxia wording
- 'Assets/Game Docs/overview.txt' - add an explicit art-direction preamble
- 'Assets/Game Docs/00_system_map.md' - carry the same art-direction note

## Relevant Assets and Quirks
- 'Assets/Game Docs/01' through '09' cite Twelve Sky 2 as the *systems* source. They are reference analysis, so their mechanics text stays; only art-direction statements change.
- The two Wuxia art-direction lines I originally flagged live in the 'Plans' files ("Stylized Eastern fantasy / Wuxia action RPG"), not in 'Game Docs'. That is why both locations are in scope.
- Per your instruction this pass changes **no** code identifiers, **no** `SkillDefinitionSO.displayName` / `description` values, and **no** `ProgressionHUD.uxml` strings. Consequence to accept knowingly: the live UI still reads "Chi", "Spirit", "Heavenly Shield", "Garuda's Prayer" and "Acupressure", and the katana animation clips keep their names. Deferred, not overlooked.
- The physical art mismatch ('Assets/Synty/PolygonSciFiCity', 'Assets/Synty/SidekickCharacters' vs. 'Assets/Player_01' and the Grruzam katana pack) is explicitly out of scope for this pass.

## Steps
- [ ] 1. Rewrite the Tone / Art Direction line in 'Assets/Plans/mmorpg-architecture-plan.md' to a Western-comic action style, noting Twelve Sky 2 is a mechanics reference only [depends: none]
- [ ] 2. Apply the same rewrite to 'Assets/Plans/katana-melee-combat-plan.md' [depends: none]
- [ ] 3. Add an art-direction preamble to 'Assets/Game Docs/overview.txt' stating the Western-comic direction and that the Twelve Sky 2 material is a systems/number reference [depends: none]
- [ ] 4. Add the matching one-line note to 'Assets/Game Docs/00_system_map.md' so the system map carries the same statement [depends: none]

## Verify
- A case-insensitive search across 'Assets/Game Docs' and 'Assets/Plans' for `wuxia` and `eastern fantasy` returns no matches.
- The same search across 'Assets/Scripts' and 'Assets/UI' returns no new matches, confirming no code, `.uxml` or `.uss` file was touched.
- 'Assets/Game Docs/01' through '09' are unmodified, since they are mechanics references rather than art direction.
