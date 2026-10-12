using System.Collections.Generic;
using System.Linq;
using KryticalNova.Items;
using KryticalNova.Pets;
using KryticalNova.Progression;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Builds the pets from one table (design: _Docs/Design/12_pet_evolution_system.md): the item that is each pet,
    /// its definition (stat, maximum, five stage looks), the pet catalog and the pet settings. Every pet is Common.
    /// Re-runnable. What the table owns is rewritten on every run (name, text, rarity, which model it has, whether it
    /// stands or bobs, its movement loop);
    /// numbers and stage looks are only filled in when missing, so values tuned in the Inspector survive.
    /// Also carries out the move from the old drone pets: assets and icons are renamed in place (ids and
    /// references kept) and retired pets are removed.
    /// </summary>
    public static class PetAssetBuilder
    {
        public const string PetsRoot = "Assets/_Game/Gameplay/Pets/Data/Assets";
        public const string SettingsPath = PetsRoot + "/PetSettings.asset";
        public const string CatalogPath = PetsRoot + "/PetCatalog.asset";
        private const string ItemFolder = SidekickGearGenerator.ItemsRoot + "/Pets";
        private const string Drones = "Assets/Synty/PolygonSciFiCity/Prefabs/Props/";
        /// <summary>The pets' own models, one folder per pet.</summary>
        public const string PetModels = "Assets/_Game/Characters/Pets";

        private static readonly float[] StageScales = { 1f, 1.08f, 1.16f, 1.24f, 1.35f };
        /// <summary>The size every pet is brought to at base growth (PetFollower's Normalized Size on the player prefab), before the item's own scale.</summary>
        private const float NormalisedSize = 0.55f;
        private static readonly float[] StageGlow = { 0f, 0.3f, 0.5f, 0.75f, 1f };

        private struct Row
        {
            public ushort Id;
            /// <summary>Asset name of the item, its definition ("PetDef_" + Name) and its icon.</summary>
            public string Asset, Name;
            /// <summary>The model that stands in until the creature's own art exists.</summary>
            public string StandIn;
            /// <summary>Path of the creature's own model, once it has one. It replaces the stand-in.</summary>
            public string Model;
            /// <summary>Paths of its models at 25, 50, 75 and 100 % growth, when a stage is a different creature. Empty or null = the model above at every stage.</summary>
            public string[] StageModels;
            /// <summary>How long its longest side is at 25, 50, 75 and 100 % growth, in metres. Null = the common size steps.</summary>
            public float[] StageSizes;
            /// <summary>The animation in which it charges its owner, and the bone that animation carries forward.</summary>
            public string Charge, ChargeBone;
            /// <summary>What it stands on at 25, 50, 75 and 100 % growth instead of the cloud: the effect's path, or null for the cloud.</summary>
            public string[] StageFeet;
            /// <summary>The animation in which it is not there for a moment, and the stretch of it (shares) during which its body is not drawn.</summary>
            public string Vanish;
            public Vector2 VanishDuring;
            /// <summary>The animation whose first frame is the pose it rests in, for a model whose file holds another pose.</summary>
            public string RestPose;
            /// <summary>Animations followed by another instead of going back to rest: (the animation, how many times it plays, what follows).</summary>
            public (string Animation, int Times, string Then)[] FollowOns;
            /// <summary>What it says in its animations: (the animation, the share of it at which it is said, the words, the stages it is said at).</summary>
            public (string Animation, float At, string Text, Vector2Int Stages)[] Lines;
            /// <summary>It stands instead of bobbing like a drone, on a cloud under each of these bones of its model.</summary>
            public string[] Feet;
            /// <summary>The effects that are this pet's own mark: the effect's prefab, and the bones of the model it is worn on.</summary>
            public Fx[] Effects;
            /// <summary>The true/false parameter of its Animator that holds its one movement loop, if it has no walk and run.</summary>
            public string MoveLoop;
            /// <summary>The true/false parameter that holds the loop it waits in instead of its default state, if it has one.</summary>
            public string RestLoop;
            /// <summary>Its size against the size every pet is brought to (PetFollower's normalised size); 0 = leave the item's as it is.</summary>
            public float Scale;
            public StatType Stat;
            public float Max;
            public string Description;
            /// <summary>Names of the five stages; empty = "Stage N".</summary>
            public string[] Stages;
            /// <summary>Glow colour of the five stages, from the art brief's palette.</summary>
            public string[] Glow;
            /// <summary>Asset and icon names this pet had before, renamed on the first run.</summary>
            public string OldAsset, OldIcon;
        }

        /// <summary>
        /// One effect of a pet's own: the effect's prefab, where it is worn (bones of the model, or a place on the
        /// body moved by an offset in footprints), whether it takes the growth stage's colour, and the animation it
        /// belongs to if it is not worn all the time, with the moments at which it plays or the stretch it plays for.
        /// </summary>
        private struct Fx
        {
            public string Effect;
            public string[] Bones;
            public PetEffectAnchor Anchor;
            public Vector3 Offset;
            public bool StageColour;
            /// <summary>The growth stages it is worn at, first to last (0 to 4). Null = all of them.</summary>
            public Vector2Int? Stages;
            /// <summary>Its size against the pet's footprint; 0 = as it was made.</summary>
            public float Size;
            public string Animation;
            public float[] At;
            public Vector2 During;
            /// <summary>Worn on a bone, it looks down the bone, and its offset is in the bone's frame in metres of the model (z = down the bone).</summary>
            public bool Along;
            /// <summary>Taken away at once when its animation is cut short.</summary>
            public bool Ends;
            /// <summary>With Along: placed in the bone's frame, but facing the way the pet faces.</summary>
            public bool Ahead;
        }

        // The Drone Core's stages: every form, the four grown ones, those with a cannon, the two with a minigun, the last.
        private static readonly Vector2Int Grown = new Vector2Int(1, PetRules.LastStage);
        private static readonly Vector2Int Minigun = new Vector2Int(2, 3);
        private static readonly Vector2Int Gunship = new Vector2Int(PetRules.LastStage, PetRules.LastStage);

        /// <summary>
        /// Joins a model's animations that follow one another into one, in its Animator: the state of the first no
        /// longer goes back to rest when it ends but straight into the next, on the frame it ends on and without a
        /// blend (the clips are made to meet), after it has played through as many times as the table says. The one
        /// that follows loses its own trigger, so it is only ever reached through the one before it. A model that
        /// lacks either state is left alone. Safe to run again: it finds the work done.
        /// </summary>
        private static void JoinAnimations(string modelPath, in Row row)
        {
            var model = string.IsNullOrEmpty(modelPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var animator = model != null ? model.GetComponentInChildren<Animator>(true) : null;
            var controller = animator != null ? animator.runtimeAnimatorController as AnimatorController : null;
            if (controller == null || controller.layers.Length == 0) return;
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            bool changed = false;
            foreach ((string animation, int times, string then) in row.FollowOns)
            {
                AnimatorState from = null, to = null;
                foreach (ChildAnimatorState child in machine.states)
                {
                    if (child.state.name == animation) from = child.state;
                    if (child.state.name == then) to = child.state;
                }
                if (from == null || to == null) continue;
                var clip = from.motion as AnimationClip;
                if (times > 1 && clip != null && !clip.isLooping)
                    Debug.LogWarning($"[Pets] {controller.name}: '{animation}' is played {times} times before '{then}' but its clip does not loop, so it will hold its last frame instead.");

                bool joined = from.transitions.Length == 1 && from.transitions[0].destinationState == to
                    && from.transitions[0].hasExitTime && Mathf.Approximately(from.transitions[0].exitTime, times) && from.transitions[0].duration == 0f;
                if (!joined)
                {
                    foreach (AnimatorStateTransition old in from.transitions) from.RemoveTransition(old);
                    AnimatorStateTransition next = from.AddTransition(to);
                    next.hasExitTime = true;
                    next.exitTime = times; // on a loop, a whole number is that many times through
                    next.hasFixedDuration = true;
                    next.duration = 0f;
                    next.offset = 0f;
                    changed = true;
                }
                foreach (AnimatorStateTransition into in machine.anyStateTransitions)
                {
                    if (into.destinationState != to) continue;
                    machine.RemoveAnyStateTransition(into);
                    changed = true;
                }
                for (int i = controller.parameters.Length - 1; i >= 0; i--)
                {
                    if (controller.parameters[i].name != then || controller.parameters[i].type != AnimatorControllerParameterType.Trigger) continue;
                    controller.RemoveParameter(i);
                    changed = true;
                }
            }
            if (!changed) return;
            EditorUtility.SetDirty(controller);
            Debug.Log($"[Pets] {controller.name}: its animations that follow one another are joined into one.");
        }

        /// <summary>
        /// The Kitsune Spirit's effects, which are spirit fire off its tail tips. Each form has its own tails: one at
        /// first (the tip is the bone DEF-spine), two at 25 % (DEF-tail_4.L and .R), then three, six and nine
        /// (DEF-tail_K_5, counted from its left; the tip is the far end of that bone, so those effects look down the
        /// bone and sit its length along it). Each form keeps the animations of the one before and gains its own:
        /// Shake, Guard and Pounce from 25 %, FoxfireFlick from 50 %, EmberWaltz and Kindle from 75 %, NinefoldBlaze
        /// and SpiritStep at 100 %.
        /// </summary>
        private static Fx[] KitsuneEffects()
        {
            var list = new List<Fx>();
            int[] tailCount = { 1, 2, 3, 6, 9 };
            float[] tipBone = { 0f, 0f, 0.075f, 0.08f, 0.12f }; // the length of the last tail bone, in metres of the model
            for (int stage = 0; stage <= PetRules.LastStage; stage++)
            {
                var here = new Vector2Int(stage, stage);
                int count = tailCount[stage];
                bool along = stage >= 2;
                var tipAt = new Vector3(0f, 0f, tipBone[stage]);
                var tails = new string[count];
                for (int k = 0; k < count; k++)
                    tails[k] = stage == 0 ? "DEF-spine" : stage == 1 ? (k == 0 ? "DEF-tail_4.L" : "DEF-tail_4.R") : $"DEF-tail_{k + 1}_5";
                // On the forms with many tails, what only needs to be suggested is worn by every other tip.
                var some = new List<string>();
                for (int k = 0; k < count; k += count > 3 ? 2 : 1) some.Add(tails[k]);
                string[] fewTails = some.ToArray();
                Fx OnTips(string effect, string[] bones) => new Fx { Effect = effect, Bones = bones, Along = along, Offset = along ? tipAt : Vector3.zero, Stages = here };

                // At rest: a mote off a tail tip now and then.
                Fx motes = OnTips(GameFeelBuilder.SpiritMotesPath, fewTails); motes.Animation = "Idle"; list.Add(motes);
                // Running: a ribbon of fire off the tips.
                Fx run = OnTips(GameFeelBuilder.SpiritTrailPath, fewTails); run.Animation = "Run"; list.Add(run);
                // SpinChase (52 frames): pushes off on 13, the tips draw their rings from 11 to 40, where they close.
                list.Add(new Fx { Effect = GameFeelBuilder.PetHopDustPath, Anchor = PetEffectAnchor.Feet, Animation = "SpinChase", At = Frames(52, 13), Stages = here, Size = 0.5f });
                Fx spin = OnTips(GameFeelBuilder.SpiritTrailPath, tails); spin.Animation = "SpinChase"; spin.During = new Vector2(11f / 52f, 40f / 52f); list.Add(spin);
                Fx closes = OnTips(GameFeelBuilder.SpiritGlintPath, new[] { tails[0] }); closes.Animation = "SpinChase"; closes.At = Frames(52, 40); list.Add(closes);

                if (stage == 0)
                {
                    // The first form: a glint under the paw it holds up (10 to 20 of 36), a ring where its tail curls
                    // (10 of 32), and a glint at its ear each way it looks (14 and 38 of 60).
                    list.Add(new Fx { Effect = GameFeelBuilder.SpiritGlintPath, Bones = new[] { "DEF-front_toe.L" }, Animation = "PawLift", At = Frames(36, 10), Stages = here });
                    list.Add(new Fx { Effect = GameFeelBuilder.SpiritGlintPath, Bones = new[] { "DEF-spine.001" }, Animation = "TailCurl", At = Frames(32, 10), Stages = here, Size = 1.3f });
                    list.Add(new Fx { Effect = GameFeelBuilder.SpiritGlintPath, Bones = new[] { "DEF-ear.L" }, Animation = "LookAround", At = Frames(60, 14), Stages = here, Size = 0.7f });
                    list.Add(new Fx { Effect = GameFeelBuilder.SpiritGlintPath, Bones = new[] { "DEF-ear.R" }, Animation = "LookAround", At = Frames(60, 38), Stages = here, Size = 0.7f });
                    continue;
                }

                // From 25 %. Shake (56 frames): sparks fly off its neck and back from 12 to 38.
                list.Add(new Fx { Effect = GameFeelBuilder.SpiritSprayPath, Bones = new[] { "DEF-spine.006", "DEF-spine.010" }, Animation = "Shake", During = new Vector2(12f / 56f, 38f / 56f), Stages = here });
                // Guard (72 frames): the ward rises as the tails curl (5 to 16), holds to 54, drops by 68; the tips
                // flare as they stop over its back (16) and arcs jump about one of them while it holds.
                list.Add(new Fx { Effect = GameFeelBuilder.SpiritWardPath, Animation = "Guard", At = Frames(72, 5), Stages = here, Ends = true });
                Fx flare = OnTips(GameFeelBuilder.SpiritGlintPath, fewTails); flare.Animation = "Guard"; flare.At = Frames(72, 16); list.Add(flare);
                Fx arcs = OnTips(GameFeelBuilder.SpiritArcsPath, new[] { tails[0] }); arcs.Animation = "Guard"; arcs.During = new Vector2(16f / 72f, 54f / 72f); arcs.Size = 0.045f; arcs.Ends = true; list.Add(arcs);
                // Pounce (56 frames): springs on 20, in the air 21 to 32, front paws land on 32 and hind paws on 36.
                list.Add(new Fx { Effect = GameFeelBuilder.PetHopDustPath, Anchor = PetEffectAnchor.Feet, Offset = new Vector3(0f, 0f, -0.2f), Animation = "Pounce", At = Frames(56, 20, 36), Stages = here, Size = 0.5f });
                Fx arc = OnTips(GameFeelBuilder.SpiritTrailPath, fewTails); arc.Animation = "Pounce"; arc.During = new Vector2(21f / 56f, 34f / 56f); list.Add(arc);
                // The strike itself is comic: a Cartoon FX impact where the front paws land, and the word over it.
                list.Add(new Fx { Effect = GameFeelBuilder.PetPounceMarkPath, Anchor = PetEffectAnchor.Feet, Offset = new Vector3(0f, 0.08f, 0.3f), Animation = "Pounce", At = Frames(56, 32), Stages = here });
                list.Add(new Fx { Effect = GameFeelBuilder.PetPounceWordPath, Anchor = PetEffectAnchor.Top, Offset = new Vector3(0f, 0.35f, 0.2f), Animation = "Pounce", At = Frames(56, 32), Stages = here });
                if (stage < 2) continue;

                // From 50 %. FoxfireFlick (60 frames): all the tails whip up over its back (16 to 22), then each
                // flicks outwards in turn from its left to its right and throws a wisp of foxfire ahead.
                Fx whip = OnTips(GameFeelBuilder.SpiritTrailPath, tails); whip.Animation = "FoxfireFlick"; whip.During = new Vector2(16f / 60f, 30f / 60f); list.Add(whip);
                int[] flicks = stage == 2 ? new[] { 20, 25, 30 } : stage == 3 ? new[] { 20, 22, 24, 26, 28, 30 } : new[] { 20, 21, 22, 24, 25, 26, 28, 29, 30 };
                for (int k = 0; k < count; k++)
                {
                    // Thrown from the very tip of the tail, ahead of the fox.
                    Fx thrown = OnTips(GameFeelBuilder.FoxfireWispPath, new[] { tails[k] }); thrown.Ahead = true; thrown.Animation = "FoxfireFlick"; thrown.At = Frames(60, flicks[k]); list.Add(thrown);
                    Fx lit = OnTips(GameFeelBuilder.SpiritGlintPath, new[] { tails[k] }); lit.Animation = "FoxfireFlick"; lit.At = Frames(60, flicks[k]); lit.Size = 0.6f; list.Add(lit);
                }
                if (stage < 3) continue;

                // From 75 %. EmberWaltz (96 frames): one full turn on its hind legs (18 to 62) in which the tail tips
                // draw a ring of fire; the front paws land on 70 and the ring bursts outwards; embers drift through the bow.
                Fx wheel = OnTips(GameFeelBuilder.SpiritTrailPath, tails); wheel.Animation = "EmberWaltz"; wheel.During = new Vector2(18f / 96f, 62f / 96f); list.Add(wheel);
                list.Add(new Fx { Effect = GameFeelBuilder.FoxfireBurstPath, Animation = "EmberWaltz", At = Frames(96, 70), Stages = here });
                list.Add(new Fx { Effect = GameFeelBuilder.FoxfireEmbersPath, Anchor = PetEffectAnchor.Top, Animation = "EmberWaltz", During = new Vector2(70f / 96f, 90f / 96f), Stages = here, Size = 0.25f });
                // Kindle (108 frames): it sits, brings its leftmost tail round, and blows on the tip (54): a small
                // flame burns there from 55 to 80 and gutters out by 88.
                Fx breath = new Fx { Effect = GameFeelBuilder.FoxfirePuffPath, Bones = new[] { "DEF-spine.011" }, Along = true, Offset = new Vector3(0f, 0f, 0.16f), Animation = "Kindle", At = Frames(108, 54), Stages = here };
                list.Add(breath);
                Fx flame = OnTips(GameFeelBuilder.FoxfireFlamePath, new[] { tails[0] }); flame.Ahead = true; flame.Animation = "Kindle"; flame.During = new Vector2(55f / 108f, 84f / 108f); flame.Ends = true; list.Add(flame);
                if (stage < 4) continue;

                // At 100 %. NinefoldBlaze (132 frames): the tails snap open from the outside in, a beat apart, each
                // lighting three frames after its beat; on 76 a pillar of fire goes up from it and a ring leaves
                // along the ground; embers rain to 124.
                (int frame, int[] pair)[] beats = { (39, new[] { 0, 8 }), (47, new[] { 1, 7 }), (55, new[] { 2, 6 }), (63, new[] { 3, 5 }), (73, new[] { 4 }) };
                foreach ((int frame, int[] pair) in beats)
                {
                    var lit = new string[pair.Length];
                    for (int n = 0; n < pair.Length; n++) lit[n] = tails[pair[n]];
                    Fx beat = OnTips(GameFeelBuilder.SpiritGlintPath, lit); beat.Animation = "NinefoldBlaze"; beat.At = Frames(132, frame); beat.Size = 1.2f; list.Add(beat);
                }
                list.Add(new Fx { Effect = GameFeelBuilder.FoxfirePillarPath, Anchor = PetEffectAnchor.Feet, Animation = "NinefoldBlaze", During = new Vector2(76f / 132f, 98f / 132f), Stages = here });
                list.Add(new Fx { Effect = GameFeelBuilder.FoxfireShockPath, Anchor = PetEffectAnchor.Feet, Animation = "NinefoldBlaze", At = Frames(132, 76), Stages = here });
                list.Add(new Fx { Effect = GameFeelBuilder.PetClapMarkPath, Animation = "NinefoldBlaze", At = Frames(132, 76), Stages = here, Size = 0.5f });
                list.Add(new Fx { Effect = GameFeelBuilder.PetBlazeWordPath, Anchor = PetEffectAnchor.Top, Offset = new Vector3(0f, 0.3f, 0.3f), Animation = "NinefoldBlaze", At = Frames(132, 77), Stages = here });
                list.Add(new Fx { Effect = GameFeelBuilder.FoxfireEmbersPath, Anchor = PetEffectAnchor.Top, Animation = "NinefoldBlaze", During = new Vector2(80f / 132f, 124f / 132f), Stages = here, Size = 0.3f });
                // SpiritStep (48 frames): it lunges and is gone on 12 (a burst where it was), is back on 18 (a burst
                // where it lands) and its tails stream past to 30.
                list.Add(new Fx { Effect = GameFeelBuilder.FoxfireBurstPath, Animation = "SpiritStep", At = Frames(48, 12, 18), Stages = here, Size = 0.6f });
                Fx streams = OnTips(GameFeelBuilder.SpiritTrailPath, fewTails); streams.Animation = "SpiritStep"; streams.During = new Vector2(18f / 48f, 30f / 48f); list.Add(streams);
                // Its six flame tufts burn all the time: a cone of flame stands where each tuft leaves the body and
                // sweeps back along it. Each rides the bone its tuft rides but faces the way the fox faces, so the
                // fire always sweeps behind it. Where each fire stands, in its bone's own frame (x across, y out of the
                // bone, z down it), in metres of the model: MEASURED in the Editor from the artist's positions
                // (README_Kitsune50-100). The shoulder tufts are the largest; the leg fires are worn at 0.7 of them.
                (string bone, Vector3 at, float size)[] tufts =
                {
                    ("DEF-spine.007", new Vector3(-0.033f, 0.103f, 0.050f), 1f), ("DEF-spine.007", new Vector3(0.037f, 0.103f, 0.050f), 1f),
                    ("DEF-front_thigh.L.001", new Vector3(0.020f, 0.050f, 0.090f), 0.7f), ("DEF-front_thigh.R.001", new Vector3(-0.023f, 0.051f, 0.090f), 0.7f),
                    ("DEF-shin.L.001", new Vector3(-0.022f, -0.054f, 0.065f), 0.7f), ("DEF-shin.R.001", new Vector3(0.019f, -0.054f, 0.065f), 0.7f),
                };
                foreach ((string bone, Vector3 at, float size) in tufts)
                    list.Add(new Fx { Effect = GameFeelBuilder.FoxfireTuftPath, Bones = new[] { bone }, Along = true, Ahead = true, Offset = at, Stages = here, Size = size });
            }
            return list.ToArray();
        }

        /// <summary>
        /// The Drone Core's effects. Its data chips at every stage; the camera flash, the scan, the blur of a spin and
        /// the steam of a temper for the forms that have those animations; and from 50 % what its cannon does, each
        /// at the muzzle of that form's own barrel (the cannon bone's length: 0.137, 0.170 and 0.178 m).
        /// </summary>
        private static Fx[] DroneCoreEffects()
        {
            var cannon = new[] { "cannon" };
            var list = new List<Fx>
            {
                // Chips of data dropped behind it as it goes.
                new Fx { Effect = GameFeelBuilder.PetCrumbsPath, Bones = new[] { "body" }, StageColour = true },
                // Taking a photo: the flash goes off on frame 34 of 80.
                new Fx { Effect = GameFeelBuilder.DroneFlashPath, Bones = new[] { "flash" }, Animation = "BarrelTurn", At = Frames(80, 34), Stages = Grown, Size = 0.5f },
                // Looking about: a cone of scan lines out of its lens.
                new Fx { Effect = GameFeelBuilder.DroneScanPath, Bones = new[] { "lens" }, Animation = "EyeLook", Ends = true },
                // Spinning: hoops of streaks round it.
                new Fx { Effect = GameFeelBuilder.DroneWhirlPath, Animation = "AxisSpin", Ends = true },
                new Fx { Effect = GameFeelBuilder.DroneWhirlPath, Animation = "AxisSpinDiagonal", Ends = true },
                // Angry: it lets off steam.
                new Fx { Effect = GameFeelBuilder.DroneSteamPath, Anchor = PetEffectAnchor.Top, Animation = "Mad", During = new Vector2(0.15f, 0.85f), Size = 0.25f },
                // The swarm (100 %): scan lines as the cube bursts (frame 10 of 44) and as it locks again (36 of 46),
                // arcs between the blocks while they are out, sparks on the lock (39 of 46).
                new Fx { Effect = GameFeelBuilder.DroneGlitchPath, Animation = "Disassemble", At = Frames(44, 10), Stages = Gunship },
                new Fx { Effect = GameFeelBuilder.DroneGlitchPath, Animation = "Assemble", At = Frames(46, 36), Stages = Gunship },
                new Fx { Effect = GameFeelBuilder.DroneArcsPath, Animation = "Disassemble", During = new Vector2(10f / 44f, 1f), Stages = Gunship, Size = 0.3f, Ends = true },
                new Fx { Effect = GameFeelBuilder.DroneArcsPath, Animation = "SwarmHover", Stages = Gunship, Size = 0.3f, Ends = true },
                new Fx { Effect = GameFeelBuilder.DroneArcsPath, Animation = "Assemble", During = new Vector2(0.001f, 36f / 46f), Stages = Gunship, Size = 0.3f, Ends = true },
                new Fx { Effect = GameFeelBuilder.DroneLockPath, Animation = "Assemble", At = Frames(46, 39), Stages = Gunship, Size = 0.35f },
            };
            float[] muzzles = { 0.137f, 0.170f, 0.178f };
            for (int stage = 2; stage <= PetRules.LastStage; stage++)
            {
                var here = new Vector2Int(stage, stage);
                var muzzle = new Vector3(0f, 0f, muzzles[stage - 2]);
                // Two shots with recoil, on frames 12 and 30 of 60.
                list.Add(new Fx { Effect = GameFeelBuilder.DroneShotPath, Bones = cannon, Along = true, Offset = muzzle, Animation = "CannonFire", At = Frames(60, 12, 30), Stages = here, Size = 0.3f });
                if (stage <= Minigun.y)
                {
                    // The barrel spins up from frame 1, fires from 14 to 60 of 80 and winds down.
                    var firing = new Vector2(14f / 80f, 60f / 80f);
                    list.Add(new Fx { Effect = GameFeelBuilder.DroneSleevePath, Bones = cannon, Along = true, Animation = "MinigunFire", During = new Vector2(0.001f, 72f / 80f), Stages = here, Ends = true });
                    list.Add(new Fx { Effect = GameFeelBuilder.DroneBurstPath, Bones = cannon, Along = true, Offset = muzzle, Animation = "MinigunFire", During = firing, Stages = here, Size = 0.3f, Ends = true });
                    // The spent rounds, thrown out at the breech for as long as it fires (owner, 11 Oct 2026).
                    list.Add(new Fx { Effect = GameFeelBuilder.DroneShellsPath, Bones = cannon, Along = true, Offset = muzzle * 0.2f, Animation = "MinigunFire", During = firing, Stages = here, Size = 0.7f });
                    // The 75 % form's pod arms kick with each shot of the cannon: a rocket leaves each (owner, 11 Oct 2026).
                    // A pod is 0.33 m long from its bone.
                    // It flies straight for a third of a second and bursts: the burst is played where it has got to,
                    // eight frames after it left (its speed at the effect's size on this form, 0.637 m across).
                    if (stage == 3)
                    {
                        const float rocketSize = 0.45f, podLength = 0.33f, footprint = 0.637f;
                        float flown = GameFeelBuilder.DroneRocketSpeed * GameFeelBuilder.DroneRocketSeconds * rocketSize * footprint;
                        var pods = new[] { "pod.L", "pod.R" };
                        list.Add(new Fx { Effect = GameFeelBuilder.DroneRocketPath, Bones = pods, Along = true, Offset = new Vector3(0f, 0f, podLength), Animation = "CannonFire", At = Frames(60, 12, 30), Stages = here, Size = rocketSize });
                        list.Add(new Fx { Effect = GameFeelBuilder.DroneBoomPath, Bones = pods, Along = true, Offset = new Vector3(0f, 0f, podLength + flown), Animation = "CannonFire", At = Frames(60, 20, 38), Stages = here, Size = 0.2f });
                    }
                }
                else
                {
                    // Eight shots off a spinning barrel, six frames apart from frame 26 of 91.
                    list.Add(new Fx { Effect = GameFeelBuilder.DroneSleevePath, Bones = cannon, Along = true, Animation = "SpinShots", During = new Vector2(0.001f, 72f / 91f), Stages = here, Ends = true });
                    list.Add(new Fx { Effect = GameFeelBuilder.DronePopPath, Bones = cannon, Along = true, Offset = muzzle, Animation = "SpinShots", At = Frames(91, 26, 32, 38, 44, 50, 56, 62, 68), Stages = here, Size = 0.3f });
                    // The fake-out: it gathers power through "Ka... Me... Ha!" (the thrust lands on frame 31 of 130) and
                    // only a puff comes out; the beam is on from frame 72 to 110.
                    list.Add(new Fx { Effect = GameFeelBuilder.DroneChargePath, Bones = cannon, Along = true, Offset = muzzle, Animation = "FakeoutBlast", During = new Vector2(0.001f, 29f / 130f), Stages = here, Size = 0.3f, Ends = true });
                    list.Add(new Fx { Effect = GameFeelBuilder.DroneFizzlePath, Bones = cannon, Along = true, Offset = muzzle, Animation = "FakeoutBlast", At = Frames(130, 31), Stages = here, Size = 0.2f });
                    list.Add(new Fx { Effect = GameFeelBuilder.DroneBeamPath, Bones = cannon, Along = true, Offset = muzzle, Animation = "FakeoutBlast", During = new Vector2(72f / 130f, 110f / 130f), Stages = here, Size = 0.45f, Ends = true });
                }
            }
            return list.ToArray();
        }

        // The Tidecaller's stages: the egg it starts as, and the four swimmers it grows into.
        private static readonly Vector2Int Egg = new Vector2Int(0, 0);
        private static readonly Vector2Int Swimmers = new Vector2Int(1, PetRules.LastStage);

        /// <summary>Frames of a clip <paramref name="last"/> frames long, as shares of it.</summary>
        private static float[] Frames(int last, params int[] frames)
        {
            var shares = new float[frames.Length];
            for (int i = 0; i < frames.Length; i++) shares[i] = frames[i] / (float)last;
            return shares;
        }

        private static readonly Row[] Rows =
        {
            new Row { Id = 8001, Asset = "Pet_Emberwing", Name = "Emberwing", StandIn = "SM_Prop_Drone_Quad_01",
                Model = PetModels + "/Emberwing/Pet_Emberwing_S1.prefab", MoveLoop = "Move", RestLoop = "Fly",
                // Cinders shed from the tips of its wings and the end of its tail.
                Effects = new[] { new Fx { Effect = GameFeelBuilder.PetCindersPath, Bones = new[] { "wing.L.004", "wing.R.004", "tail.001" }, StageColour = true } },
                Stat = StatType.MaxChi, Max = 2000f,
                Description = "A winged mystic beast in lacquer and gold. It grows your Flux as it matures.",
                Stages = new[] { "", "", "", "", "Ascendant" },
                Glow = new[] { "D4AF37", "D4AF37", "D4AF37", "D4AF37", "2E86AB" },
                OldAsset = "Pet_Drone", OldIcon = "Pet_DroneQuad" },
            new Row { Id = 8002, Asset = "Pet_DroneCore", Name = "Drone Core", StandIn = "SM_Prop_Drone_02",
                Model = PetModels + "/DroneCore/Pet_DroneCore_S1.prefab", MoveLoop = "FrontRoll",
                // The four grown forms are models of their own, each with more animations than the one before.
                StageModels = new[]
                {
                    PetModels + "/DroneCore_25/Pet_DroneCore_25.prefab", PetModels + "/DroneCore_50/Pet_DroneCore_50.prefab",
                    PetModels + "/DroneCore_75/Pet_DroneCore_75.prefab", PetModels + "/DroneCore_100/Pet_DroneCore_100.prefab",
                },
                // The longest side of each form with its cannon, pods and fins, at the scale the base cube is shown
                // at (0.30 m as made, 0.25 m in the game): the cubes themselves come out 0.28, 0.31, 0.35 and 0.38 m.
                StageSizes = new[] { 0.28f, 0.47f, 0.53f, 0.63f },
                // The swarm: it comes apart, hovers as blocks for two rounds, and comes together again, as one
                // animation (the states are joined in the 100 % model's Animator; Disassemble starts it).
                FollowOns = new[] { ("Disassemble", 1, "SwarmHover"), ("SwarmHover", 2, "Assemble") },
                // The fake-out, at 24 frames a second in a clip of 130.
                Lines = new[]
                {
                    ("FakeoutBlast", 1f / 130f, "Ka...", Gunship), ("FakeoutBlast", 13f / 130f, "Me...", Gunship), ("FakeoutBlast", 27f / 130f, "Ha!", Gunship),
                    ("FakeoutBlast", 45f / 130f, "...just kidding", Gunship), ("FakeoutBlast", 65f / 130f, "Die!", Gunship),
                },
                Effects = DroneCoreEffects(),
                Scale = 0.45f, // a cube fills its whole box: at the common size it dwarfed its owner
                Stat = StatType.MaxHP, Max = 2200f,
                Description = "A boxy little robot buddy that bolts on plating and cannon pods. It grows your Health as it matures.",
                Stages = new[] { "", "", "", "", "Gunship" },
                Glow = new[] { "4FD1FF", "4FD1FF", "F2C230", "4FD1FF", "4FD1FF" },
                OldAsset = "Pet_DroneHeavy", OldIcon = "Pet_DroneHeavy" },
            new Row { Id = 8003, Asset = "Pet_Tidecaller", Name = "Tidecaller", StandIn = "SM_Prop_Drone_Ball_01",
                Model = PetModels + "/Tidecaller/Pet_Tidecaller_S1.prefab", MoveLoop = "Move",
                // It hatches: from 25 % on it is a swimmer, larger and more armoured at each stage. The four share
                // one rig and one set of animations, which are not the egg's.
                StageModels = new[]
                {
                    PetModels + "/Tidecaller_25/Pet_Tidecaller_25.prefab", PetModels + "/Tidecaller_50/Pet_Tidecaller_50.prefab",
                    PetModels + "/Tidecaller_75/Pet_Tidecaller_75.prefab", PetModels + "/Tidecaller_100/Pet_Tidecaller_100.prefab",
                },
                // The egg is 0.30 m; each form after it is 0.1 m larger than the one before (owner, 10 Oct 2026).
                StageSizes = new[] { 0.4f, 0.5f, 0.6f, 0.7f },
                // The swimmers' Ram: it turns on its owner and butts them.
                Charge = "Ram", ChargeBone = "body",
                // Its effects belong to its animations. Moments and stretches are frames of the clip (24 a second).
                // The first five are the egg's (stage 0), the rest the swimmers' (stages 1 to 4).
                Effects = new[]
                {
                    // A hop: dust where it leaves the floor (frame 7 of 35) and where it comes down (23).
                    new Fx { Effect = GameFeelBuilder.PetHopDustPath, Anchor = PetEffectAnchor.Feet, Animation = "HopSpin", At = Frames(35, 7, 23), Stages = Egg },
                    // The victory bounce comes down three times: frames 17, 32 and 50 of 75.
                    new Fx { Effect = GameFeelBuilder.PetHopDustPath, Anchor = PetEffectAnchor.Feet, Animation = "VictoryBounce", At = Frames(75, 17, 32, 50), Stages = Egg },
                    // Rolling along: dust that runs after it, for as long as the roll plays.
                    new Fx { Effect = GameFeelBuilder.PetRollDustPath, Anchor = PetEffectAnchor.Feet, Animation = "Move", Stages = Egg },
                    // The nap: it dozes from frame 20 to 105 of 149 and starts awake at 110. Zs in a bubble over its top.
                    new Fx { Effect = GameFeelBuilder.PetZzzPath, Anchor = PetEffectAnchor.Top, Offset = new Vector3(0f, 0.72f, 0f),
                        Animation = "WobbleNap", During = new Vector2(20f / 149f, 105f / 149f), Stages = Egg },
                    // The tide call: it leans back from frame 10, heaves forward at 38 and again at 55 of 95. The wave is
                    // called at 21, gathers behind it for 0.6 s, runs past it and breaks in front at 55.
                    new Fx { Effect = GameFeelBuilder.PetTideWavePath, Anchor = PetEffectAnchor.Feet, Animation = "TideCall", At = Frames(95, 21), Stages = Egg },

                    // The swimmers' tide call is the same wave: they rear up from frame 28 of 123 and beat down at 62
                    // and at 81. Called at 47, it gathers behind them while they are reared and breaks in front on the
                    // second beat (34 frames later, as for the egg). The wave was made for the egg's footprint: the
                    // swimmers are half as wide again with their fins out, and at full size it buried their owner's head.
                    new Fx { Effect = GameFeelBuilder.PetTideWavePath, Anchor = PetEffectAnchor.Feet, Animation = "TideCall", At = Frames(123, 47), Stages = Swimmers, Size = 0.55f },
                    // Swimming along: water that parts at the nose, runs back round the body and off behind.
                    new Fx { Effect = GameFeelBuilder.PetTideFlowPath, Anchor = PetEffectAnchor.Body, Animation = "Move", Stages = Swimmers },
                    // The backward somersault turns over between frames 24 and 68 of 99: water is dragged round with it.
                    new Fx { Effect = GameFeelBuilder.PetTideSwirlPath, Anchor = PetEffectAnchor.Body, Animation = "SwimFlip",
                        During = new Vector2(20f / 99f, 70f / 99f), Stages = Swimmers },
                    // The ram lands on frame 22 of 83: a comic bump where its nose meets its owner.
                    new Fx { Effect = GameFeelBuilder.PetRamBumpPath, Bones = new[] { "body" }, Offset = new Vector3(0f, 0.1f, 0.5f),
                        Animation = "Ram", At = Frames(83, 22), Stages = Swimmers, Size = 0.55f }, // a little bump, even on the largest

                    // The three animations the later swimmers gained (12 Oct 2026), made of the same water: the
                    // flow's ribbons, the wave's shards, and the tide call's own wave. ShellSpin from 50 %, Undertow
                    // from 75 %, Maelstrom at 100 %.
                    // ShellSpin (100 frames): it lets go on 16, spins to 58, throws its fins out on 60.
                    new Fx { Effect = GameFeelBuilder.TideTornadoPath, Anchor = PetEffectAnchor.Feet, Animation = "ShellSpin", During = new Vector2(16f / 100f, 57f / 100f), Stages = new Vector2Int(2, PetRules.LastStage) },
                    new Fx { Effect = GameFeelBuilder.TideFlingPath, Animation = "ShellSpin", At = Frames(100, 60), Stages = new Vector2Int(2, PetRules.LastStage) },
                    // Undertow (120 frames): corkscrews down 26 to 50, hangs to 58, the great stroke on 59, brakes on 80.
                    new Fx { Effect = GameFeelBuilder.TideFunnelPath, Bones = new[] { "body" }, Animation = "Undertow", During = new Vector2(26f / 120f, 50f / 120f), Stages = new Vector2Int(3, PetRules.LastStage) },
                    new Fx { Effect = GameFeelBuilder.TideBubblesPath, Bones = new[] { "body" }, Animation = "Undertow", During = new Vector2(50f / 120f, 58f / 120f), Stages = new Vector2Int(3, PetRules.LastStage), Size = 0.3f },
                    new Fx { Effect = GameFeelBuilder.TideGeyserPath, Anchor = PetEffectAnchor.Feet, Animation = "Undertow", At = Frames(120, 59), Stages = new Vector2Int(3, PetRules.LastStage) },
                    new Fx { Effect = GameFeelBuilder.PetWhooshWordPath, Anchor = PetEffectAnchor.Top, Offset = new Vector3(0.5f, 0.5f, 0f), Animation = "Undertow", At = Frames(120, 61), Stages = new Vector2Int(3, PetRules.LastStage) },
                    new Fx { Effect = GameFeelBuilder.TideSprayPath, Bones = new[] { "body" }, Animation = "Undertow", At = Frames(120, 80), Stages = new Vector2Int(3, PetRules.LastStage) },
                    // Maelstrom (138 frames): gathers 36 to 72, the clap on 78. The wave is the tide call's, which
                    // breaks 34 frames after it is called: called on 44 and at full size, it breaks on the clap.
                    new Fx { Effect = GameFeelBuilder.TideGatherPath, Anchor = PetEffectAnchor.Feet, Animation = "Maelstrom", During = new Vector2(36f / 138f, 70f / 138f), Stages = new Vector2Int(4, 4) },
                    new Fx { Effect = GameFeelBuilder.TideArcsPath, Bones = new[] { "body" }, Animation = "Maelstrom", During = new Vector2(60f / 138f, 76f / 138f), Stages = new Vector2Int(4, 4), Size = 0.1f, Ends = true },
                    new Fx { Effect = GameFeelBuilder.PetTideWavePath, Anchor = PetEffectAnchor.Feet, Animation = "Maelstrom", At = Frames(138, 44), Stages = new Vector2Int(4, 4) },
                    new Fx { Effect = GameFeelBuilder.TideShockPath, Offset = new Vector3(0f, 0f, 0.5f), Animation = "Maelstrom", At = Frames(138, 78), Stages = new Vector2Int(4, 4) },
                    // The clap is comic too: a Cartoon FX ring of puffs round it, and the word over it.
                    new Fx { Effect = GameFeelBuilder.PetClapMarkPath, Offset = new Vector3(0f, 0f, 0.5f), Animation = "Maelstrom", At = Frames(138, 78), Stages = new Vector2Int(4, 4) },
                    new Fx { Effect = GameFeelBuilder.PetClapWordPath, Anchor = PetEffectAnchor.Top, Offset = new Vector3(0f, 0.6f, 0.4f), Animation = "Maelstrom", At = Frames(138, 79), Stages = new Vector2Int(4, 4) },
                    new Fx { Effect = GameFeelBuilder.TideLightningPath, Anchor = PetEffectAnchor.Feet, Offset = new Vector3(0f, 0f, 1f), Animation = "Maelstrom", At = Frames(138, 78, 81), Stages = new Vector2Int(4, 4), Size = 0.25f },
                    new Fx { Effect = GameFeelBuilder.TideSprayPath, Anchor = PetEffectAnchor.Top, Animation = "Maelstrom", At = Frames(138, 84), Stages = new Vector2Int(4, 4) },
                },
                Scale = 0.55f, // an egg is nearly as wide as it is tall: at the common size it was as big as its owner's chest
                Stat = StatType.DefensePower, Max = 2200f,
                Description = "A pocket-sized kaiju that never outgrows its owner's shoulder. It grows your Defense as it matures.",
                Stages = new[] { "Spawnling", "", "", "", "Apex" },
                Glow = new[] { "5AD8C6", "5AD8C6", "5AD8C6", "3FA7D6", "3FA7D6" },
                OldAsset = "Pet_DroneBall", OldIcon = "Pet_DroneBall" },
            new Row { Id = 8004, Asset = "Pet_KitsuneSpirit", Name = "Kitsune Spirit", StandIn = "SM_Prop_Drone_Insect_01",
                Model = PetModels + "/KitsuneSpirit/Pet_KitsuneSpirit_S1.prefab",
                // A fox of its own at every stage: two tails at 25 %, three at 50 %, six at 75 %, nine at 100 %.
                StageModels = new[]
                {
                    PetModels + "/KitsuneSpirit_25/Pet_KitsuneSpirit_25.prefab", PetModels + "/KitsuneSpirit_50/Pet_KitsuneSpirit_50.prefab",
                    PetModels + "/KitsuneSpirit_75/Pet_KitsuneSpirit_75.prefab", PetModels + "/KitsuneSpirit_100/Pet_KitsuneSpirit_100.prefab",
                },
                // The longest side of each form, tails and all, at the scale the first fox is shown at (0.66 m as
                // made, 0.55 m in the game): the artist's sizes, in which each body is 0.1 m longer than the last.
                StageSizes = new[] { 0.60f, 0.77f, 0.80f, 1.0f },
                // The two largest stand on fire instead of clouds, worn the same way under each paw (owner, 12 Oct 2026).
                StageFeet = new[] { null, null, GameFeelBuilder.FoxfirePawsPath, GameFeelBuilder.FoxfirePawsPath },
                // SpiritStep (48 frames, 100 % only): it is not there from frame 12 to 18.
                Vanish = "SpiritStep", VanishDuring = new Vector2(12f / 48f, 18f / 48f),
                Feet = new[] { "DEF-front_toe.L", "DEF-front_toe.R", "DEF-toe.L", "DEF-toe.R" },
                Effects = KitsuneEffects(),
                Stat = StatType.AttackPower, Max = 1100f,
                Description = "A fox spirit wrapped in paper talismans, gaining tails as it grows. It grows your Attack as it matures.",
                Stages = new[] { "One Tail", "Three Tails", "Five Tails", "Seven Tails", "Nine Tails" },
                Glow = new[] { "FF8A3D", "FF8A3D", "FF8A3D", "FF8A3D", "FFD23F" },
                OldAsset = "Pet_DroneInsect", OldIcon = "Pet_DroneInsect" },
        };

        /// <summary>Pets that no longer exist: (item asset, icon). Their item ids (8005) are never handed out again.</summary>
        private static readonly (string Asset, string Icon)[] Retired = { ("Pet_DroneQuad", "Pet_Drone") };

        [MenuItem("Krytical Nova/Items/7. Build Pets")]
        public static void Build()
        {
            WeatherVfxBuilder.EnsureFolder(PetsRoot);
            WeatherVfxBuilder.EnsureFolder(ItemFolder);

            var settings = LoadOrCreate<PetSettings>(SettingsPath);
            if (settings.auraPrefab == null)
            {
                settings.auraPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameFeelBuilder.PetGlowPath);
                if (settings.auraPrefab == null)
                    Debug.LogWarning("[Pets] No pet glow effect yet. Run Krytical Nova/GameFeel/Rebuild Pet Feel, then build the pets again.");
                EditorUtility.SetDirty(settings);
            }
            var items = new List<ItemDefinition>(Rows.Length);
            var definitions = new List<PetDefinition>(Rows.Length);
            foreach (Row row in Rows)
            {
                ItemDefinition item = Item(row);
                items.Add(item);
                definitions.Add(Definition(row));
            }

            var removed = new List<ItemDefinition>();
            foreach ((string asset, string icon) in Retired)
            {
                string itemPath = $"{ItemFolder}/{asset}.asset";
                var gone = AssetDatabase.LoadAssetAtPath<ItemDefinition>(itemPath);
                if (gone != null)
                {
                    removed.Add(gone);
                    AssetDatabase.DeleteAsset(itemPath);
                }
                string iconPath = $"{ItemIconCapture.IconFolder}/{icon}.png";
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath) != null && !items.Any(i => AssetDatabase.GetAssetPath(i.icon) == iconPath))
                    AssetDatabase.DeleteAsset(iconPath);
            }

            var catalog = LoadOrCreate<PetCatalog>(CatalogPath);
            catalog.SetContents(definitions, settings);
            EditorUtility.SetDirty(catalog);

            RegisterItems(items, removed);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Pets] {items.Count} pets built: {string.Join(", ", Rows.Select(r => r.Name))}. " +
                      "Run Krytical Nova/Setup/Player Prefab if the player has no PlayerPet yet.");
        }

        private static ItemDefinition Item(in Row row)
        {
            string path = $"{ItemFolder}/{row.Asset}.asset";
            string oldPath = $"{ItemFolder}/{row.OldAsset}.asset";
            if (AssetDatabase.LoadAssetAtPath<ItemDefinition>(path) == null && AssetDatabase.LoadAssetAtPath<ItemDefinition>(oldPath) != null)
            {
                string error = AssetDatabase.RenameAsset(oldPath, row.Asset);
                if (!string.IsNullOrEmpty(error)) Debug.LogError($"[Pets] Could not rename {oldPath}: {error}");
            }

            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<ItemDefinition>();
                item.itemId = row.Id;
                item.petScale = 1f; // multiplier on PetFollower's normalised size
                AssetDatabase.CreateAsset(item, path);
            }
            if (item.itemId != row.Id)
                Debug.LogError($"[Pets] {path} has item id {item.itemId}, the table says {row.Id}. Ids are never renumbered: fix the table or the asset.", item);

            item.displayName = row.Name;
            item.description = row.Description;
            item.rarity = ItemRarity.Common;
            item.equipSlot = EquipSlot.Pet;
            item.maxStack = 1;
            item.itemLevel = 0; // any level may use a pet
            if (row.Scale > 0f) item.petScale = row.Scale;
            string modelPath = string.IsNullOrEmpty(row.Model) ? Drones + row.StandIn + ".prefab" : row.Model;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model != null) item.petPrefab = model;
            else Debug.LogError($"[Pets] Model {modelPath} not found for {row.Name}.");

            Sprite icon = Icon(row);
            if (icon != null) item.icon = icon;
            EditorUtility.SetDirty(item);
            return item;
        }

        /// <summary>The pet's icon under its own name, renamed from the icon of the model that stands in for it.</summary>
        private static Sprite Icon(in Row row)
        {
            string path = $"{ItemIconCapture.IconFolder}/{row.Asset}.png";
            string oldPath = $"{ItemIconCapture.IconFolder}/{row.OldIcon}.png";
            if (AssetDatabase.LoadAssetAtPath<Sprite>(path) == null && AssetDatabase.LoadAssetAtPath<Sprite>(oldPath) != null)
            {
                string error = AssetDatabase.RenameAsset(oldPath, row.Asset);
                if (!string.IsNullOrEmpty(error)) Debug.LogError($"[Pets] Could not rename {oldPath}: {error}");
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static PetDefinition Definition(in Row row)
        {
            string path = $"{PetsRoot}/PetDef_{row.Asset.Substring("Pet_".Length)}.asset";
            var definition = AssetDatabase.LoadAssetAtPath<PetDefinition>(path);
            bool created = definition == null;
            if (created)
            {
                definition = ScriptableObject.CreateInstance<PetDefinition>();
                definition.stat = row.Stat;
                definition.maxValue = row.Max;
                AssetDatabase.CreateAsset(definition, path);
            }
            definition.itemId = row.Id;
            definition.displayName = row.Name;
            definition.description = row.Description;
            definition.moveLoop = row.MoveLoop ?? "";
            definition.restLoop = row.RestLoop ?? "";
            definition.chargeAnimation = row.Charge ?? "";
            definition.chargeBone = row.ChargeBone ?? "";
            definition.vanishAnimation = row.Vanish ?? "";
            definition.vanishDuring = row.VanishDuring;
            definition.restPose = row.RestPose ?? "";
            int followCount = row.FollowOns != null ? row.FollowOns.Length : 0;
            definition.followOns = new PetFollowOn[followCount];
            for (int i = 0; i < followCount; i++)
                definition.followOns[i] = new PetFollowOn { animation = row.FollowOns[i].Animation, times = row.FollowOns[i].Times, then = row.FollowOns[i].Then };
            if (followCount > 0)
            {
                JoinAnimations(row.Model, row);
                if (row.StageModels != null)
                    foreach (string stageModel in row.StageModels) JoinAnimations(stageModel, row);
            }
            int lineCount = row.Lines != null ? row.Lines.Length : 0;
            definition.lines = new PetLine[lineCount];
            for (int i = 0; i < lineCount; i++)
                definition.lines[i] = new PetLine { animation = row.Lines[i].Animation, at = row.Lines[i].At, text = row.Lines[i].Text, stages = row.Lines[i].Stages };
            bool stands = row.Feet != null && row.Feet.Length > 0;
            definition.bobs = !stands;
            if (stands)
            {
                definition.footBones = row.Feet;
                definition.footEffect = AssetDatabase.LoadAssetAtPath<GameObject>(GameFeelBuilder.PetCloudPath);
                if (definition.footEffect == null)
                    Debug.LogWarning($"[Pets] No pet cloud effect yet for {row.Name}. Run Krytical Nova/GameFeel/Rebuild Pet Cloud, then build the pets again.");
            }

            // Its own effects are the table's: a pet with none in the table wears none.
            int effectCount = row.Effects != null ? row.Effects.Length : 0;
            definition.effects = new PetEffect[effectCount];
            for (int i = 0; i < effectCount; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(row.Effects[i].Effect);
                if (prefab == null)
                    Debug.LogWarning($"[Pets] No effect at {row.Effects[i].Effect} for {row.Name}. Run Krytical Nova/GameFeel/Rebuild Pet Effects, then build the pets again.");
                Fx fx = row.Effects[i];
                definition.effects[i] = new PetEffect
                {
                    prefab = prefab,
                    bones = fx.Bones ?? new string[0],
                    anchor = fx.Anchor,
                    offset = fx.Offset,
                    stageColour = fx.StageColour,
                    stages = fx.Stages ?? new Vector2Int(0, PetRules.LastStage),
                    size = fx.Size > 0f ? fx.Size : 1f,
                    animation = fx.Animation ?? "",
                    at = fx.At ?? new float[0],
                    during = fx.During,
                    alongBone = fx.Along,
                    facesAhead = fx.Ahead,
                    endsWithAnimation = fx.Ends,
                };
            }

            if (definition.stages == null || definition.stages.Length != PetRules.StageCount)
                definition.stages = new PetStageLook[PetRules.StageCount];
            for (int stage = 0; stage < PetRules.StageCount; stage++)
            {
                PetStageLook look = definition.stages[stage];
                look.name = row.Stages[stage];
                // A stage that is a creature of its own: the table's, like the pet's model.
                if (stage > 0 && row.StageModels != null && stage - 1 < row.StageModels.Length && !string.IsNullOrEmpty(row.StageModels[stage - 1]))
                {
                    var stageModel = AssetDatabase.LoadAssetAtPath<GameObject>(row.StageModels[stage - 1]);
                    if (stageModel != null) look.model = stageModel;
                    else Debug.LogError($"[Pets] Stage model {row.StageModels[stage - 1]} not found for {row.Name}.");
                }
                // A size in metres is the table's too: the stage's scale is that size against the pet's base size.
                if (stage > 0 && row.StageSizes != null && stage - 1 < row.StageSizes.Length && row.StageSizes[stage - 1] > 0f)
                    look.scale = row.StageSizes[stage - 1] / (NormalisedSize * (row.Scale > 0f ? row.Scale : 1f));
                // What a stage stands on instead of the pet's own foot effect is the table's as well.
                string feet = stage > 0 && row.StageFeet != null && stage - 1 < row.StageFeet.Length ? row.StageFeet[stage - 1] : null;
                look.footEffect = string.IsNullOrEmpty(feet) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(feet);
                if (!string.IsNullOrEmpty(feet) && look.footEffect == null)
                    Debug.LogWarning($"[Pets] No foot effect at {feet} for {row.Name}. Run Krytical Nova/GameFeel/Rebuild Kitsune Foxfire Effects, then build the pets again.");
                if (look.scale <= 0f)
                {
                    // A stage that was never set up: the stand-in look of the art brief.
                    look.scale = StageScales[stage];
                    look.glow = ColorBible.Hex(row.Glow[stage]);
                    look.glowStrength = StageGlow[stage];
                }
                definition.stages[stage] = look;
            }
            EditorUtility.SetDirty(definition);
            return definition;
        }

        /// <summary>Pets are items a player owns: they belong in the item catalog, and retired ones leave it.</summary>
        private static void RegisterItems(List<ItemDefinition> pets, List<ItemDefinition> removed)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(SidekickGearGenerator.CatalogPath);
            if (catalog == null)
            {
                Debug.LogWarning("[Pets] No item catalog yet: run Krytical Nova/Items/1 and 6, then build the pets again.");
                return;
            }
            var items = catalog.Items.Where(i => i != null && !removed.Contains(i)).Concat(pets).Distinct().OrderBy(i => i.itemId).ToList();
            var equipped = catalog.StarterEquipped.Where(i => i != null && !removed.Contains(i)).ToList();
            var bag = catalog.StarterBag.Where(s => s.item != null && !removed.Contains(s.item)).ToList();
            catalog.SetContents(items, equipped, bag);
            EditorUtility.SetDirty(catalog);
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
