# Twelve Sky 2 — Module 07: PvP Systems

## 1. PvP is the central endgame pillar
Twelve Sky 2 was designed around faction conflict and large-scale PvP rather than treating PvP as an optional side mode.

## 2. PvP layers
The overall structure is:
Individual combat
→ Group combat
→ Guild/clan combat
→ Faction combat
→ Large-scale objective warfare.

## 3. PvP progression
PvP can award resources that feed back into character progression.

Current systems include:
- Contribution Points.
- Class Points.
- Hero Rank points.
- PvP materials.
- Special event rewards.

## 4. Class Points
The Class System converts Class Points into additional class bonuses.

Current Reborn documentation states Class Points can be earned through PvP during Holy Stone Battles.

## 5. Hero Rank
Hero Rank is a weekly ranking system.

It awards points from activities such as:
- Outpost wars.
- Muo Outpost.
- Coliseum.
- Alcatraz.
- Circuit Valley.
- Labyrinth.
- Holy Stone participation.
- Monster kills.

The system resets weekly.

## 6. PvP defense progression
Later progression can include permanent PvP-oriented bonuses.

For example, the current Diamond Pill system provides permanent damage reduction against opponents during PvP and is tied to Rebirth progression.

## 7. PvP design loop
Build character
→ Enter PvP
→ Earn CP/Class/Rank rewards
→ Improve build
→ Enter higher-level PvP
→ Compete for faction objectives and status.

## 8. Important design distinction
PvP has two separate goals:
- Personal combat performance.
- Faction/objective contribution.

A player can therefore be valuable without simply having the highest damage.

## 9. Krytical Nova II: what is built (2026-10-04)

- **Individual PvP:** players of different factions fight freely (see `11_factions_lore.md`); players of the same
  faction only in an accepted **duel**, pairwise and to the death.
- **Death:** the killing blow picks the animation. A blow from the front half turns the body to face it and knocks
  it over backwards; a blow from behind is taken as the body stands and knocks it down forwards. A critical kill
  throws the body about 5.5 m away from the killer, smeared as it flies; on the killer's own screen it hangs in
  slow motion for a blink first (a picture only: see `Rendering-Bible.md` 6f). The body lies where it lands, then the server respawns the
  player on one of the map's spawn points after 8 s (Zone01: the Central Plaza). The countdown is in the chat's
  Combat tab. Code: `PlayerDeathMotor`, `CombatEntity.Killed`, `PlayerSpawnService.Respawn`.
- Not built: group, guild and faction warfare, contribution and class points, hero rank.

### Sources
- Twelve Sky 2 Reborn — Class System Guide.
- Twelve Sky 2 Reborn — Hero Rank System.
- Twelve Sky 2 Reborn — Diamond Pill.
- Twelve Sky 2 — War overview.
