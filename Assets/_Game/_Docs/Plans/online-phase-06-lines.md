# Online phase 6: Lines, fronts and Mercs

Part of `online-roadmap.md` ("Lines, fronts and Mercs" there is the design; this is the build). Written
11 Oct 2026, rewritten the same day after the owner's decisions. **Not built.** Needs Phase 5; every cap
here is sized from Phase 7, which therefore runs first.

## Goal

The game is one open world: one copy of every map, one server per map and per war. This phase builds the
few cases where a second copy is made by rule, and the two things that keep wars full and fair: splitting
a queue into even wars, and Mercs.

A **Line** is one running copy of a map (the owner's word, 11 Oct 2026; other games say channel, which
here means chat).

## Decided by the owner (11 Oct 2026)

- One open world. Extra Lines only by the rules below.
- Wars and The Scar are one server for all players. During a Beacon War each faction's zones are open to
  all players too.
- The Beacon stands in the Home City map, and the city can be attacked by the other factions.
- A war has a cap per faction and a queue. When more queue than one war holds, the server takes everyone
  who queued and splits them into as many even wars as are needed (the owner's example: a cap of 99, 33 a
  faction; 150 queued; two wars).
- **Mercs.** When the sides are uneven, the server asks players on the crowded side whether they will
  join another faction for the current event only. The player is dressed in the equivalent equipment;
  stats, skills and everything else are a carbon copy. They fight for the short side for that event, so
  nobody has to sit out.
- If too few accept, the war opens uneven up to a limit; players beyond it wait.
- A Merc earns what the side they fought for earns. The war in The Scar is fought for a faction-wide buff
  lasting an hour, which a Merc cannot benefit from; other rewards are decided later.
- No Mercs in Beacon Wars. Beacon Wars are open to absolutely everyone, whatever map or zone they are
  on, for now.
- Fronts, with one Beacon shared across them, and The Scar opening a second Line from an even queue:
  yes. Mercs apply in The Scar as well.
- A Home City's shops and Kiosks stay open during a Beacon War.

## How it works

| Case | What makes a Line | Behaviour |
| --- | --- | --- |
| A hunting map, hub or Home City that overflows | Measured population above what one server holds | New arrivals fill a Line to its soft cap before the next one opens; the hard cap leaves room for Squad mates and friends joining on purpose |
| Siege Night, Turf War | The queue when the war opens | One war per `cap` players queued, sides matched in each, Squads kept together. Each war is its own server and has its own result |
| Beacon War | More players than one front holds | A front is one Line of each map on the road (The Scar, the faction maps, the Home City). Every front shows the one Beacon with one health, kept by the world service; it falls on every front at once. Nobody waits; no Mercs |
| The Scar | An even group waiting at the portals | The Scar 1 always exists, with a cap per faction. An arrival whose side is full is offered a place as a Merc on a short side. Those who decline wait; when enough wait for a fair fight, The Scar 2 opens and they are dealt in evenly. An emptied Line stops taking arrivals and closes |

**Dealing a queue into wars.** Inputs: who queued, their faction, their Squad, the cap per faction, the
unevenness limit. Steps: (1) count each faction; (2) offer Merc places from the largest side to the
smallest until the sides are within the limit or nobody else accepts; (3) the number of wars is the
queued total divided by the war's size, rounded up, but never so many that a war falls under the
smallest size worth opening; (4) deal Squads, then single players, so each war has the same number from
each side; (5) players beyond the unevenness limit stay in the queue for the next opening. The rules are
a pure class with tests, like `TradeRules`.

**A Merc.** For the event only: hostility follows the side fought for; the outfit shows that faction's
equivalent set; the namecard shows that side's crest and a MERC tag. Stats, skills, level, gear stats,
Pack, Creds and the saved character are untouched. Proposed and not yet reviewed: the look is applied the
way Holoweave applies one (`EquipmentLooks`: it changes what is drawn and nothing else), and where a body
cannot wear the other faction's set the crest and colours change instead; the Merc hears that side's
Faction chat for the event and keeps Crew and Squad chat; a Squad answers as a whole through its leader.

**Where a map has more than one Line.** The world service places an arrival in the Line a Squad mate is
in, then the fullest Line below its soft cap. The Line list shows every Line of the map and how full it
is, and marks those holding Squad mates, Crew mates and Contacts. Switching uses the travel ticket of
Phase 5 with the same map as the destination; it is refused while fighting, in a Callout, while trading,
while dead and into a full Line, and there is a short wait between switches. "Go to their Line" is
offered from the Contacts and Squad windows. The Line shows beside the map name on the HUD.

## Open design questions for the owner

1. **The unevenness limit**: how much larger one side may be than another when a war opens.
2. **The smallest war worth opening**, so a queue is not split into two thin wars.
3. **Kiosks on a map with several Lines.** With shops and Kiosks open during a Beacon War and the Home
   City's Lines being the fronts, a Kiosk in every Line (one stock, sold through the world service) is
   the natural reading. Confirm.
4. **Bruisers and bosses:** one per Line, or one per map shared across Lines.
5. **A Merc's body.** What a Null frame or a skeleton shows when fighting for the Covenant.
6. **What "open to absolutely everyone" costs.** If a Beacon War draws more than the fronts a machine can
   run, something has to give. Revisit after Phase 7.

## Work items

1. A `WorldSettings` asset: per map, its kind and caps; per war kind, the cap per faction, the
   unevenness limit and the smallest war; the switch wait. No number in code.
2. `WarQueueRules` (pure, tested): the dealing above.
3. The world service: the Line table (map, Line, address, population), placement, the list, the war
   queue, opening and closing Lines and fronts, the shared Beacon health.
4. The Merc offer and state: a server-sent question, one server-checked answer, the event-long change of
   side and look, and its end. A new player-side component (a row in `PlayerSystemsSetup`); its request
   goes on the audit list of `online-security-model.md`.
5. A player-side component for the Line list and switching (another row), its requests on the audit list.
6. The Line list, the war queue and the Merc offer in each faction's skin, with keyboard prompts only.
7. The Line beside the map name; in Contacts, Crew and Squad lists.
8. GM: `gm.line` (list, move me, move target), `gm.war.queue`, `gm.merc`, `gm.say` to one Line or all.

## Tunables

Per map: soft cap, hard cap. Per war kind: cap per faction, unevenness limit, smallest war. Global: the
wait between switches. None is chosen; the caps come from Phase 7. The only figure in code today is
`defaultMaxPlayers = 100` on `NetworkConnectionManager`.

## Done when

- A queue larger than a war's cap, played by bot clients and two real clients, opens the right number of
  wars with matched sides and Squads kept together.
- An uneven queue produces Merc offers; an accepted one fights for the other side with the same stats and
  skills, in that side's look, and is itself again afterwards with nothing saved changed.
- Too few acceptances: the war opens within the limit and the rest wait.
- A Beacon War on two fronts: damage on either front lowers the one Beacon, and it falls on both at once.
  No Merc offer appears. The Home City's shops and Kiosks work throughout.
- The Scar opens a second Line only from an even group of waiting players, and closes it when it empties.
- Two Lines of a town: placement with a Squad mate, the list, switching, refusal while fighting and into
  a full Line; Faction chat typed in one is read in the other.
