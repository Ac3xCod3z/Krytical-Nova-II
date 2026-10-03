using UnityEngine;
using CTFX2.Creator.Components;
using System.Linq;
using System.Collections.Generic;
using System;
using CTFX2.Creator.Data;
using CTFX2.Creator.Objects;
using PSD.Creator.Tool;
using CTFX2.CameraShake;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CTFX2.Creator.Tool
{
    [DisallowMultipleComponent]
    [AddComponentMenu("CTFX2/Cartoon FX")]
    [RequireComponent(typeof(ParticleSystem))]
    [ExecuteInEditMode]
    public class CartoonFX : PSDCreator<CartoonFX, CartoonFXBasic>
    {
        #region Data
        public Text text;
        public Background background;
        public Halftone halftone;
        public Effects effects;
        public Impact impact;
        public Debris debris;
        public Smoke smoke;
        #endregion

        private void Start()
        {
            if (basic.useCameraShake && basic.cameraShake?.cameraShake != null)
            {
                foreach (var shakeController in FindObjectsByType<CameraShakeController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    shakeController.Shake(basic.cameraShake);
            }
        }

        public override string GetTypeName(IComponent component)
        {
            string typeName = component.GetType().Name;
            switch (component)
            {
                case TextComponent text: return $"{typeName} {(int)text.Character} {text.Letter} {text.Id} {text.Line}";
                case SmokeTrailComponent trail: return $"{typeName} {GetTypeName(trail.transform.parent.GetComponents<IComponent>().FirstOrDefault(c => c != component))}";
                case EffectTrailComponent trail: return $"{typeName} {GetTypeName(trail.transform.parent.GetComponents<IComponent>().FirstOrDefault(c => c != component))}";
                case IMultipleComponent multiple: return $"{typeName} {multiple.Id}";
                default: return typeName;
            }
        }

        protected override Action<TBillboard, TModule> SetupBillboardsSetter<TBillboard, TModule>(int current)
            => (c, module) =>
            {
                if (module is Debris.DebrisModule m && m.enableTrail) SetupTrail(c, m.trail, current);
                if (module is Smoke.SmokeModule m2 && m2.enableTrail) SetupTrail(c, m2, m2.trail, current);
            };
        private void SetupTrail(Component parent, SmokeTrail trail, int order)
        {
            var sub = parent.GetComponent<ParticleSystem>().subEmitters;

            trail.enabled = true;
            if (trail.Object == null || !SetupComponent<SmokeTrailComponent>(trail, out var trailComponent, parent: parent, initializer: c => { c.module = new SmokeTrail.SmokeTrailModule(); c.Order = order; }, reset: false))
            {
                sub.enabled = false;
                return;
            }

            trailComponent.settings = trail;

            sub.enabled = true;
            while (sub.subEmittersCount > 0) sub.RemoveSubEmitter(0);
            sub.AddSubEmitter(trailComponent.ParticleSystem, ParticleSystemSubEmitterType.Birth, ParticleSystemSubEmitterProperties.InheritNothing, trail.probability);

            trailComponent.ResetSystem(trailComponent.Reference.system);
#if UNITY_EDITOR
            EditorUtility.SetDirty(trailComponent);
#endif
        }
        private void SetupTrail(Component parent, Smoke.SmokeModule module, EffectTrail trail, int order)
        {
            var sub = parent.GetComponent<ParticleSystem>().subEmitters;

            trail.enabled = true;
            if (trail.Object == null || !SetupComponent<EffectTrailComponent>(trail, out var trailComponent, parent: parent, initializer: c => { c.module = new EffectTrail.EffectTrailModule(); c.Order = order; }, reset: false))
            {
                sub.enabled = false;
                return;
            }

            trailComponent.settings = trail;
            trailComponent.Parent = module;

            sub.enabled = true;
            while (sub.subEmittersCount > 0) sub.RemoveSubEmitter(0);
            sub.AddSubEmitter(trailComponent.ParticleSystem, ParticleSystemSubEmitterType.Birth, ParticleSystemSubEmitterProperties.InheritLifetime, trail.probability);

            trailComponent.ResetSystem(trailComponent.Reference.system);
#if UNITY_EDITOR
            EditorUtility.SetDirty(trailComponent);
#endif
        }

        protected override void SetupComponents()
        {
            int order = 0;
            SetupBillboards<SmokeComponent, Smoke, SmokeObject, Smoke.SmokeModule>(new List<SmokeComponent>(), smoke, (c, i) => $"Smoke {i}", ref order);
            SetupBillboards<DebrisComponent, Debris, DebrisObject, Debris.DebrisModule>(new List<DebrisComponent>(), debris, (c, i) => $"Debris {i}", ref order);
            SetupBillboards<HalftoneComponent, Halftone, HalftoneObject, Halftone.HalftoneModule>(new List<HalftoneComponent>(), halftone, (c, i) => $"Halftone {i}", ref order);
            SetupBillboards<BackgroundComponent, Background, BackgroundObject, Background.BackgroundModule>(new List<BackgroundComponent>(), background, (c, i) => $"Background {i}", ref order);
            SetupBillboards<EffectsComponent, Effects, EffectsObject, Effects.EffectsModule>(new List<EffectsComponent>(), effects, (c, i) => $"Effects {i}", ref order);

            if (text.text.Length > 0)
            {
                int maxLetters = text.text.Max(l => l.Length);
                var textComponents = new List<TextComponent>();
                for (int i = 0; i < text.text.Length; i++)
                {
                    string line = text.text[i];
                    for (int j = 0; j < line.Length; j++)
                    {
                        char character = line[j];
                        SetupBillboards<TextComponent, Text, TextObject, Text.TextModule>(
                            textComponents,
                            text,
                            (c, id) => $"Text {i} {j} {id} {(int)c.Character}",
                            ref order,
                            setter: (c) => { c.Character = character; c.Letter = j; c.Line = i; c.Lines = text.text.Length; c.MaxLetters = maxLetters; },
                            idCheck: (c, id) => c.Line == i && c.Id == id && c.Letter == j,
                            cleanup: false
                        );
                    }
                }
                CleanupMultiple(textComponents);
            }

            SetupBillboards<ImpactComponent, Impact, ImpactObject, Impact.ImpactModule>(new List<ImpactComponent>(), impact, (c, i) => $"Impact {i}", ref order);

#if UNITY_EDITOR
            EditorUtility.UnloadUnusedAssetsImmediate();
            Resources.UnloadUnusedAssets();
#endif
        }
    }
}
