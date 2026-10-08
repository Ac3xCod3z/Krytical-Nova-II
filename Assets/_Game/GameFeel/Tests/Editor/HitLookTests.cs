using System.Collections.Generic;
using KryticalNova.GameFeel;
using KryticalNova.Networking;
using KryticalNova.Progression;
using MoreMountains.Feedbacks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// How hits look: the rules (which kind a hit is shown as, which word it gets, when a word is held back), and
    /// the effects as they stand in the project: every weapon class has a mark and three words of its own, no
    /// comic effect is shared between two moments, and the look of a critical hit on another player is used for
    /// nothing else. Also what keeps them readable and cheap: they are lettering, drawn over the inked frame; a
    /// mark is on screen long enough to be seen; the pool takes an effect back when its particles are done.
    /// </summary>
    public sealed class HitLookTests
    {
        private const string SettingsPath = "Assets/_Game/GameFeel/Data/Assets/HitLookSettings.asset";
        private const string FeelPrefabPath = "Assets/_Game/GameFeel/Prefabs/PFX_GameFeel.prefab";
        private const string ComicFolder = "Assets/_Game/VFX/GameFeel/Comic/";
        private const string PackFolder = "Assets/_ThirdParty/";
        private const string RendererPath = "Assets/_Game/Rendering/Settings/PC_Renderer.asset";
        private const string LetteringLayer = "ComicLettering";

        private HitLookSettings looks;
        private GameObject feel;

        [OneTimeSetUp]
        public void Load()
        {
            looks = AssetDatabase.LoadAssetAtPath<HitLookSettings>(SettingsPath);
            feel = AssetDatabase.LoadAssetAtPath<GameObject>(FeelPrefabPath);
            Assert.NotNull(looks, SettingsPath);
            Assert.NotNull(feel, FeelPrefabPath);
        }

        // ---------------------------------------------------------------- the rules

        [Test]
        public void TheRarestThingAboutAHit_IsTheKindItIsShownAs()
        {
            Assert.AreEqual(HitKind.Normal, HitLooks.KindOf(false, false, false, false));
            Assert.AreEqual(HitKind.Elemental, HitLooks.KindOf(false, false, false, true));
            Assert.AreEqual(HitKind.Critical, HitLooks.KindOf(true, false, false, true), "critical before Elemental");
            Assert.AreEqual(HitKind.Shattered, HitLooks.KindOf(true, false, true, true), "a barrier broken before critical");
            Assert.AreEqual(HitKind.Reflected, HitLooks.KindOf(true, true, true, true), "thrown back before everything");
        }

        [Test]
        public void ALightHit_IsOnlyTheOrdinaryStrikesOfACombo()
        {
            Assert.IsFalse(HitLooks.IsHeavy(false, 1, HitKind.Normal));
            Assert.IsFalse(HitLooks.IsHeavy(false, 2, HitKind.Elemental));
            Assert.IsTrue(HitLooks.IsHeavy(false, 3, HitKind.Normal), "the finisher, and every skill");
            Assert.IsTrue(HitLooks.IsHeavy(true, 1, HitKind.Critical));
            Assert.IsTrue(HitLooks.IsHeavy(false, 1, HitKind.Shattered));
            Assert.IsTrue(HitLooks.IsHeavy(false, 1, HitKind.Reflected));
        }

        [Test]
        public void AHitsWord_IsCritical_ThenTheSignatureSkills_ThenHeavy_AndNoneForALightHit()
        {
            Assert.AreEqual(HitWord.None, HitLooks.WordFor(false, 1, ClassSkillSlot.None));
            Assert.AreEqual(HitWord.None, HitLooks.WordFor(false, 2, ClassSkillSlot.None));
            Assert.AreEqual(HitWord.Heavy, HitLooks.WordFor(false, 3, ClassSkillSlot.None), "the combo's finisher");
            Assert.AreEqual(HitWord.Heavy, HitLooks.WordFor(false, 3, ClassSkillSlot.S3), "a skill that is not a signature one");
            foreach (ClassSkillSlot slot in new[] { ClassSkillSlot.S4, ClassSkillSlot.S6, ClassSkillSlot.S8 })
                Assert.AreEqual(HitWord.Skill, HitLooks.WordFor(false, 3, slot), slot.ToString());
            foreach (ClassSkillSlot slot in new[] { ClassSkillSlot.S1, ClassSkillSlot.S2, ClassSkillSlot.S5, ClassSkillSlot.S7, ClassSkillSlot.Stance, ClassSkillSlot.Guard })
                Assert.AreNotEqual(HitWord.Skill, HitLooks.WordFor(false, 3, slot), slot.ToString());
            Assert.AreEqual(HitWord.Critical, HitLooks.WordFor(true, 1, ClassSkillSlot.None));
            Assert.AreEqual(HitWord.Critical, HitLooks.WordFor(true, 3, ClassSkillSlot.S8), "a critical hit says so, whatever dealt it");
        }

        [Test]
        public void OneAttacker_ShowsOneWordAtATime_ButACriticalOneIsNotHeldBackByAnOrdinaryOne()
        {
            const float gap = 0.45f;
            Assert.IsTrue(HitLooks.GapAllows(HitWord.Heavy, 10f, float.NegativeInfinity, HitWord.None, gap), "the first word");
            Assert.IsFalse(HitLooks.GapAllows(HitWord.Heavy, 10.2f, 10f, HitWord.Heavy, gap), "a three-hit skill says its word once");
            Assert.IsFalse(HitLooks.GapAllows(HitWord.Skill, 10.2f, 10f, HitWord.Heavy, gap));
            Assert.IsTrue(HitLooks.GapAllows(HitWord.Heavy, 10.5f, 10f, HitWord.Heavy, gap), "the gap has passed");
            Assert.IsTrue(HitLooks.GapAllows(HitWord.Critical, 10.1f, 10f, HitWord.Heavy, gap), "the critical word comes through");
            Assert.IsFalse(HitLooks.GapAllows(HitWord.Critical, 10.1f, 10f, HitWord.Critical, gap), "but not two critical words on top of each other");
            Assert.IsFalse(HitLooks.GapAllows(HitWord.None, 20f, 10f, HitWord.None, gap));
        }

        [Test]
        public void TheLocalPlayersWords_AreAlwaysShown_OthersOnlyNearAndOnlySoMany()
        {
            Assert.IsTrue(HitLooks.BudgetAllows(true, 500f, 99, 5, 26f), "the local player's own fight");
            Assert.IsTrue(HitLooks.BudgetAllows(false, 10f, 4, 5, 26f));
            Assert.IsFalse(HitLooks.BudgetAllows(false, 10f, 5, 5, 26f), "enough words of other fights are up");
            Assert.IsFalse(HitLooks.BudgetAllows(false, 27f, 0, 5, 26f), "too far to read");
            Assert.IsFalse(HitLooks.BudgetAllows(false, 10f, 0, 0, 26f), "none wanted");
        }

        [Test]
        public void TheLookOfItsOwn_IsForACriticalHitByOnePlayerOnAnother_AndNothingElse()
        {
            Assert.IsTrue(HitLooks.IsPvpCrit(true, false, true, true, false));
            Assert.IsFalse(HitLooks.IsPvpCrit(false, false, true, true, false), "not critical");
            Assert.IsFalse(HitLooks.IsPvpCrit(true, true, true, true, false), "thrown back: no damage was done");
            Assert.IsFalse(HitLooks.IsPvpCrit(true, false, false, true, false), "a creature's critical hit");
            Assert.IsFalse(HitLooks.IsPvpCrit(true, false, true, false, false), "a critical hit on a creature or a dummy");
            Assert.IsFalse(HitLooks.IsPvpCrit(true, false, true, true, true), "a hit on oneself (a GM preview)");
        }

        // ---------------------------------------------------------------- the effects as they stand

        [Test]
        public void EveryWeaponClass_HasAMarkAndThreeWords()
        {
            foreach (WeaponClassId id in WeaponClasses.All)
            {
                WeaponHitLook look = looks.Look(id);
                Assert.AreEqual(id, look.weaponClass, id + " falls back to the unarmed look");
                AssertComic(look.lightImpact, id + " light mark");
                AssertComic(look.heavyImpact, id + " heavy mark");
                AssertComic(look.heavyWord, id + " heavy word");
                AssertComic(look.criticalWord, id + " critical word");
                AssertComic(look.skillWord, id + " skill word");
                Assert.Greater(look.swing.a, 0f, id + " has no colour for its blade trail");
            }
            AssertComic(looks.unarmed.lightImpact, "unarmed light mark");
            AssertComic(looks.unarmed.heavyImpact, "unarmed heavy mark");
            Assert.AreSame(looks.unarmed, looks.Look(WeaponClassId.None));
            AssertComic(looks.reflectedWord, "the word of a blow thrown back");
            AssertComic(looks.shatteredWord, "the word of a barrier broken");
        }

        [Test]
        public void NoTwoWeaponClasses_SwingTheSameColour()
        {
            for (int a = 0; a < looks.classes.Length; a++)
                for (int b = a + 1; b < looks.classes.Length; b++)
                    Assert.Greater(Distance(looks.classes[a].swing, looks.classes[b].swing), 0.15f,
                        $"{looks.classes[a].weaponClass} and {looks.classes[b].weaponClass}");
        }

        [Test]
        public void EveryKindOfHit_HasColoursOfItsOwn()
        {
            HitKind[] kinds = { HitKind.Normal, HitKind.Elemental, HitKind.Critical, HitKind.Shattered, HitKind.Reflected };
            for (int a = 0; a < kinds.Length; a++)
                for (int b = a + 1; b < kinds.Length; b++)
                {
                    ComicRamp first = looks.Ramp(kinds[a]), second = looks.Ramp(kinds[b]);
                    // A word's letters are the fill colour: that is what is read first.
                    Assert.Greater(Distance(first.fill, second.fill), 0.2f, $"{kinds[a]} and {kinds[b]} have the same main colour");
                }
        }

        [Test]
        public void NoComicEffect_IsSharedBetweenTwoMoments()
        {
            var usedBy = new Dictionary<GameObject, HashSet<string>>();
            void Use(GameObject effect, string moment)
            {
                if (effect == null) return;
                if (!usedBy.TryGetValue(effect, out HashSet<string> moments)) usedBy[effect] = moments = new HashSet<string>();
                moments.Add(moment);
            }

            foreach (WeaponHitLook look in looks.classes)
            {
                Use(look.lightImpact, look.weaponClass + ": a light hit");
                Use(look.heavyImpact, look.weaponClass + ": a heavy hit");
                Use(look.heavyWord, look.weaponClass + ": a heavy hit's word");
                Use(look.criticalWord, look.weaponClass + ": a critical hit's word");
                Use(look.skillWord, look.weaponClass + ": a signature skill's word");
            }
            Use(looks.unarmed.lightImpact, "unarmed: a light hit");
            Use(looks.unarmed.heavyImpact, "unarmed: a heavy hit");
            Use(looks.reflectedWord, "a blow thrown back");
            Use(looks.shatteredWord, "a barrier broken");
            foreach (PvpCritLook crit in looks.pvpCrit)
            {
                foreach (GameObject panel in crit.panels) Use(panel, "a critical hit on another player (" + crit.style + ")");
                Use(crit.lines, "a critical hit on another player (lines)");
            }
            // The players of the game feel prefab. The two deaths are one moment, and so are the three pick-ups.
            foreach (MMF_Player player in feel.GetComponentsInChildren<MMF_Player>(true))
            {
                string moment = player.name;
                if (moment == "Critical Death") moment = "Death";
                if (moment.StartsWith("Pick Up")) moment = "Pick Up";
                foreach (MMF_Feedback feedback in player.FeedbacksList)
                    if (feedback is MMF_PooledVfx vfx && vfx.Prefab != null && vfx.Prefab.GetComponent<ComicFx>() != null)
                        Use(vfx.Prefab, "the game feel's " + moment);
            }

            Assert.Greater(usedBy.Count, 60, "the comic effects are handed out");
            foreach (KeyValuePair<GameObject, HashSet<string>> each in usedBy)
                Assert.AreEqual(1, each.Value.Count, $"{each.Key.name} is used for: {string.Join("; ", each.Value)}");
        }

        [Test]
        public void TheCriticalHitOnAPlayer_HasThreeLooks_AndEachIsItsOwn()
        {
            var seen = new HashSet<GameObject>();
            foreach (PvpCritStyle style in new[] { PvpCritStyle.SplashPanel, PvpCritStyle.ImpactFrame, PvpCritStyle.FactionStrike })
            {
                PvpCritLook crit = looks.PvpCrit(style);
                Assert.NotNull(crit, style.ToString());
                Assert.AreEqual(style, crit.style, style + " is missing: another look stands in for it");
                Assert.Greater(crit.panels.Length, 0, style.ToString());
                foreach (GameObject panel in crit.panels)
                {
                    AssertComic(panel, style + " panel");
                    Assert.IsTrue(seen.Add(panel), panel.name + " is the panel of two looks");
                }
            }
            Assert.AreEqual(3, looks.PvpCrit(PvpCritStyle.FactionStrike).panels.Length, "one panel a faction: Hegemony, Null, Covenant");
            Assert.NotNull(looks.PvpCrit(), "the look in use");
        }

        [Test]
        public void ComicEffects_AreOurCopies_ShortEnoughToGoBackToThePool()
        {
            int checkedEffects = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ComicFolder.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var effect = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var comic = effect.GetComponent<ComicFx>();
                Assert.NotNull(comic, path + " has no ComicFx");
                Assert.Greater(comic.PartCount, 0, path + " has no part that takes a colour");
                foreach (MonoBehaviour behaviour in effect.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    Assert.NotNull(behaviour, path + " has a missing script");
                    string space = behaviour.GetType().Namespace ?? string.Empty;
                    Assert.IsFalse(space.StartsWith("CTFX2") || space.StartsWith("PSD"), path + " still carries the pack's authoring script " + behaviour.GetType().Name);
                }
                foreach (ParticleSystem system in effect.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ParticleSystem.MainModule main = system.main;
                    Assert.IsFalse(main.loop, path + ": " + system.name + " loops");
                    Assert.Less(main.duration + main.startDelay.constantMax + main.startLifetime.constantMax, 4f,
                        path + ": " + system.name + " keeps the effect out of the pool for seconds");
                    Assert.IsFalse(system.collision.enabled, path + ": " + system.name + " collides with the world");
                }
                // A word's letters each have a material: those are ours, never the pack's (the pack's tool rewrites them).
                if (effect.name.StartsWith("PFX_Word_") || effect.name.StartsWith("PFX_PvpCrit_"))
                    foreach (ParticleSystemRenderer renderer in effect.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    {
                        if (renderer.sharedMaterial == null) continue;
                        StringAssert.DoesNotStartWith(PackFolder, AssetDatabase.GetAssetPath(renderer.sharedMaterial), path + ": " + renderer.name);
                    }
                checkedEffects++;
            }
            Assert.Greater(checkedEffects, 60);
        }

        [Test]
        public void TheGameFeel_HandsOutTheHitLooks_AndHasThePlayersOfTheNewMoments()
        {
            var presenter = feel.GetComponent<GameFeelPresenter>();
            Assert.NotNull(presenter);
            var so = new SerializedObject(presenter);
            Assert.AreSame(looks, so.FindProperty("looks").objectReferenceValue, "the presenter's hit looks");
            foreach (string field in new[] { "pvpCritFeel", "levelUpFeel", "pickupFeel", "levelUpWorldFeel", "pickupWorldFeel", "rarePickupWorldFeel", "epicPickupWorldFeel",
                         "hitWorldFeel", "heavyHitWorldFeel" })
                Assert.NotNull(so.FindProperty(field).objectReferenceValue, field);
            // A hit's mark and word come from the hit looks: the hit players themselves show nothing comic.
            foreach (string field in new[] { "hitWorldFeel", "heavyHitWorldFeel", "skillHitWorldFeel" })
            {
                var player = (MMF_Player)so.FindProperty(field).objectReferenceValue;
                foreach (MMF_Feedback feedback in player.FeedbacksList)
                    if (feedback is MMF_PooledVfx vfx && vfx.Prefab != null)
                        Assert.IsNull(vfx.Prefab.GetComponent<ComicFx>(), field + " still shows " + vfx.Prefab.name);
            }
        }

        [Test]
        public void ComicEffects_AreLettering_DrawnOverTheInkedFrameAndNotWithIt()
        {
            int layer = LayerMask.NameToLayer(LetteringLayer);
            Assert.GreaterOrEqual(layer, 0, "the project has no " + LetteringLayer + " layer");
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ComicFolder.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (Transform part in AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<Transform>(true))
                    Assert.AreEqual(layer, part.gameObject.layer, path + ": " + part.name + " is not on the lettering layer");
            }

            // The renderer draws that layer in a pass of its own, after the ink and the glow, and nowhere else.
            Object renderer = AssetDatabase.LoadMainAssetAtPath(RendererPath);
            Assert.NotNull(renderer, RendererPath);
            var so = new SerializedObject(renderer);
            int transparent = so.FindProperty("m_TransparentLayerMask").intValue;
            Assert.AreEqual(0, transparent & (1 << layer), "the renderer's transparent pass draws the lettering layer too: it would be drawn twice, once under the ink");
            SerializedProperty features = so.FindProperty("m_RendererFeatures");
            Object lettering = null, glow = null;
            for (int i = 0; i < features.arraySize; i++)
            {
                Object feature = features.GetArrayElementAtIndex(i).objectReferenceValue;
                if (feature == null) continue;
                if (feature.GetType().Name == "ComicLetteringFeature") lettering = feature;
                if (feature.GetType().Name == "GlowHaloFeature") glow = feature;
            }
            Assert.NotNull(lettering, "the renderer has no ComicLettering feature: the lettering layer would not be drawn at all");
            Assert.NotNull(glow, "the renderer has no GlowHalo feature");
            int letteringEvent = new SerializedObject(lettering).FindProperty("renderPassEvent").intValue;
            int glowEvent = new SerializedObject(glow).FindProperty("settings.renderPassEvent").intValue;
            Assert.Greater(letteringEvent, glowEvent, "lettering is drawn before the glow, which would lie over a word again");
        }

        [Test]
        public void AMark_IsOnScreenLongEnoughToBeSeen()
        {
            int marks = 0;
            foreach (string guid in AssetDatabase.FindAssets("PFX_Mark t:Prefab", new[] { ComicFolder.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mark = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                float longest = 0f;
                foreach (ParticleSystem system in mark.GetComponentsInChildren<ParticleSystem>(true))
                    if (system.gameObject != mark) longest = Mathf.Max(longest, system.main.startDelay.constantMax + system.main.startLifetime.constantMax);
                Assert.GreaterOrEqual(longest, 0.17f, path + " is gone before its shape is seen");
                marks++;
            }
            Assert.GreaterOrEqual(marks, 24);
        }

        [Test]
        public void ThePool_TakesAnEffectBack_WhenItsParticlesAreDone()
        {
            var effect = new GameObject("PoolDurationProbe");
            try
            {
                var system = effect.AddComponent<ParticleSystem>();
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ParticleSystem.MainModule main = system.main;
                main.loop = false;
                main.duration = 5f;
                main.startDelay = 0.2f;
                main.startLifetime = 0.5f;
                ParticleSystem.EmissionModule emission = system.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0.1f, 8) });
                Assert.AreEqual(0.8f, NetworkCombatVFXPool.GetParticleDuration(effect), 0.001f, "a single burst: its delay, the burst's time and a particle's life");

                emission.SetBursts(new[] { new ParticleSystem.Burst(0.1f, 8, 8, 3, 0.25f) });
                Assert.AreEqual(1.3f, NetworkCombatVFXPool.GetParticleDuration(effect), 0.001f, "a burst that comes three times: up to the last one");

                emission.rateOverTime = 10f;
                Assert.AreEqual(5.7f, NetworkCombatVFXPool.GetParticleDuration(effect), 0.001f, "a system that emits at a rate emits for its whole run");

                emission.rateOverTime = 0f;
                main.loop = true;
                Assert.AreEqual(0.7f, NetworkCombatVFXPool.GetParticleDuration(effect), 0.001f, "a loop is ended by whoever holds it: a particle's life after that");
            }
            finally
            {
                Object.DestroyImmediate(effect);
            }
        }

        private static void AssertComic(GameObject effect, string what)
        {
            Assert.NotNull(effect, what + " is missing");
            Assert.NotNull(effect.GetComponent<ComicFx>(), what + " (" + effect.name + ") has no ComicFx");
            StringAssert.StartsWith(ComicFolder, AssetDatabase.GetAssetPath(effect), what);
        }

        private static float Distance(Color a, Color b)
        {
            float r = a.r - b.r, g = a.g - b.g, bl = a.b - b.b;
            return Mathf.Sqrt(r * r + g * g + bl * bl);
        }
    }
}
