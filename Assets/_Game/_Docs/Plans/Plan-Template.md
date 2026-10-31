# Plan template

Copy this page to start the plan for a new system or a large change (`Plans/<name>-plan.md`). Keep the headings; write
under each in plain words. The plan is a live checklist: tick a step in the same PR that does it.

The one rule this template adds: **the Evals come before any code.** Each eval is one pass/fail check that proves part
of the plan works, and names how it is checked. The owner approves the evals with the plan's other picks; building
starts after that. Where this comes from: `Plans/code-evals-and-checks-plan.md`, item F.

---

# <System name> plan

**Status: proposed <date>, waiting for the owner's picks.** (Later: agreed <date>; step N is built; built in full.)

Row <N> of `../Reference/12Sky/01_Systems_Build_List.md`. Design: `../Design/<doc>.md` (if there is one).

## What it is for

What the player gets, in two or three sentences, using the Krytical Nova terms from the glossary.

## Risk

High, medium or low, from what breaking it does to a player (`../Guides/Review-Rules.md`):
high if a player could lose items, Creds or a save, be locked out or see another player's data; medium if a feature
could stop working; low if something only looks or sounds wrong. `kn_check.py --risk` gives the level of a change.

## Evals (written and approved before any code)

One line per check. Each says what must be true and how it is proved: an EditMode test (name it), a `kn_check` check,
a second-client test, or a manual step the owner does. A high-risk plan has at least one eval for each way a player
could lose something, and a second-client test.

- [ ] <What must be true.> Proved by: `<TestName>` / `kn_check` / second client / owner in Play.
- [ ] <What must be true.> Proved by: ...
- [ ] Saves: <what is saved, and that an old save still loads>. Proved by: ...
- [ ] Two clients: <what both must see the same>. Proved by: second-client test.

An eval is strict enough when bad work fails it. A new test is shown failing once (break the code, see it go red)
before it counts.

## Steps (one PR each)

- [ ] 1. <Step>. Evals it proves: <which lines above>.
- [ ] 2. <Step>. Evals it proves: ...

## The owner's picks

Questions as multiple choice, the recommended option first. Record the answers here with the date.

1. **<Question>:** a) <recommended>; b) <other>; c) <other>.

## As built

Filled in as steps land: what differs from the plan, the numbers that ended up in the settings assets, and how it was
checked.
