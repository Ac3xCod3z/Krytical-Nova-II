# Music mechanics: Cadence, and what may follow it

Written 12 Oct 2026 on branch `music/cadence`, from the owner's note of 10 Oct 2026 and his answers of 10 and
12 Oct. Row 63 of the build list (`../Reference/12Sky/01_Systems_Build_List.md`); rows 64 to 66 are the three
candidates at the end of this page.

**Status: design agreed, nothing built.** Built in milestones M0 to M7 (section 7), each one stopping for the
owner's check, on the branch only, with one merge at the end. Numbers marked *(my pick)* have not been reviewed.

## 1. The rule every music mechanic follows

> **A music mechanic belongs to one part of the game and is live only there.** (Owner, 12 Oct 2026.)

Cadence is combat's. It runs in a Callout, when a Hostile turns on you, later in wars and in The Scar. Out of a
fight, in the starter town, the game looks and sounds as it does without it. The same holds for the candidates:
forge timing is live only at the bench, fishing only with a line in the water, a concert only on its stage.

So every such mechanic needs one honest answer to "is the player in that activity right now?". For combat that
answer is `PlayerCombatState` (section 3).

## 2. What other games do, and what we take

| Game | What it does | What we take |
| --- | --- | --- |
| Hi-Fi Rush | Attacks always come out; a press on the beat hits harder, and a combo ends in a timed Beat Hit. The world moves with the music (pistons, platforms), and so does the robot cat that rides with the hero. An optional on-screen meter shows the beat for players who want it. | Reward, never punish. A companion as the metronome. An optional beat marker. The world moving with the music. |
| Metal: Hellsinger | On-beat shots build a multiplier (4, 8, 16), and the music gains layers as it rises. | Not now: the owner asked for a one-shot bonus. A streak is a later candidate. |
| BPM: Bullets Per Minute, Crypt of the NecroDancer | Actions are tied to the beat; NecroDancer shows a beat meter at the bottom of the screen. A Hellsinger player's complaint puts the risk well: timing rules can feel like playing with input lag on purpose. | The warning: an off-beat skill must still cast, at once, exactly as today. |
| Monster Hunter (roasting meat) | A short tune plays and the player presses as it ends. | The model for forge timing (row 64). |
| Final Fantasy XIV, Performance | Each player's notes go to the server, which plays them back to those nearby, so players in an ensemble hear each other late; the game gives them a metronome and a count-in. | Never judge a press by when it reaches the server. Each player is judged against the music they hear; the server checks the claim. |
| Fortnite Festival | Has a calibration page for each device's sound and picture delay. | A "Beat timing offset" slider in Settings. |
| Friday Night Funkin' | Notes come down lanes; one side plays, then the other. | The model for concerts (row 66). |

What the sources say and what is from memory: the Hi-Fi Rush, Hellsinger, NecroDancer, FFXIV and Fortnite
Festival rows rest on the pages listed in section 9. The cat as a beat reader, the Monster Hunter tune and the
Friday Night Funkin' lanes are from memory and were not checked against a source on the day.

## 3. In a fight or not

`PlayerCombatState` (`Gameplay/Combat/Runtime`) already exists. The server decides and every machine is told.
A player is in a fight from the moment they give or take a blow, cast an attack skill, set upon a target or
stand in a Callout, until six seconds pass with none of it. The pet already reads it.

M1 adds what is missing:

- **A Hostile has turned on the player.** The server knows who each Hostile is after (`IHostileBrain.Target`);
  `HostileNet` tells that player's `PlayerCombatState`. The fight then starts before the first blow, which gives
  the music time to come in.
- **Ground that is always a fight.** Wars and The Scar are not built. `PlayerCombatState` gets one server call
  that holds a player in combat while it is set, for those systems to use. Only a GM command calls it for now.
- It remembers **why** the player is in a fight (blow, attack, Callout, Hostile, held), for `gm.combat`.

**As built (M1, 12 Oct 2026).** `PlayerCombatState.Reasons` (`CombatReasons`: Blow, Attack, Callout, Hostile, Held;
several can hold at once; known where the fight is decided, a client is only told in or out),
`ServerNoteThreat()` (called twice a second by `HostileNet` for the player its brain is after) and
`ServerSetHeld(by, held)`. A fight still ends six seconds after the last blow or the last moment a Hostile was
after the player; a Callout and a hold end at once. `gm.combat` prints the state, `gm.combat hold on|off` holds
it, `gm.combat watch on|off` prints every start and end to the log. The arena is
`Scenes/Development/Test_Cadence.unity`, made from the town by **Krytical Nova ▸ Zone ▸ Build Cadence Test Arena**:
the town, NPCs, traffic and portals taken out, one Hostile Zone (`north-gate`, Bonewalkers, which only fight
back) north of the spawn. A Hostile that attacks on sight is one command away: `gm.hostile.spawn Gargoyle`.

## 4. Cadence

**The rule.** While in a fight, an attack skill's key pressed within the window around a beat makes that cast
deal a little more damage. One cast, then it is gone. Off the beat nothing is lost and nothing is delayed.

- Only a fresh key press counts. A held key that repeats, an Autopilot cast, a buff skill and a charged skill
  earn nothing.
- A press that is queued (mid-swing, or out of reach: `PlayerSkillQueue`) keeps its Cadence for a short time.
  This matters: in a fight the automatic combo is nearly always mid-swing, so most presses are queued.
- The skill that starts a fight cannot be on Cadence. The beat is not playing yet.
- The bonus multiplies the cast's damage. It does not touch Critical, and it is not one of the next-hit buffs
  (Redline and the like), which work as before.
- The automatic three-strike combo is not timed by the player and is left alone (owner, 10 Oct 2026).

**Numbers** *(all my picks)*, in `CadenceSettings`:

| Number | Value | Why this to start |
| --- | --- | --- |
| Window | 90 ms either side of the beat | Hi-Fi Rush is known for being forgiving. At 120 beats a minute a random press lands in it about one time in three. |
| Bonus | +6 % damage on the cast | "A small bonus." Mashing is worth about 2 %, good timing 6 %. |
| Queued Cadence kept | 1.5 s | Long enough for a swing to end, too short to bank one for later. |
| Against players | yes | A Callout is where the owner pictures it running. |
| Switch | `enabled` | Off, the game is as it was. |

**Who decides.** The server decides progression in this game, and a hit is progression. But a press cannot be
judged by when it reaches the server (section 2, FFXIV). So:

1. The player's game measures the press against the music it is playing and sends, with the cast request, the
   time of the press and how far off the beat it was.
2. The server checks that the player is in a fight, that the press is recent, and that its own beat grid puts
   that time about where the client says. If so, the cast gets the bonus.

A cheating client can claim every press is on the beat. What it gains is the small bonus on skills it was
casting anyway; that is accepted for now and noted for the security pass (row 59).

**What the player is shown.** Only the player who earned it, as with damage numbers: the pet pops, a small
sound, a mark on the damage number.

## 5. The beat

- A `MusicConductor` on the zone's World Environment object keeps the zone's **combat playlist** running on
  server time whether or not anyone hears it. It replicates one small state: which track, and the server time
  it started. Offline it runs on local time, as the weather does.
- Every fighter in a zone is therefore on the same beat: both sides of a Callout, a whole Squad.
- Each player's game fades the track in when its own player enters a fight and out when the fight ends
  (`CombatBeat`, a value from 0 to 1 that everything showing the beat reads). The player joins the track where
  it is, not at its start *(my pick)*.
- A track needs a steady tempo. Each `MusicTrack` holds its clip, beats a minute, the time of its first beat
  and beats to the bar, measured by `tempo.py` in the sound-scout skill and checked by the owner's ear with a
  click on every beat (the audition page, and `gm.music.click` in the game).
- Between two tracks there is no beat, and no Cadence.
- The world out of a fight has no music yet. That is its own later piece of work.

**As built (M3, 12 Oct 2026).** `Audio/Runtime`: `MusicConductor` (on the zone's World Environment object; replicates
`MusicState`: when the round began, a track forced by a GM, a GM's nudge), `CombatMusicPlayer` (books the track on
the audio clock a little ahead, so it starts on the sample the shared clock asks for; sets it right again only when
it has sat more than 45 ms off for three quarters of a second), `CombatBeat` (static: `Live01`, `HasBeat`, `Beats`,
`Index`, `Phase01`, `OnBar`, `Pulse()`, `TryError()`), `BeatMath`. A track is a `MusicTrack` asset, a zone's fight
tracks a `ZoneMusic` asset (gap 2 s, fade in 0.6 s, out 2.5 s: my picks); both are made by **Krytical Nova ▸ Setup ▸
Music** from the sound-scout skill's `music.json`. Tracks are kept compressed in memory, not streamed, so that a
start lands on the sample. GM: `gm.music`, `gm.music.track <n|name|round>`, `gm.music.click [on|off]`,
`gm.music.nudge <ms>`. Settings ▸ Audio has **Beat timing offset** (-150 to +150 ms), which moves the beat
everything keeps time to; it does not move the sound. Only the arena has a conductor so far, playing a made-up
test beat (120 beats a minute) until the owner's picks are baked.
Measured in the arena with two Editors on one PC: the sound sat within about 15 ms of the shared clock on the
host and within about 30 ms on the client, and the client's place in the track was within a few tens of ms of the
host's.

## 6. What keeps time

All of it only while the local player is in a fight.

- **The pet** is the metronome: a small scale pop and an emission pulse on each beat, stronger on the first of
  the bar. Pets that bob (Drone Core, the Tidecaller egg, Emberwing) dip on the beat; pets that stand (Kitsune
  Spirit) do not bob. A player sees it on their own pet and on the pets of others who are fighting. Sizes start
  small (the owner's standing note on pet glow).
- **No pet out:** a small beat marker by the Hotbar, Settings choice Auto / On / Off; Auto shows it only with
  no pet out *(my addition)*.
- **The world:** the conductor sets two global shader values each frame, the beat's pulse and its phase, which
  any shader can read at no cost per object, and a `BeatPulse` component drives a light's brightness, an
  emission colour or a scale. Both rest at zero out of a fight: a player walking through town sees a normal
  town while the player duelling beside them sees it move.

## 7. Milestones

Each ends in a commit on `music/cadence` and stops for the owner's check. Tested in the arena
`Scenes/Development/Test_Cadence.unity`; the starter town is not touched before M7.

| | Milestone | Done when |
| --- | --- | --- |
| M0 | Branch and paper | This page and the build list rows exist; the tempo scan is running. |
| M1 | In a fight or not | `gm.combat` turns on for a blow, a Hostile turning on you and a Callout, and off six seconds after. |
| M2 | The music is measured and picked | The owner has picked tracks whose click sits on the beat; they are baked with their `MusicTrack`. |
| M3 | The beat plays in a fight | Silence out of a fight; the track fades in and out with one; two clients hear the same beat. |
| M4 | Cadence | An on-beat skill hits harder on host and client; the window and bonus feel right. |
| M5 | The pet keeps time | All four pets, on host and client; the marker with no pet out. |
| M6 | The world moves | Three examples in the arena, chosen from a review sheet. |
| M7 | Into the game, and the merge | Starter town wired; 60 frames a second held; records updated; the owner says merge. |

## 8. Candidates (not built, not asked for)

The owner's other three ideas, each under the rule of section 1.

**Forge timing (row 64).** At the Armorer's bench, a Temper attempt starts a short figure of beats; pressing on
its last beat adds a small amount to the chance (the owner's "1 to 3 %, undecided"). Live only while the bench
is open. Open questions: whether it applies to Fusion, Tuning and Reforge; whether the added chance may pass
the cap `TemperSettings.maxChance`; whether a missed press costs anything (the rule of section 2 says no). The
chance is decided on the server (`PlayerGearWork.ServerTemper`, `GearProgressionRules.TemperChance`), so the
press would travel with the request as a Cadence press does.

**Rhythm fishing (row 65).** A line in the water starts a pattern; the catch depends on how it is played, and
some items come only from fishing. Needs fishing itself first: water that can be fished, a rod, a catch table.
Live only while the line is out.

**Amphitheater concerts (row 66).** Each faction has an amphitheater. A concert is an event players sign up
for; those on stage play note lanes (Friday Night Funkin' as the model) while everyone in the stands hears it,
for a larger reward. Needs the amphitheaters, a sign-up and schedule, note charts for each song, and a way for
the stands to hear one performance: section 2's FFXIV lesson applies in full, so the audience hears the track
with each player's hits and misses laid over it a moment late, not live notes. What "AI driven" means for the
concerts (the songs, the charts, the performers when no player signs up) is the owner's to say.

Also noted, not asked for: a Cadence streak or multiplier (Hellsinger), music that grows with the fight, music
for the world out of a fight.

## 9. Sources

- Hi-Fi Rush: [Sports Illustrated preview](https://videogames.si.com/features/hi-fi-rush-preview), [Vice on how it was made](https://vice.com/en/article/88qyw5/how-a-designer-known-for-horror-made-the-musical-gaming-triumph-hi-fi-rush), [DSOGaming announcement](https://www.dsogaming.com/news/hi-fi-rush-is-a-new-action-game-where-the-world-syncs-to-the-music-from-the-creators-of-evil-within-ghostwire-tokyo/)
- Metal: Hellsinger: [PCGamesN review](https://www.pcgamesn.com/metal-hellsinger/review-pc), [Checkpoint Gaming review](https://checkpointgaming.net/reviews/2022/09/metal-hellsinger-review-riff-tear/), [a player's complaint on Steam](https://steamcommunity.com/app/1061910/discussions/0/3415431214848534273)
- Final Fantasy XIV: ["Ensembles Assemble", official blog](https://na.finalfantasyxiv.com/blog/002867.html), [Performance, Console Games Wiki](https://ffxiv.consolegameswiki.com/wiki/Performance)
- Fortnite Festival: [tune-up settings, Epic Games](https://www.epicgames.com/help/fortnite-festival-c-202300000001635/c-Trending_0/fortnite-festival-a202300000015798)
