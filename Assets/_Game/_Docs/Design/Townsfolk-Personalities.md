# Townsfolk personalities (DRAFT for the owner to edit)

Owner's DAY list item 18 (13 Nov 2026): the worker drafts all ten, the owner edits. Every number is **proposed**. The real
values live in the ten assets in `Gameplay/Townsfolk/Data/Assets/Personalities/` (`TP_*.asset`, menu Krytical Nova ▸
Townsfolk ▸ Personality): edit them there. Krytical Nova ▸ Townsfolk ▸ Build Assets never overwrites an asset that exists.

Speeds in m/s (Synty walk about 1.4, jog about 3.5). "Stops" = chance to stop at a spot after a walk, else it strolls on.
"Stay" = seconds at a spot. Out = chance to be out at dawn / day / dusk / night (game hours: dawn 05-08, day 08-18, dusk
18-22, night 22-05). How many of each are out is the area's mix (Sky Concord: 48 by day, 20 at night, owner's pick).

| # | Personality | Walk | Stops | Stay | Likes (weight) | Chats with | Players | Out | Rain | Sky Concord day / night |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Busy Worker | 1.6-1.8 | 90 % | 120-300 | Work 1, Stall 0.4, Vendor stall 0.3 | Busy Worker, Guard | ignores; steps aside at 2.5 m | shifts 06-14, 14-22, 22-06 | shelter | 8 / 3 |
| 2 | Stroller | 1.0-1.2 | 50 % | 20-60 | Viewpoint 1, Bench 0.8, Stall 0.4, Vendor stall 0.4, Chat 0.3 | Stroller, Gossip, Shopper | glances 3 s, greets 10 % | 40 / 80 / 60 / 10 % | shelter, then home | 8 / 2 |
| 3 | Jogger | jogs 3.2-3.8 | 15 % | 10-20 (stretch) | Jog point 1, Viewpoint 0.5 | nobody | swerves, nods 5 % | 90 / 20 / 60 / 5 % | home | 3 / 0 |
| 4 | Gossip | 1.1-1.3 | 70 % | 60-180 | Chat circle 1, Bench 0.6, Stall 0.4 | Gossip, Nosy Local, Shopper, Stroller | looks 4 s, comments 15 % | 20 / 70 / 70 / 10 % | chat under shelter | 6 / 1 |
| 5 | Shopper | 1.2-1.4 | 70 % | 15-45 per stall | Stall 1, Vendor stall 0.9, Bench 0.3 | Shopper, Gossip | glances 2 s | 0 / 80 / 50 / 0 % (stalls 08-20) | under canopies | 8 / 0 |
| 6 | Loner | 1.0-1.3 | 30 % | 120-300 | Lean wall 1, Bench 0.8, Viewpoint 0.7; crowds count double | nobody (declines) | looks away, keeps 3 m | 40 % all day | ignores it | 3 / 3 |
| 7 | Nosy Local | 1.0-1.2 | 60 % | 30-90 | Lean wall 1, Chat circle 0.6; spots near players +50 % | Gossip, Nosy Local | watches 6 s, greets 25 % | 50 / 70 / 50 / 10 % | shelter, still watching | 4 / 0 |
| 8 | Night Owl | 1.0-1.3 | 40 % | 60-180 | lit Lean wall 1, lit Bench 0.8, Viewpoint 0.6 (only lit spots at night) | Night Owl, Loner | nods | 0 / 10 / 50 / 90 % | shelter | 2 / 6 |
| 9 | Street Performer | 1.0 | always | 300-600 | Perform 1 (an audience view beside each stage) | nobody | plays to a player who stops | 0 / 70 / 80 / 20 % | packs up, home | 2 / 1 |
| 10 | Guard | 1.3 | always | 60-120 per post | Guard post 1, Gate 0.8 | Guard | nods; watches runners | shifts 06-18, 18-06 | stays | 4 / 4 |

**Changed from the plan's table (for the owner to confirm):** the Busy Worker got a third shift, 22-06, because the plan's
night mix has 3 workers out while its shifts ended at 22:00. The "Players" column and the lines are client-only and come
with step 6 (head-look, glances, greetings, text barks); the first pass shows the plan, the spots and the activities.

Lines (text first, owner pick 4a; each personality's `barks` in its asset): kept neutral and free of lore claims until the
owner writes them.
