using UnityEngine;
using PSD.Creator.Objects;
using CTFX2.Creator.Data;
using PSD.Creator.Components;
using Utility = PSD.Creator.Utility;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CTFX2.Creator.Components
{
    public abstract class TextBillboardComponent<T, S, TModule> : BillboardComponent<T, S, TModule>
        where T : TextBillboard<S, TModule>
        where S : BillboardObject
        where TModule : TextBillboard<S, TModule>.TextBillboardModule
    {
        protected virtual SystemHandler.ShapeModule.Shape Shape => new SystemHandler.ShapeModule.Sphere(0.001f, 1);

        protected abstract Curve Rotation { get; }

        protected override void ResetSystem()
        {
            base.ResetSystem();

            System.Noise.SetStrength((Settings.turbulence, 0.5f));
            System.Noise.SetFrequency(Settings.turbulenceSpeed);

            ApplyMovementEffect();
            Move();
        }

        protected virtual void Move() => MoveForward();

        protected virtual Vector3 GetForwardMovement() => new Vector3(0, 0, Settings.distance);

        protected void MoveForward() => MoveTo(GetForwardMovement());

        protected void MoveTo(Vector3 target) => System.VelocityOverLifetime.SetVelocity(BuildMoveCurve(target));

        private ((bool i, bool o) x, (bool i, bool o) y, (bool i, bool o) none, bool hasX, bool hasY, AnimationCurve easing, float T, float attack, float release) GetSampling()
        {
            (bool i, bool o) x = (Settings.inMovement == TextBillboard<S, TModule>.EMovement.Slide, Settings.outMovement == TextBillboard<S, TModule>.EMovement.Slide);
            (bool i, bool o) y = (Settings.inMovement == TextBillboard<S, TModule>.EMovement.Rise, Settings.outMovement == TextBillboard<S, TModule>.EMovement.Rise);
            (bool i, bool o) none = (Settings.inMovement == TextBillboard<S, TModule>.EMovement.None, Settings.outMovement == TextBillboard<S, TModule>.EMovement.None);
            AnimationCurve easing = Settings.movementEasing?.Easing ?? Utility.AnimationUtility.DefaultEasing;
            float T = System.Main.Lifetime;
            float attack = Mathf.Clamp01(Settings.movementAttack / T);
            float release = Mathf.Clamp01(Settings.movementRelease / T);
            return (x, y, none, x.i || x.o, y.i || y.o, easing, T, attack, release);
        }

        protected Curve3D BuildMoveCurve(Vector3 target, int samples = 20)
        {
            var (x, y, none, hasX, hasY, easing, T, attack, release) = GetSampling();

            float moveDurationRatio = 1 - Settings.speed;
            float dt = 1f / (samples - 1);

            float sum = 0f;
            for (int i = 0; i < samples; i++)
                sum += easing.Evaluate((i + 0.5f) / samples);
            float vScale = 1.0f / (T * moveDurationRatio * (sum / samples));

            Keyframe[] baseKeys = new Keyframe[easing.keys.Length + 1];
            for (int i = 0; i < easing.keys.Length; i++)
            {
                var k = easing.keys[i];
                baseKeys[i] = new Keyframe(k.time * moveDurationRatio, k.value)
                {
                    inTangent = k.inTangent / moveDurationRatio,
                    outTangent = k.outTangent / moveDurationRatio
                };
            }
            baseKeys[^1] = new Keyframe(moveDurationRatio + 0.001f, 0f);

            Vector3 startOffset = new Vector3(x.i ? 1 : 0, y.i ? 1 : 0);
            Vector3 endOffset = new Vector3(hasX ? 1 : 0, hasY ? 1 : 0);

            System.Shape.SetShape(Shape);
            System.Shape.SetPosition(Vector3.Scale(target, endOffset) - startOffset * Settings.movementOffset);
            System.Shape.SetRandomizePosition(module.randomOffset);

            float[] inVels = new float[samples];
            float[] outVels = new float[samples];

            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)(samples - 1);
                float tPrev = Mathf.Max(0f, t - dt);
                float tNext = Mathf.Min(1f, t + dt);

                // IN: spawn -> target
                float InEval(float _t) => (attack > 0 && _t < attack) ? easing.Evaluate(_t / attack) : 1f;
                inVels[i] = (InEval(tNext) - InEval(tPrev)) / (2f * dt * T);

                // OUT: target -> exit
                float OutEval(float _t) => (release > 0 && _t > (1f - release)) ? easing.Evaluate((_t - (1f - release)) / release) : 0f;
                outVels[i] = (OutEval(tNext) - OutEval(tPrev)) / (2f * dt * T);
            }

            Curve Combine(TextBillboard<S, TModule>.EMovement axisType)
            {
                Keyframe[] combinedKeys = new Keyframe[samples];
                for (int i = 0; i < samples; i++)
                {
                    float v = 0;
                    if (Settings.inMovement == axisType) v += inVels[i];
                    if (Settings.outMovement == axisType) v += outVels[i];
                    combinedKeys[i] = new Keyframe(i / (float)(samples - 1), v);
                }
                return (Settings.movementOffset, new AnimationCurve(combinedKeys));
            }

            Curve3D curve = (target * vScale, new AnimationCurve(baseKeys));
            return (
                hasX ? Combine(TextBillboard<S, TModule>.EMovement.Slide) : curve.X,
                hasY ? Combine(TextBillboard<S, TModule>.EMovement.Rise) : curve.Y,
                curve.Z
            );
        }

        private void ApplyMovementEffect()
        {
            var (x, y, none, hasX, hasY, easing, T, attack, release) = GetSampling();

            AnimationCurve BuildCombinedAngularCurve(TextBillboard<S, TModule>.EMovement effectType, int samples = 128)
            {
                float dt = 1f / (samples - 1);

                float[] derivs = new float[samples];
                for (int i = 0; i < samples; i++)
                {
                    float t = (float)i / (samples - 1);
                    if (i == 0) derivs[i] = (easing.Evaluate(t + dt) - easing.Evaluate(t)) / dt;
                    else if (i == samples - 1) derivs[i] = (easing.Evaluate(t) - easing.Evaluate(t - dt)) / dt;
                    else derivs[i] = (easing.Evaluate(t + dt) - easing.Evaluate(t - dt)) / (2f * dt);
                }

                Keyframe[] vKeys = new Keyframe[samples];
                for (int i = 0; i < samples; i++)
                {
                    float t = (float)i / (samples - 1);
                    float velocity = 0f;

                    if (Settings.inMovement == effectType && t < attack)
                        velocity += SampleDerivative(derivs, t / attack) / (attack * T);

                    if (Settings.outMovement == effectType && t > 1f - release)
                        velocity += SampleDerivative(derivs, (t - (1f - release)) / release) / (release * T);

                    vKeys[i] = new Keyframe(t, velocity) { inTangent = 0, outTangent = 0 };
                }
                return new AnimationCurve(vKeys);
            }

            float SampleDerivative(float[] derivatives, float t)
            {
                float idx = t * (derivatives.Length - 1);
                int lo = Mathf.Clamp((int)idx, 0, derivatives.Length - 2);
                return Mathf.Lerp(derivatives[lo], derivatives[lo + 1], idx - lo);
            }

            var r = -Mathf.PI / 2 * new Vector3(y.i ? 1 : 0, x.i ? 1 : 0, Settings.inMovement == TextBillboard<S, TModule>.EMovement.Flip ? 1 : 0);
            Curve3D rotation = (r, r);
            rotation.Z += Rotation;
            System.Main.SetRotation(rotation);

            System.RotationOverLifetime.SetRotation((
                hasY ? (Mathf.PI / 2, BuildCombinedAngularCurve(TextBillboard<S, TModule>.EMovement.Rise)) : null,
                hasX ? (Mathf.PI / 2, BuildCombinedAngularCurve(TextBillboard<S, TModule>.EMovement.Slide)) : null,
                Settings.inMovement == TextBillboard<S, TModule>.EMovement.Flip || Settings.outMovement == TextBillboard<S, TModule>.EMovement.Flip
                     ? (Mathf.PI / 2, BuildCombinedAngularCurve(TextBillboard<S, TModule>.EMovement.Flip)) : null
            ));

#if UNITY_EDITOR
            System.SizeOverLifetime.SetSize((1, AnimationBuilder.Build(1, AnimationUtility.TangentMode.Linear, (0, 0), (attack, 1), (1 - release, 1), (1, 0))));
#endif
        }
    }
}