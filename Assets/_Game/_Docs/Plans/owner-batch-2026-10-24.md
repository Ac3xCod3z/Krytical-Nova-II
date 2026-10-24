# Owner batch, 24 Oct 2026: checklist

A live checklist: each box is ticked as its piece lands on main. Under each item is what I understood you asked for,
so you can correct me early.

## Already landed today

- [x] **Beat cues** (PR 150). Beat Ring at the feet with no pet; closing ring that turns gold in the window and locks on
      in a gold flare; cyan count-in; the beat track above the Hotbar; EARLY / LATE; a metronome setting.
- [x] **Beat glow** (PRs 151, 152). In a fight the world's light breathes to the beat: glowing surfaces, bloom, nearby
      lights and the Nova; dimmer between beats, brighter on the beat.
- [x] **Character select picks the test server** in an Editor test session (PR 153).
- [x] **Softer bloom** (PR 154). *Understood:* the horizontal "tears" are Beautify's anamorphic streaks; they are halved
      (0.12 → 0.06) and narrower (spread 1.2 → 0.7), and the beat glow lifts the bloom less.
- [x] **Beat pulse on weapon glow** (PR 154). *Understood:* the glow round weapons (and pets) swells on the beat with
      the rest of the beat glow.
- [x] **Strike Dummy always infinite HP** (PR 154). *Understood:* dummies never go down, and the GM options that lower
      their health (`gm.dummy.hp`, `gm.dummy.maxhp`, `gm.dummy.inf` and their GM Tools controls) are gone.

## Working on now

- [ ] **Accessibility: flashing.** *Understood:* a photosensitivity warning the first time the game starts, and an
      Accessibility page in Settings where every flashing or pulsing effect can be turned down (a strength slider and a
      Reduce flashing switch): the beat glow, Beat Ring flashes and camera kick, COMBO shake, hit flashes, pet flashes,
      Nova flashes and lightning.
  - [x] Settings ▸ ACCESS page with the slider and the switch
  - [x] Every flashing effect follows it
  - [x] First-launch warning screen (also reachable again from the page). *Owner, 24 Oct:* an **agreement page**:
        "I UNDERSTAND AND AGREE" (or agree with Reduce flashing), and the answer is stored on the PC so it does not
        appear on every load; it comes back only from the Accessibility page's button.
        *Built:* the agreement and its stored answer (`kn.access.warningAgreed`, with the date). *Owner, 24 Oct:* UI
        Toolkit as the base, with faction skins for the real versions: rebuilt as a UI Toolkit document
        (`UI/Settings/Resources/FirstRunSetup.uxml`) that wears the character's faction skin in the game and the menus'
        skin on the title; the card is solid in each faction's window colour. The title and the opening wait for it
        (its clicks never skip the title). Play-tested in all three skins, 24 Oct.
    - [x] *Owner, 24 Oct:* once agreed on the PC and the player joins with an account, the agreement is recorded on
          the account in the save database (through the save service), so KN keeps a record of it. *Built:* the
          account part `agreements` (first and latest time agreed, and when the server recorded it); a new agreement
          on any PC updates "latest". Needs no database change. Test with a second client left.
- [x] **Colour-blind setting.** *Understood:* a correction for Protanopia, Deuteranopia and Tritanopia with a strength
      slider, on the same Accessibility page, applied to the whole screen.
      *Built:* a screen pass after post-processing (`ColourBlindFeature`, daltonization) on the PC renderer, costing
      nothing while Off; the setup's colour swatches use the same sums. *Not yet:* the HUD and menus are drawn over the
      picture, so the pass does not reach them; their colours can be put through `Accessibility.Correct` next.
- [ ] **HDR settings.** *Understood:* HDR on / off and brightness (paper white, peak) in Settings ▸ Graphics, and set up
      in a short display step the very first time the game starts, adjustable later.
- [ ] **Beat track skinned per faction.** *Understood:* the beat lines above the Hotbar should use each faction's HUD
      art from the skin assets, the same layout but looking more custom and better.

## Next, in this order

- [ ] **When Cadence stops.** *Understood:* the fight music and Cadence must not end just because a timer runs out. Each
      kind of fight gets its own "is it over?" check:
  - Strike Dummy: until you deselect the dummy **and** move at least 30 m from it.
  - Hostiles: while any Hostile targets you or is still coming at you; it ends once all are dead or none approach.
  - Callouts: until the bout ends. Wars and PvP events: their own rule when they are built.
- [ ] **Water.** *Understood:* you can jump on water (land on it as on ground) and fall into it; falling in puts you back
      on the nearest shore, losing some HP. *Owner, 24 Oct:* the effects of a jump off and a landing on water must be
      more prominent than walking or running on it (bigger splash, spray ring and sound), to show the greater force.
- [ ] **Character creator, proportions page.** *Understood:* while on the page with height, bust and butt sliders the
      character wears the bare outfit so the change shows best. While adjusting height only, a height ruler stands
      beside them with something from the world for scale (a tree), and a note under it says it is there for reference.
- [ ] **GM Tools window.** *Understood:* the World tab cannot change the time or anything else, and parts of the window
      are blacked out (boxes and text you cannot see), though most buttons still work. Fix the World tab and the drawing.
      *Owner, 24 Oct:* seen at least while in Play mode; check both in Play and out of it.

## Housekeeping

- [ ] GM Command Reference updated (dummy commands gone, new ones added)
- [ ] Encyclopedia of Nova rebuilt and published after each landing
- [x] Test server stopped and test session removed when you are done testing (24 Oct: server closed, session file
      removed, the Editor's last server back to "Play here")
