using System;
using System.Linq;
using UnityEngine;
using MinMaxCurve = UnityEngine.ParticleSystem.MinMaxCurve;
using MinMaxGradient = UnityEngine.ParticleSystem.MinMaxGradient;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Creator.Tool
{
    public abstract partial class Component<T, TCreator, TBasic>
    {
        protected class Curve
        {
            public MinMaxCurve Value;

            public Curve(MinMaxCurve value)
            {
                Value = Normalize(value);
            }

            private static MinMaxCurve Normalize(MinMaxCurve c)
            {
                return c.mode switch
                {
                    ParticleSystemCurveMode.Constant => new MinMaxCurve(c.constant),
                    ParticleSystemCurveMode.TwoConstants => new MinMaxCurve(c.constantMin, c.constantMax),
                    ParticleSystemCurveMode.Curve => new MinMaxCurve(c.curveMultiplier, c.curve),
                    ParticleSystemCurveMode.TwoCurves => new MinMaxCurve(c.curveMultiplier, c.curveMin, c.curveMax),
                    _ => new MinMaxCurve(c.constant)
                };
            }

            public static Curve Constant(float value) => new Curve(new MinMaxCurve(value));
            public static Curve TwoConstants(float min, float max) => new Curve(new MinMaxCurve(min, max));
            public static Curve CurveMode(float multiplier, AnimationCurve curve) => new Curve(new MinMaxCurve(multiplier, curve));
            public static Curve TwoCurves(float multiplier, AnimationCurve min, AnimationCurve max) => new Curve(new MinMaxCurve(multiplier, min, max));

            public Curve Invert()
            {
                var original = Value;
                return original.mode switch
                {
                    ParticleSystemCurveMode.Constant => Constant(-original.constant),
                    ParticleSystemCurveMode.TwoConstants => TwoConstants(-original.constantMax, -original.constantMin),
                    ParticleSystemCurveMode.Curve => CurveMode(original.curveMultiplier, InvertAnimationCurve(original.curve)),
                    ParticleSystemCurveMode.TwoCurves => TwoCurves(original.curveMultiplier, InvertAnimationCurve(original.curveMax), InvertAnimationCurve(original.curveMin)),
                    _ => this
                };
            }

            private static AnimationCurve InvertAnimationCurve(AnimationCurve src)
            {
                if (src == null) return null;

                Keyframe[] keys = new Keyframe[src.keys.Length];
                for (int i = 0; i < keys.Length; i++)
                {
                    var k = src.keys[i];
                    keys[i] = new Keyframe(k.time, -k.value)
                    {
                        inTangent = -k.inTangent,
                        outTangent = -k.outTangent,
                        weightedMode = k.weightedMode,
                        inWeight = k.inWeight,
                        outWeight = k.outWeight
                    };
                }
                return new AnimationCurve(keys);
            }

            public static implicit operator MinMaxCurve(Curve c) => Normalize(c.Value);
            public static implicit operator Curve(MinMaxCurve c) => new Curve(c);

            public static implicit operator Curve(float value) => Constant(value);
            public static implicit operator Curve((float value, float multiplier) t) => TwoConstants(t.value * t.multiplier, t.value);
            public static implicit operator Curve((float value, AnimationCurve easing) t) => CurveMode(t.value, t.easing);
            public static implicit operator Curve((float value, AnimationCurve min, AnimationCurve max) t) => TwoCurves(t.value, t.min, t.max);

            public static implicit operator Curve((Curve min, Curve max) tuple)
            {
                var minVal = tuple.min.Value;
                var maxVal = tuple.max.Value;

                if (minVal.mode == ParticleSystemCurveMode.Constant && maxVal.mode == ParticleSystemCurveMode.Constant)
                    return TwoConstants(minVal.constant, maxVal.constant);

                AnimationCurve minCurve = minVal.mode == ParticleSystemCurveMode.Curve ? minVal.curve : AnimationCurve.Constant(0f, 1f, minVal.constant);
                AnimationCurve maxCurve = maxVal.mode == ParticleSystemCurveMode.Curve ? maxVal.curve : AnimationCurve.Constant(0f, 1f, maxVal.constant);

                float multiplier = Mathf.Max(minVal.curveMultiplier, maxVal.curveMultiplier);
                if (minVal.curveMultiplier != multiplier && minVal.curveMultiplier > 0)
                    minCurve = RescaleCurveKeys(minCurve, minVal.curveMultiplier / multiplier);
                if (maxVal.curveMultiplier != multiplier && maxVal.curveMultiplier > 0)
                    maxCurve = RescaleCurveKeys(maxCurve, maxVal.curveMultiplier / multiplier);

                return TwoCurves(multiplier, minCurve, maxCurve);
            }

            private static AnimationCurve RescaleCurveKeys(AnimationCurve curve, float scale)
            {
                Keyframe[] keys = new Keyframe[curve.keys.Length];
                for (int i = 0; i < keys.Length; i++)
                {
                    var k = curve.keys[i];
                    keys[i] = new Keyframe(k.time, k.value * scale)
                    {
                        inTangent = k.inTangent * scale,
                        outTangent = k.outTangent * scale
                    };
                }
                return new AnimationCurve(keys);
            }

            public static Curve operator +(Curve a, Curve b) => Combine(a, b, (x, y) => x + y);
            public static Curve operator -(Curve a, Curve b) => Combine(a, b, (x, y) => x - y);
            public static Curve operator *(Curve a, Curve b) => Combine(a, b, (x, y) => x * y);
            public static Curve operator /(Curve a, Curve b) => Combine(a, b, (x, y) => x / y);
            private static Curve Combine(Curve a, Curve b, Func<float, float, float> operation)
            {
                var m1 = a.Value;
                var m2 = b.Value;

                if (m1.mode == ParticleSystemCurveMode.Constant && m2.mode == ParticleSystemCurveMode.Constant)
                    return new Curve(new MinMaxCurve(operation(m1.constant, m2.constant)));

                if (m1.mode == ParticleSystemCurveMode.TwoConstants && m2.mode == ParticleSystemCurveMode.Constant)
                    return TwoConstants(operation(m1.constantMin, m2.constant), operation(m1.constantMax, m2.constant));
                if (m1.mode == ParticleSystemCurveMode.Constant && m2.mode == ParticleSystemCurveMode.TwoConstants)
                    return TwoConstants(operation(m1.constant, m2.constantMin), operation(m1.constant, m2.constantMax));
                if (m1.mode == ParticleSystemCurveMode.TwoConstants && m2.mode == ParticleSystemCurveMode.TwoConstants)
                    return TwoConstants(operation(m1.constantMin, m2.constantMin), operation(m1.constantMax, m2.constantMax));

                bool useRange = m1.mode == ParticleSystemCurveMode.TwoConstants ||
                                m1.mode == ParticleSystemCurveMode.TwoCurves ||
                                m2.mode == ParticleSystemCurveMode.TwoConstants ||
                                m2.mode == ParticleSystemCurveMode.TwoCurves;

                int samples = 20;
                Keyframe[] minKeys = new Keyframe[samples];
                Keyframe[] maxKeys = useRange ? new Keyframe[samples] : null;

                for (int i = 0; i < samples; i++)
                {
                    float t = i / (float)(samples - 1);

                    if (useRange)
                    {
                        float minVal = operation(EvaluateMin(m1, t), EvaluateMin(m2, t));
                        float maxVal = operation(EvaluateMax(m1, t), EvaluateMax(m2, t));
                        minKeys[i] = new Keyframe(t, minVal);
                        maxKeys[i] = new Keyframe(t, maxVal);
                    }
                    else
                    {
                        float val = operation(m1.Evaluate(t), m2.Evaluate(t));
                        minKeys[i] = new Keyframe(t, val);
                    }
                }

                if (useRange) return TwoCurves(1f, new AnimationCurve(minKeys), new AnimationCurve(maxKeys));
                return CurveMode(1f, new AnimationCurve(minKeys));
            }
            private static float EvaluateMin(MinMaxCurve m, float t)
            {
                return m.mode switch
                {
                    ParticleSystemCurveMode.TwoConstants => m.constantMin,
                    ParticleSystemCurveMode.TwoCurves => m.curveMin.Evaluate(t) * m.curveMultiplier,
                    _ => m.Evaluate(t)
                };
            }
            private static float EvaluateMax(MinMaxCurve m, float t)
            {
                return m.mode switch
                {
                    ParticleSystemCurveMode.TwoConstants => m.constantMax,
                    ParticleSystemCurveMode.TwoCurves => m.curveMax.Evaluate(t) * m.curveMultiplier,
                    _ => m.Evaluate(t)
                };
            }
        }

        protected class Curve3D
        {
            public Curve X, Y, Z;

            public Curve3D(Curve x = null, Curve y = null, Curve z = null)
            {
                X = Normalize(x);
                Y = Normalize(y);
                Z = Normalize(z);
            }

            private static Curve Normalize(Curve c)
            {
                if (c == null) return null;
                return new Curve(c.Value);
            }

            public Curve3D Invert() => new Curve3D(X?.Invert(), Y?.Invert(), Z?.Invert());

            static Curve3D Map(Vector3 v, Func<float, Curve> f) => new Curve3D(f(v.x), f(v.y), f(v.z));

            public static implicit operator (Curve x, Curve y, Curve z)(Curve3D c) => (c.X, c.Y, c.Z);
            public static implicit operator Curve3D((Curve x, Curve y, Curve z) t) => new Curve3D(t.x, t.y, t.z);
            public static implicit operator Curve3D(Vector3 v) => Map(v, x => x);
            public static implicit operator Curve3D((Vector3 v, float? m) t) => Map(t.v, x => Curve.TwoConstants(x * t.m.Value, x));
            public static implicit operator Curve3D((Vector3 v, AnimationCurve e) t) => Map(t.v, x => Curve.CurveMode(x, t.e));
            public static implicit operator Curve3D((Curve3D min, Curve3D max) tuple)
            {
                return new Curve3D(
                    (tuple.min.X, tuple.max.X),
                    (tuple.min.Y, tuple.max.Y),
                    (tuple.min.Z, tuple.max.Z)
                );
            }

            public static Curve3D operator +(Curve3D a, Curve3D b) => SafeOp(a, b, (a, b) => a + b);
            public static Curve3D operator +(Curve3D a, float m) => a + (m, m, m);
            public static Curve3D operator -(Curve3D a, Curve3D b) => SafeOp(a, b, (a, b) => a - b);
            public static Curve3D operator -(Curve3D a, float m) => a - (m, m, m);
            public static Curve3D operator *(Curve3D a, Curve3D b) => SafeOp(a, b, (a, b) => a * b);
            public static Curve3D operator *(Curve3D a, float m) => a * (m, m, m);
            public static Curve3D operator /(Curve3D a, Curve3D b) => SafeOp(a, b, (a, b) => a / b);
            public static Curve3D operator /(Curve3D a, float m) => a / (m, m, m);

            private static Curve3D SafeOp(Curve3D a, Curve3D b, Func<Curve, Curve, Curve> op)
                => new Curve3D(SafeOp(a?.X, b?.X, op), SafeOp(a?.Y, b?.Y, op), SafeOp(a?.Z, b?.Z, op));
            private static Curve SafeOp(Curve a, Curve b, Func<Curve, Curve, Curve> op)
            {
                if (a == null) return b; if (b == null) return a;
                return op(a, b);
            }
        }

        protected class Burst : Curve
        {
            public float Time;

            public Burst(float time, Curve curve) : base(curve)
            {
                Time = time;
            }

            public static implicit operator ParticleSystem.Burst(Burst burst) => new ParticleSystem.Burst(burst.Time, burst);
            public static implicit operator Burst((float time, Curve curve) t) => new Burst(t.time, t.curve);
        }

        protected class Gradient
        {
            public MinMaxGradient Value;

            public Gradient(MinMaxGradient value)
            {
                Value = Normalize(value);
            }

            private static MinMaxGradient Normalize(MinMaxGradient g)
            {
                return g.mode switch
                {
                    ParticleSystemGradientMode.Color => new MinMaxGradient(g.color),
                    ParticleSystemGradientMode.TwoColors => new MinMaxGradient(g.colorMin, g.colorMax),
                    ParticleSystemGradientMode.Gradient => new MinMaxGradient(g.gradient),
                    ParticleSystemGradientMode.TwoGradients => new MinMaxGradient(g.gradientMin, g.gradientMax),
                    ParticleSystemGradientMode.RandomColor => new MinMaxGradient(g.gradient),
                    _ => new MinMaxGradient(g.color)
                };
            }

            public static Gradient Color(Color color) => new Gradient(new MinMaxGradient(color));
            public static Gradient TwoColors(Color min, Color max) => new Gradient(new MinMaxGradient(min, max));
            public static Gradient GradientMode(UnityEngine.Gradient gradient) => new Gradient(new MinMaxGradient(gradient));
            public static Gradient TwoGradients(UnityEngine.Gradient min, UnityEngine.Gradient max) => new Gradient(new MinMaxGradient(min, max));

            public static implicit operator MinMaxGradient(Gradient g) => g.Value;
            public static implicit operator Gradient(MinMaxGradient g) => new Gradient(g);
            public static implicit operator Gradient(Color color) => Color(color);
            public static implicit operator Gradient(UnityEngine.Gradient gradient) => GradientMode(gradient);
            public static implicit operator Gradient((Color min, Color max) t) => TwoColors(t.min, t.max);
            public static implicit operator Gradient((UnityEngine.Gradient min, UnityEngine.Gradient max) t) => TwoGradients(t.min, t.max);

            public static implicit operator Gradient((bool isGradient, UnityEngine.Gradient gradient, Color color) t)
                => t.isGradient ? GradientMode(t.gradient) : Color(t.color);

            public static implicit operator Gradient((Gradient min, Gradient max) tuple)
            {
                var minVal = tuple.min.Value;
                var maxVal = tuple.max.Value;

                if (minVal.mode == ParticleSystemGradientMode.Color && maxVal.mode == ParticleSystemGradientMode.Color)
                    return TwoColors(minVal.color, maxVal.color);

                UnityEngine.Gradient minGrad = minVal.mode == ParticleSystemGradientMode.Gradient
                    ? minVal.gradient
                    : SolidGradient(minVal.color);
                UnityEngine.Gradient maxGrad = maxVal.mode == ParticleSystemGradientMode.Gradient
                    ? maxVal.gradient
                    : SolidGradient(maxVal.color);

                return TwoGradients(minGrad, maxGrad);
            }

            private static UnityEngine.Gradient SolidGradient(Color color)
            {
                var g = new UnityEngine.Gradient();
                g.SetKeys(
                    new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                    new[] { new GradientAlphaKey(color.a, 0f), new GradientAlphaKey(color.a, 1f) }
                );
                return g;
            }
        }

#if UNITY_EDITOR
        protected class AnimationBuilder
        {
            public AnimationCurve Curve;
            public AnimationUtility.TangentMode Mode;

            public struct Key
            {
                public float Time, Value;
                public Key(float time, float value)
                {
                    Time = time;
                    Value = value;
                }

                public static implicit operator Keyframe(Key k) => new Keyframe(k.Time, k.Value);
                public static implicit operator Key((float time, float value) t) => new Key(t.time, t.value);
            }

            public AnimationBuilder(AnimationUtility.TangentMode mode, params Key[] keys) : this(new AnimationCurve(keys.Select(k => (Keyframe)k).ToArray()))
            {
                for (int i = 0; i < keys.Length; i++)
                {
                    try
                    {
                        AnimationUtility.SetKeyLeftTangentMode(Curve, i, mode);
                        AnimationUtility.SetKeyRightTangentMode(Curve, i, mode);
                    }
                    catch (IndexOutOfRangeException) { }
                }
            }
            public AnimationBuilder(AnimationCurve curve)
            {
                Curve = curve;
            }

            public static AnimationBuilder Build(float duration = 1, AnimationUtility.TangentMode mode = AnimationUtility.TangentMode.Linear, params Key[] keys)
                => new AnimationBuilder(mode, keys.Select(k => new Key(k.Time / duration, k.Value)).ToArray());

            public static implicit operator AnimationCurve(AnimationBuilder b) => b.Curve;
        }
#endif
    }
}