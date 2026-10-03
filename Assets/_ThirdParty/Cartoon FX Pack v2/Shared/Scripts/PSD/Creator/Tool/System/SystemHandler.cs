using System;
using UnityEngine;
using PSD.Creator.Utility;

namespace PSD.Creator.Tool
{
    public abstract partial class Component<T, TCreator, TBasic>
    {
        protected partial class SystemHandler
        {
            private readonly IComponent component;

            private ParticleSystem System => component.ParticleSystem;
            private Material Material => component.Material;

            public SystemHandler(IComponent component)
            {
                this.component = component;
                Renderer = new RendererModule(this);
                Main = new MainModule(this);
                Emission = new EmissionModule(this);
                Shape = new ShapeModule(this);
                VelocityOverLifetime = new VelocityOverLifetimeModule(this);
                LimitVelocityOverLifetime = new LimitVelocityOverLifetimeModule(this);
                InheritVelocity = new InheritVelocityModule(this);
                LifetimeByEmitterSpeed = new LifetimeByEmitterSpeedModule(this);
                ForceOverLifetime = new ForceOverLifetimeModule(this);
                ColorOverLifetime = new ColorOverLifetimeModule(this);
                ColorBySpeed = new ColorBySpeedModule(this);
                SizeOverLifetime = new SizeOverLifetimeModule(this);
                SizeBySpeed = new SizeBySpeedModule(this);
                RotationOverLifetime = new RotationOverLifetimeModule(this);
                RotationBySpeed = new RotationBySpeedModule(this);
                ExternalForces = new ExternalForcesModule(this);
                Noise = new NoiseModule(this);
                Collision = new CollisionModule(this);
                Trigger = new TriggerModule(this);
                SubEmitters = new SubEmittersModule(this);
                TextureSheetAnimation = new TextureSheetAnimationModule(this);
                Lights = new LightsModule(this);
                Trails = new TrailModule(this);
                CustomData = new CustomDataModule(this);
            }

            public static SystemHandler Build(IComponent component) => new SystemHandler(component);

            public float Time => System.time;

            public SystemHandler CopyFrom(ParticleSystem source)
            {
#if UNITY_EDITOR
                System.CopySystem(source);
#endif
                return this;
            }

            private SystemHandler SetMaterial(Action<Material> setter)
            {
                setter(Material);
                return this;
            }
            public SystemHandler SetFloat(string name, float value) => SetMaterial(m => m.SetFloat(name, value));
            public SystemHandler SetColor(string name, Color value) => SetMaterial(m => m.SetColor(name, value));
            public SystemHandler SetTexture(string name, Texture value) => SetMaterial(m => m.SetTexture(name, value));
            public SystemHandler SetVector(string name, Vector4 value) => SetMaterial(m => m.SetVector(name, value));

            public abstract class Module<TModule, TParticleModule> where TModule : Module<TModule, TParticleModule>
            {
                protected SystemHandler handler;

                protected abstract TParticleModule ParticleModule { get; }

                protected virtual Action<TParticleModule, bool> SetActive { get; } = null;

                public Module(SystemHandler handler)
                {
                    this.handler = handler;
                }

                protected TModule Set(Action<TParticleModule> setter)
                {
                    SetActive?.Invoke(ParticleModule, true);
                    setter(ParticleModule);
                    return (TModule)this;
                }

                protected TModule Set(Action<TParticleModule, (Curve value, bool separate)> setter, Func<TParticleModule, Curve> getter, Curve value)
                    => Set(m => setter(m, (value ?? getter(m), false)));
                protected TModule Set(Action<TParticleModule, Curve> setter, Func<TParticleModule, Curve> getter, Curve value)
                    => Set(m => setter(m, value ?? getter(m)));
                protected TModule Set(Action<TParticleModule, (Curve x, Curve y, Curve z, bool separate)> setter, Func<TParticleModule, (Curve x, Curve y, Curve z)> getter, Curve3D value)
                    => Set(m => setter(m, (value.X ?? getter(m).x, value.Y ?? getter(m).y, value.Z ?? getter(m).z, true)));
                protected TModule Set(Action<TParticleModule, (Curve x, Curve y, Curve z)> setter, Func<TParticleModule, (Curve x, Curve y, Curve z)> getter, Curve3D value)
                    => Set(m => setter(m, (value.X ?? getter(m).x, value.Y ?? getter(m).y, value.Z ?? getter(m).z)));

                protected TModule Set(Action<TParticleModule, (float? value, bool separate)> setter, Func<TParticleModule, float?> getter, float? value)
                    => Set(m => setter(m, (value ?? getter(m), false)));
                protected TModule Set(Action<TParticleModule, float?> setter, Func<TParticleModule, float?> getter, float? value)
                    => Set(m => setter(m, value ?? getter(m)));
                protected TModule Set(Action<TParticleModule, (float? x, float? y, float? z, bool separate)> setter, Func<TParticleModule, (float x, float y, float z)> getter, (float? x, float? y, float? z) value)
                    => Set(m => setter(m, (value.x ?? getter(m).x, value.y ?? getter(m).y, value.z ?? getter(m).z, true)));
                protected TModule Set(Action<TParticleModule, (float? x, float? y, float? z)> setter, Func<TParticleModule, (float x, float y, float z)> getter, (float? x, float? y, float? z) value)
                    => Set(m => setter(m, (value.x ?? getter(m).x, value.y ?? getter(m).y, value.z ?? getter(m).z)));

                protected TModule Set(Action<TParticleModule, (Vector3, bool separate)> setter, Func<TParticleModule, Vector3> getter, (float? x, float? y, float? z) value)
                    => Set(m => setter(m, (new Vector3(value.x ?? getter(m).x, value.y ?? getter(m).y, value.z ?? getter(m).z), true)));
                protected TModule Set(Action<TParticleModule, Vector3> setter, Func<TParticleModule, Vector3> getter, (float? x, float? y, float? z) value)
                    => Set(m => setter(m, new Vector3(value.x ?? getter(m).x, value.y ?? getter(m).y, value.z ?? getter(m).z)));

                protected TModule Set(Action<TParticleModule, (Vector2, bool separate)> setter, Func<TParticleModule, Vector2> getter, (float? x, float? y) value)
                    => Set(m => setter(m, (new Vector2(value.x ?? getter(m).x, value.y ?? getter(m).y), true)));
                protected TModule Set(Action<TParticleModule, Vector2> setter, Func<TParticleModule, Vector2> getter, (float? x, float? y) value)
                    => Set(m => setter(m, new Vector2(value.x ?? getter(m).x, value.y ?? getter(m).y)));

                public void SetEnabled(bool enabled) => SetActive?.Invoke(ParticleModule, enabled);
            }
        }
    }
}