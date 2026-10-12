# World scale: the measure everything is held to

Written 12 Oct 2026, with Crew Halls (row 62), because a Hall is where a player first stands next to a chair.
The numbers live in `WorldScaleSettings` (`Assets/_Game/World/Data/Assets/WorldScale.asset`, written by
**Krytical Nova ▸ World ▸ Build World Scale**); this page is the same table in words. Change both.

**Status: agreed.** The owner chose option A (true size, up to 25 % over) on 12 Oct 2026 from the three at the
end of this page. Changing it later is one number and a rebuild.

## The rule

1. **One unit is one metre.** Nothing in the project is authored at another scale.
2. **A person is 1.8 m tall.** Every size below is read against that figure, and every review picture of a
   building, a room or a furnishing carries a 1.8 m figure for the same reason.
3. **A made thing is the size the real thing is.** A chair is a chair's size because someone 1.8 m tall sits
   in it. Each kind of thing has a real measurement it is rooted in (the table below), and a model of it may
   be up to **25 % over** that or **15 % under** before it is brought back. The 25 % is not a taste: it is what
   the art packs draw. Measured across the nine packs in the project, their own doors are true to life (1.0 m
   by 1.97 m), their tables are 0.74 to 0.97 m, their chairs 1.0 to 1.25 m to the top of the back, their beds
   2.2 to 2.7 m: furniture is drawn a little heavy so that it reads from a camera fifteen metres up.
4. **Grand is a stated ratio to a person, never an accident.** What is meant to impress says how many persons
   high it is:

| Tier | Ratio to a person | In metres | What belongs here |
| --- | --- | --- | --- |
| Tiny | a sixth or less | to 0.3 | what is held in a hand: a cup, a candle, a book, a skull |
| Small | to a half | 0.3 to 0.9 | a stool, a pot, a lantern, a pet (0.4 m) |
| Human | about one | 0.9 to 2.7 | furniture, a house door (2.0 m), a storey (3.0 m) |
| **Heroic** | 1.5 and up | 2.7 to 5.4 | a Hall's door (3.2 m), a throne, a hero's statue, a hall's hearth |
| **Monumental** | 3 and up | 5.4 to 18 | a Hall (6 m inside, 7 to 15 m outside), a gate, a grown tree, a colossus |
| **Colossal** | 10 and up | 18 and over | the Storm Sovereign (18 m to the head), a citadel, a great tree at its tallest |

   A thing is moved up a tier on purpose and by a builder's number (a Facade's `modelScale`, never more than
   1.5), so that a Hall is grand because it was made one and a half times a house, and a cup is tiny because a
   hand holds it.
5. **Rooms follow the packs' own module.** The packs build in 2.5 m and 5 m pieces with a 3 m storey. A Hall's
   room is two storeys high (6 m) and its floor is in multiples of 5 m (20 by 15 m at tier 1), so that every
   Finish fits it without being stretched.
6. **The camera gets its allowance in the opening, not in the object.** A doorway a player walks through is
   made wider than life (the Hall's is 2.4 m) because the camera is behind and above them; the door, the
   table and the bed beside it are not.

## The kinds

A builder names what kind of thing a model is; `WorldScaleSettings.Fit` answers what to multiply it by. A
model within its kind's range is left exactly as its maker made it (and one within 5 % of the range is left
alone too, since a hair's breadth is not worth a changed mesh).

| Kind | Judged by | Real size | In the game | Rooted in |
| --- | --- | --- | --- | --- |
| chair | height | 0.95 m | 0.81 to 1.19 | a dining chair's back is at 0.85 to 1.0 m, its seat at 0.45 m |
| stool | height | 0.6 m | 0.4 to 0.9 | a stool's seat is 0.45 m, a bar stool's 0.75 m |
| armchair | height | 1.1 m | 0.85 to 1.5 | an armchair or office chair is 0.9 to 1.2 m |
| throne | height | 1.9 m | 1.5 to 2.7 | a seat of state stands a head over the person in it (heroic) |
| bench | length | 1.8 m | 1.2 to 3.2 | a bench for three is 1.5 to 1.8 m |
| sofa | length | 2.2 m | 1.6 to 3.1 | a three-seat sofa is 2.0 to 2.3 m |
| bed | length | 2.0 m | 1.9 to 2.6 | a mattress is 1.9 to 2.0 m, a made bed with its frame 2.2 m |
| bunk | length | 2.0 m | 1.9 to 3.6 | a berth is as long as a bed; a rack of them as long as it is wide |
| table | height | 0.75 m | 0.65 to 0.95 | a dining table or a desk is 0.74 to 0.76 m |
| lowtable | height | 0.45 m | 0.3 to 0.6 | a coffee table |
| counter | height | 1.0 m | 0.85 to 1.3 | a kitchen counter is 0.9 m, a bar 1.05 to 1.1 m |
| tallcase | height | 2.0 m | 1.5 to 3.0 | a wardrobe, bookcase or locker; library shelving goes a storey high |
| lowcase | height | 0.85 m | 0.6 to 1.3 | a dresser or a sideboard |
| chest | length | 0.9 m | 0.5 to 1.4 | a sea chest |
| crate | largest side | 1.0 m | 0.5 to 1.5 | a shipping crate |
| barrel | height | 0.95 m | 0.6 to 1.25 | a wine barrel stands 0.95 m |
| candle | height | 0.25 m | 0.08 to 0.7 | a candle in its stick |
| tablelamp | height | 0.5 m | 0.2 to 1.0 | a lamp or lantern carried in a hand |
| floorlamp | height | 1.6 m | 0.9 to 2.2 | a standing lamp is about head height |
| lamppost | height | 4.0 m | 2.5 to 5.5 | a street lamp for walkers is 3 to 5 m |
| chandelier | largest side | 1.2 m | 0.7 to 2.4 | a hall chandelier is 0.9 to 1.5 m across |
| brazier | height | 1.0 m | 0.6 to 1.6 | it burns at waist to chest height |
| hearth | height | 1.6 m | 1.0 to 2.8 | a mantel is at 1.2 to 1.5 m; a hall's hearth is taller than a person |
| screen | largest side | 1.0 m | 0.25 to 2.0 | a monitor is 0.5 to 0.7 m, a wall screen 1.2 to 1.9 m |
| console | height | 1.2 m | 0.7 to 1.9 | worked at with the hands |
| machine | height | 1.9 m | 1.2 to 2.8 | a vending machine or server rack is 1.8 to 2.0 m |
| statue | height | 2.4 m | 1.2 to 4.2 | life size to twice life size on its plinth |
| monument | height | 5.4 m | 4.0 to 9.0 | three persons high or more |
| fountain | length | 3.0 m | 1.2 to 5.5 | a courtyard basin is 2 to 4 m across |
| well | length | 1.8 m | 1.2 to 3.6 | a well's mouth is 1.2 to 1.8 m |
| rug | length | 2.5 m | 0.8 to 9.5 | a rug is 1.5 to 3 m, a hall runner up to 9 m |
| picture | largest side | 0.9 m | 0.25 to 2.6 | a painting, a poster, a mounted shield |
| banner | height | 2.2 m | 1.0 to 4.5 | a hall banner hangs 1.5 to 4 m |
| small | largest side | 0.3 m | 0.04 to 0.9 | what is held in a hand |
| decor | largest side | 1.5 m | 0.4 to 3.6 | between a chair and a wardrobe |
| shelter | height | 4.0 m | 2.5 to 10 | a gazebo or an arch is walked under |
| potplant | height | 0.9 m | 0.3 to 2.2 | knee to chest high |
| planter | length | 1.5 m | 0.5 to 5.2 | a planter box |
| flowers | height | 0.5 m | 0.15 to 1.3 | ankle to knee high |
| bush | height | 1.2 m | 0.5 to 2.8 | a garden shrub |
| hedge | height | 1.3 m | 0.8 to 2.2 | waist to shoulder high |
| smalltree | height | 5.0 m | 3.0 to 8.0 | a garden tree: a maple, a cherry |
| tree | height | 9.0 m | 6.0 to 14 | a grown tree |
| greattree | height | 16 m | 12 to 25 | the tallest thing on a Lot |
| rock | largest side | 1.5 m | 0.4 to 4.0 | a garden boulder |

## What it did to the furnishings (12 Oct 2026)

All 223 furnishings were measured and fitted. Most were already true and were left alone. The ones the
measure moved:

- **Brought down:** the Hegemony's Executive Bed and Bar Stool (5 %), the Null's Iron Throne (3.1 m to 2.7),
  Great Brazier (3.7 m to 2.8) and Iron Cot, the Covenant's Elven Chair (1.44 m to 1.19), Feast Table,
  Spring Fountain, Leaf Chandelier and Moon Lantern.
- **Brought up:** a few small things below their kind's range (the Viking Realm's brazier). The older Vikings
  pack's pines, drawn 3.6 m tall, were first brought up to 6 m and then replaced by the Viking Realm's, which are
  true (9.7 m and 13.8 m).
- **Left grand on purpose:** the Null's Colossus (5.7 m, a monument), the Covenant's Lamp Post (5.7 m).

The Red Maple with the 23 m crown that dominated the first lawn is gone with the Samurai pack's pieces (the
Hegemony draw on the sci-fi packs only); its like would now be a `smalltree`, 4 to 7 m.

## Standing in the way

The same measurements decide what a walker bumps into. A piece's solid part is its mesh between the ground
and 2.1 m, the height a person takes up: a table is all of itself, a tree is its trunk, and its crown is solid
too but walked under. A piece lower than 0.35 m (a knee-high step) stops nobody, nor does a rug, a tuft of
flowers, an arch or a gazebo. The numbers are `blockHeight` and `walkBand` in `HousingSettings`.

## The options the owner chooses from

| | The rule | What changes |
| --- | --- | --- |
| **A (chosen)** | true size, up to 25 % over | the packs' furniture stays as drawn; only outliers move |
| B | true size, up to 10 % over | most chairs, beds and tables are brought down 10 to 15 %: slimmer beside a character, closer to life |
| C | everything made is one and a quarter times life | furniture is left as drawn and the small things, doors and rooms are brought up to match: a chunkier world |
