using System;
using System.Linq;
using UnityEngine;
using MinMaxGradient = UnityEngine.ParticleSystem.MinMaxGradient;
using UnityEngine.Rendering;
using PSD.Creator.Data;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Creator.Tool
{
    public abstract partial class Component<T, TCreator, TBasic>
    {
        protected partial class SystemHandler
        {
            public class MainModule : Module<MainModule, ParticleSystem.MainModule>
            {
                protected override ParticleSystem.MainModule ParticleModule => handler.System.main;

                public MainModule(SystemHandler handler) : base(handler) { }

                public float Duration => ParticleModule.duration;
                public bool Looping => ParticleModule.loop;
                public bool Prewarm => Looping && ParticleModule.prewarm;
                public float Lifetime
                {
                    get
                    {
                        switch (ParticleModule.startLifetime.mode)
                        {
                            case ParticleSystemCurveMode.Constant:
                                return ParticleModule.startLifetime.constant;
                            case ParticleSystemCurveMode.TwoConstants:
                                return ParticleModule.startLifetime.constantMin;
                            case ParticleSystemCurveMode.Curve:
                                return ParticleModule.startLifetime.curve.keys.Last().time;
                            case ParticleSystemCurveMode.TwoCurves:
                                return ParticleModule.startLifetime.curveMin.keys.Last().time;
                            default:
                                return 1;
                        }
                    }
                }

                public MainModule SetDelay(Curve value) => Set(m => m.startDelay = value);
                public MainModule SetLifetime(Curve value) => Set(m => m.startLifetime = value);
                public MainModule SetSpeed(Curve value) => Set(m => m.startSpeed = value);
                public MainModule SetSize(Curve value) => Set((m, v) => (m.startSize, m.startSize3D) = v, m => m.startSize, value);
                public MainModule SetSize(Curve3D value) => Set((m, v) => (m.startSizeX, m.startSizeY, m.startSizeZ, m.startSize3D) = v, m => (m.startSizeX, m.startSizeY, m.startSizeZ), value);
                public MainModule SetSize(Curve x = null, Curve y = null, Curve z = null) => SetSize((x, y, z));
                public MainModule SetRotation(Curve value) => Set((m, v) => (m.startRotation, m.startRotation3D) = v, m => m.startRotation, value);
                public MainModule SetRotation(Curve3D value) => Set((m, v) => (m.startRotationX, m.startRotationY, m.startRotationZ, m.startRotation3D) = v, m => (m.startRotationX, m.startRotationY, m.startRotationZ), value);
                public MainModule SetRotation(Curve x = null, Curve y = null, Curve z = null) => SetRotation((x, y, z));
                public MainModule SetFlip(float value) => Set(m => m.flipRotation = value);
                public MainModule SetColor(MinMaxGradient value) => Set(m => m.startColor = value);
                public MainModule SetGravity(ParticleSystemGravitySource source) => Set(m => m.gravitySource = source);
                public MainModule SetGravity(Curve value) => Set(m => m.gravityModifier = value);
                public MainModule SetSimulationSpace(ParticleSystemSimulationSpace space) => Set(m => m.simulationSpace = space);
                public MainModule SetSimulationSpeed(float value) => Set(m => m.simulationSpeed = value);
                public MainModule SetPlayOnAwake(bool value) => Set(m => m.playOnAwake = value);
                public MainModule SetEmitter(ParticleSystemEmitterVelocityMode mode, float? x = null, float? y = null, float? z = null) =>
                     Set(m => m.emitterVelocityMode = mode)
                    .Set((m, v) => m.emitterVelocity = v, m => m.emitterVelocity, (x, y, z));
                public MainModule SetParticleLimit(int value) => Set(m => m.maxParticles = value);
                public MainModule SetStopAction(ParticleSystemStopAction action) => Set(m => m.stopAction = action);
                public MainModule SetCulling(ParticleSystemCullingMode mode) => Set(m => m.cullingMode = mode);
                public MainModule SetRingBuffer(ParticleSystemRingBufferMode mode, float? x = null, float? y = null) =>
                     Set(m => m.ringBufferMode = mode)
                    .Set((m, v) => m.ringBufferLoopRange = v, m => m.ringBufferLoopRange, (x, y));
            }
            public MainModule Main;

            public class EmissionModule : Module<EmissionModule, ParticleSystem.EmissionModule>
            {
                protected override ParticleSystem.EmissionModule ParticleModule => handler.System.emission;

                protected override Action<ParticleSystem.EmissionModule, bool> SetActive => (m, enabled) => m.enabled = enabled;

                public EmissionModule(SystemHandler handler) : base(handler) { }

                public EmissionModule SetRateOverTime(Curve value) => Set(m => m.rateOverTime = value);
                public EmissionModule SetRateOverDistance(Curve value) => Set(m => m.rateOverDistance = value);
                public EmissionModule SetBursts(int count, Func<int, Burst> provider) => SetBursts(Enumerable.Range(0, count).Select(i => provider(i)).ToArray());
                public EmissionModule SetBursts(params Burst[] bursts) => Set(m => m.SetBursts(bursts.Select(b => new ParticleSystem.Burst(b.Time, b)).ToArray()));
            }
            public EmissionModule Emission;

            public class ShapeModule : Module<ShapeModule, ParticleSystem.ShapeModule>
            {
                protected override ParticleSystem.ShapeModule ParticleModule => handler.System.shape;

                protected override Action<ParticleSystem.ShapeModule, bool> SetActive => (m, enabled) => m.enabled = enabled;

                public ShapeModule(SystemHandler handler) : base(handler) { }

                public class ShapeMultiMode
                {
                    private readonly ParticleSystemShapeMultiModeValue? Mode;
                    private readonly float? Spread;
                    private readonly Curve Speed;
                    public ShapeMultiMode(ParticleSystemShapeMultiModeValue? mode = null, float? spread = null, Curve speed = null)
                    { Mode = mode; Spread = spread; Speed = speed; }
                    internal ShapeModule Set(ShapeModule module) => module.Set(m =>
                    {
                        if (Mode.HasValue) m.arcMode = Mode.Value;
                        if (Spread.HasValue) m.arcSpread = Spread.Value;
                        if (Speed != null) m.arcSpeed = Speed;
                    });
                }

                public abstract class Shape
                {
                    public abstract ParticleSystemShapeType Type { get; }
                    internal virtual ShapeModule Set(ShapeModule module) => module.Set(m => m.shapeType = Type);
                }

                public abstract class PrimitiveShape : Shape
                {
                    private readonly float Radius, Arc;
                    private readonly ShapeMultiMode Mode;
                    public PrimitiveShape(float radius, float arc, ShapeMultiMode mode)
                    {
                        Radius = radius; Arc = arc;
                        Mode = mode;
                    }
                    internal override ShapeModule Set(ShapeModule module) => base.Set(module).Set(m =>
                    {
                        Mode?.Set(module);
                        m.radius = Radius; m.arc = Arc;
                    });
                }
                public abstract class Ellipse : PrimitiveShape
                {
                    private readonly float Thickness;
                    public Ellipse(float radius, float thickness, float arc = 360, ShapeMultiMode mode = null)
                        : base(radius, arc, mode)
                    { Thickness = thickness; }
                    internal override ShapeModule Set(ShapeModule module) => base.Set(module).Set(m => m.radiusThickness = Thickness);
                }
                public class Sphere : Ellipse
                {
                    public override ParticleSystemShapeType Type => ParticleSystemShapeType.Sphere;
                    public Sphere(float radius, float thickness, float arc = 360, ShapeMultiMode mode = null)
                        : base(radius, thickness, arc, mode) { }
                }
                public class Hemisphere : Ellipse
                {
                    public override ParticleSystemShapeType Type => ParticleSystemShapeType.Hemisphere;
                    public Hemisphere(float radius, float thickness, float arc = 360, ShapeMultiMode mode = null)
                        : base(radius, thickness, arc, mode) { }
                }
                public class Cone : Ellipse
                {
                    public override ParticleSystemShapeType Type => ParticleSystemShapeType.Cone;
                    private readonly float Angle;
                    public Cone(float angle, float radius, float thickness, float arc = 360, ShapeMultiMode mode = null)
                        : base(radius, thickness, arc, mode) { Angle = angle; }
                    internal override ShapeModule Set(ShapeModule module) => base.Set(module).Set(m => m.angle = Angle);
                }
                public class ConeVolume : Cone
                {
                    public override ParticleSystemShapeType Type => ParticleSystemShapeType.ConeVolume;
                    public ConeVolume(float angle, float radius, float thickness, float arc = 360, ShapeMultiMode mode = null)
                        : base(angle, radius, thickness, arc, mode) { }
                }
                public class Donut : Ellipse
                {
                    public override ParticleSystemShapeType Type => ParticleSystemShapeType.Donut;
                    private readonly float DonutRadius;
                    public Donut(float donutRadius, float radius, float thickness, float arc = 360, ShapeMultiMode mode = null)
                        : base(radius, thickness, arc, mode) { DonutRadius = donutRadius; }
                    internal override ShapeModule Set(ShapeModule module) => base.Set(module).Set(m => m.donutRadius = DonutRadius);
                }
                public class Circle : Ellipse
                {
                    public override ParticleSystemShapeType Type => ParticleSystemShapeType.Circle;
                    public Circle(float radius, float thickness, float arc = 360, ShapeMultiMode mode = null)
                        : base(radius, thickness, arc, mode) { }
                }
                public class Edge : PrimitiveShape
                {
                    public override ParticleSystemShapeType Type => ParticleSystemShapeType.SingleSidedEdge;
                    public Edge(float radius, float arc = 360, ShapeMultiMode mode = null)
                        : base(radius, arc, mode) { }
                }
                public class Box : Shape
                {
                    public override ParticleSystemShapeType Type => ParticleSystemShapeType.Box;
                    public Box() { }
                }
                public class BoxEdge : Box
                {
                    public override ParticleSystemShapeType Type => ParticleSystemShapeType.BoxEdge;
                    public BoxEdge() { }
                }
                public class BoxShell : Box
                {
                    public override ParticleSystemShapeType Type => ParticleSystemShapeType.BoxShell;
                    public BoxShell() { }
                }
                public class Rectangle : Shape
                {
                    public override ParticleSystemShapeType Type => ParticleSystemShapeType.Rectangle;
                    public Rectangle() { }
                }
                public abstract class MeshShape : Shape
                {
                    private readonly ParticleSystemMeshShapeType MeshType;
                    private readonly ShapeMultiMode Mode;
                    private readonly int? MaterialIndex;
                    private readonly bool UseMeshColors;
                    private readonly float NormalOffset;
                    public MeshShape(ParticleSystemMeshShapeType meshType, int? materialIndex, bool useMeshColors, float normalOffset, ShapeMultiMode mode)
                    {
                        MeshType = meshType;
                        Mode = mode;
                        MaterialIndex = materialIndex;
                        UseMeshColors = useMeshColors;
                        NormalOffset = normalOffset;
                    }
                    internal override ShapeModule Set(ShapeModule module) => base.Set(module).Set(m =>
                    {
                        Mode?.Set(module);
                        m.meshShapeType = MeshType;
                        m.useMeshMaterialIndex = MaterialIndex.HasValue; m.meshMaterialIndex = MaterialIndex.HasValue ? MaterialIndex.Value : 0;
                        m.useMeshColors = UseMeshColors;
                        m.normalOffset = NormalOffset;
                    });
                }
                public class Mesh : MeshShape
                {
                    public override ParticleSystemShapeType Type => ParticleSystemShapeType.Mesh;
                    private readonly UnityEngine.Mesh mesh;
                    public Mesh(UnityEngine.Mesh mesh, ParticleSystemMeshShapeType meshType, int? materialIndex, bool useMeshColors, float normalOffset, ShapeMultiMode mode = null)
                        : base(meshType, materialIndex, useMeshColors, normalOffset, mode)
                    { this.mesh = mesh; }
                    internal override ShapeModule Set(ShapeModule module) => base.Set(module).Set(m => m.mesh = mesh);
                }
                public class MeshRenderer : MeshShape
                {
                    public override ParticleSystemShapeType Type => ParticleSystemShapeType.MeshRenderer;
                    private readonly UnityEngine.MeshRenderer meshRenderer;
                    public MeshRenderer(UnityEngine.MeshRenderer meshRenderer, ParticleSystemMeshShapeType meshType, int? materialIndex, bool useMeshColors, float normalOffset, ShapeMultiMode mode = null)
                        : base(meshType, materialIndex, useMeshColors, normalOffset, mode)
                    { this.meshRenderer = meshRenderer; }
                    internal override ShapeModule Set(ShapeModule module) => base.Set(module).Set(m => m.meshRenderer = meshRenderer);
                }
                public class SkinnedMeshRenderer : MeshShape
                {
                    public override ParticleSystemShapeType Type => ParticleSystemShapeType.SkinnedMeshRenderer;
                    private readonly UnityEngine.SkinnedMeshRenderer skinnedMeshRenderer;
                    public SkinnedMeshRenderer(UnityEngine.SkinnedMeshRenderer skinnedMeshRenderer, ParticleSystemMeshShapeType meshType, int? materialIndex, bool useMeshColors, float normalOffset, ShapeMultiMode mode = null)
                        : base(meshType, materialIndex, useMeshColors, normalOffset, mode)
                    { this.skinnedMeshRenderer = skinnedMeshRenderer; }
                    internal override ShapeModule Set(ShapeModule module) => base.Set(module).Set(m => m.skinnedMeshRenderer = skinnedMeshRenderer);
                }
                public abstract class SpriteShape : Shape
                {
                    private readonly ParticleSystemMeshShapeType SpriteType;
                    private readonly float NormalOffset;
                    public SpriteShape(ParticleSystemMeshShapeType spriteType, float normalOffset)
                    {
                        SpriteType = spriteType;
                        NormalOffset = normalOffset;
                    }
                    internal override ShapeModule Set(ShapeModule module) => base.Set(module).Set(m =>
                    {
                        m.meshShapeType = SpriteType;
                        m.normalOffset = NormalOffset;
                    });
                }
                public class Sprite : SpriteShape
                {
                    public override ParticleSystemShapeType Type => ParticleSystemShapeType.Sprite;
                    private readonly UnityEngine.Sprite sprite;
                    public Sprite(UnityEngine.Sprite sprite, ParticleSystemMeshShapeType spriteType, float normalOffset) : base(spriteType, normalOffset)
                    { this.sprite = sprite; }
                    internal override ShapeModule Set(ShapeModule module) => base.Set(module).Set(m => m.sprite = sprite);
                }
                public class SpriteRenderer : SpriteShape
                {
                    public override ParticleSystemShapeType Type => ParticleSystemShapeType.SpriteRenderer;
                    private readonly UnityEngine.SpriteRenderer spriteRenderer;
                    public SpriteRenderer(UnityEngine.SpriteRenderer spriteRenderer, ParticleSystemMeshShapeType spriteType, float normalOffset) : base(spriteType, normalOffset)
                    { this.spriteRenderer = spriteRenderer; }
                    internal override ShapeModule Set(ShapeModule module) => base.Set(module).Set(m => m.spriteRenderer = spriteRenderer);
                }

                public ShapeModule SetShape(Shape shape) => shape.Set(this);
                public ShapeModule SetTexture(Texture2D value) => Set(m => m.texture = value);
                public ShapeModule SetPosition(float? x = null, float? y = null, float? z = null) => Set((m, v) => m.position = v, m => m.position, (x, y, z));
                public ShapeModule SetPosition(Vector3 value) => SetPosition(value.x, value.y, value.z);
                public ShapeModule SetRotation(float? x = null, float? y = null, float? z = null) => Set((m, v) => m.rotation = v, m => m.rotation, (x, y, z));
                public ShapeModule SetScale(float? x = null, float? y = null, float? z = null) => Set((m, v) => m.scale = v, m => m.scale, (x, y, z));
                public ShapeModule SetAlignToDirection(bool value) => Set(m => m.alignToDirection = value);
                public ShapeModule SetRandomizeDirection(float value) => Set(m => m.randomDirectionAmount = value);
                public ShapeModule SetSpherizeDirection(float value) => Set(m => m.sphericalDirectionAmount = value);
                public ShapeModule SetRandomizePosition(float value) => Set(m => m.randomPositionAmount = value);

                public ShapeModule SetAbsolutePosition(float? x = null, float? y = null, float? z = null) => SetShape(new Sphere(0.001f, 1));
            }
            public ShapeModule Shape;

            public class VelocityOverLifetimeModule : Module<VelocityOverLifetimeModule, ParticleSystem.VelocityOverLifetimeModule>
            {
                protected override ParticleSystem.VelocityOverLifetimeModule ParticleModule => handler.System.velocityOverLifetime;

                protected override Action<ParticleSystem.VelocityOverLifetimeModule, bool> SetActive => (m, enabled) => m.enabled = enabled;

                public VelocityOverLifetimeModule(SystemHandler handler) : base(handler) { }

                public VelocityOverLifetimeModule SetVelocity(Curve3D value) => Set((m, v) => (m.x, m.y, m.z) = v, m => (m.x, m.y, m.z), value);
                public VelocityOverLifetimeModule SetVelocity(Curve x = null, Curve y = null, Curve z = null) => SetVelocity((x, y, z));
                public VelocityOverLifetimeModule SetSpace(ParticleSystemSimulationSpace value) => Set(m => m.space = value);
                public VelocityOverLifetimeModule SetOrbital(Curve3D value) => Set((m, v) => (m.orbitalX, m.orbitalY, m.orbitalZ) = v, m => (m.orbitalX, m.orbitalY, m.orbitalZ), value);
                public VelocityOverLifetimeModule SetOrbital(Curve x = null, Curve y = null, Curve z = null) => SetOrbital((x, y, z));
                public VelocityOverLifetimeModule SetOrbitalOffset(Curve3D value) => Set((m, v) => (m.orbitalOffsetX, m.orbitalOffsetY, m.orbitalOffsetZ) = v, m => (m.orbitalOffsetX, m.orbitalOffsetY, m.orbitalOffsetZ), value);
                public VelocityOverLifetimeModule SetOrbitalOffset(Curve x = null, Curve y = null, Curve z = null) => SetOrbitalOffset((x, y, z));
                public VelocityOverLifetimeModule SetRadial(Curve value) => Set(m => m.radial = value);
                public VelocityOverLifetimeModule SetSpeed(Curve value) => Set(m => m.speedModifier = value);
            }
            public VelocityOverLifetimeModule VelocityOverLifetime;

            public class LimitVelocityOverLifetimeModule : Module<LimitVelocityOverLifetimeModule, ParticleSystem.LimitVelocityOverLifetimeModule>
            {
                protected override ParticleSystem.LimitVelocityOverLifetimeModule ParticleModule => handler.System.limitVelocityOverLifetime;

                protected override Action<ParticleSystem.LimitVelocityOverLifetimeModule, bool> SetActive => (m, enabled) => m.enabled = enabled;

                public LimitVelocityOverLifetimeModule(SystemHandler handler) : base(handler) { }

                public LimitVelocityOverLifetimeModule SetSpeed(Curve value) => Set((m, v) => (m.limit, m.separateAxes) = v, m => m.limit, value);
                public LimitVelocityOverLifetimeModule SetSpeed(Curve3D value, ParticleSystemSimulationSpace space = ParticleSystemSimulationSpace.Local) =>
                     Set((m, v) => (m.limitX, m.limitY, m.limitZ, m.separateAxes) = v, m => (m.limitX, m.limitY, m.limit), value)
                    .Set(m => m.space = space);
                public LimitVelocityOverLifetimeModule SetSpeed(Curve x = null, Curve y = null, Curve z = null, ParticleSystemSimulationSpace space = ParticleSystemSimulationSpace.Local) => SetSpeed((x, y, z), space);
                public LimitVelocityOverLifetimeModule SetDampen(float value) => Set(m => m.dampen = value);
                public LimitVelocityOverLifetimeModule SetDrag(Curve value) => Set(m => m.drag = value);
                public LimitVelocityOverLifetimeModule SetMultiplyBySize(bool value) => Set(m => m.multiplyDragByParticleSize = value);
                public LimitVelocityOverLifetimeModule SetMultiplyByVelocity(bool value) => Set(m => m.multiplyDragByParticleVelocity = value);
            }
            public LimitVelocityOverLifetimeModule LimitVelocityOverLifetime;

            public class InheritVelocityModule : Module<InheritVelocityModule, ParticleSystem.InheritVelocityModule>
            {
                protected override ParticleSystem.InheritVelocityModule ParticleModule => handler.System.inheritVelocity;

                protected override Action<ParticleSystem.InheritVelocityModule, bool> SetActive => (m, enabled) => m.enabled = enabled;

                public InheritVelocityModule(SystemHandler handler) : base(handler) { }

                public InheritVelocityModule SetMode(ParticleSystemInheritVelocityMode mode) => Set(m => m.mode = mode);
                public InheritVelocityModule SetMultiplier(Curve value) => Set(m => m.curve = value);
            }
            public InheritVelocityModule InheritVelocity;

            public class LifetimeByEmitterSpeedModule : Module<LifetimeByEmitterSpeedModule, ParticleSystem.LifetimeByEmitterSpeedModule>
            {
                protected override ParticleSystem.LifetimeByEmitterSpeedModule ParticleModule => handler.System.lifetimeByEmitterSpeed;

                protected override Action<ParticleSystem.LifetimeByEmitterSpeedModule, bool> SetActive => (m, enabled) => m.enabled = enabled;

                public LifetimeByEmitterSpeedModule(SystemHandler handler) : base(handler) { }

                public LifetimeByEmitterSpeedModule SetMultiplier(Curve value) => Set(m => m.curve = value);
                public LifetimeByEmitterSpeedModule SetRange(float? x = null, float? y = null) => Set((m, v) => m.range = v, m => m.range, (x, y));
            }
            public LifetimeByEmitterSpeedModule LifetimeByEmitterSpeed;

            public class ForceOverLifetimeModule : Module<ForceOverLifetimeModule, ParticleSystem.ForceOverLifetimeModule>
            {
                protected override ParticleSystem.ForceOverLifetimeModule ParticleModule => handler.System.forceOverLifetime;

                protected override Action<ParticleSystem.ForceOverLifetimeModule, bool> SetActive => (m, enabled) => m.enabled = enabled;

                public ForceOverLifetimeModule(SystemHandler handler) : base(handler) { }

                public ForceOverLifetimeModule SetForce(Curve3D value) => Set((m, v) => (m.x, m.y, m.z) = v, m => (m.x, m.y, m.z), value);
                public ForceOverLifetimeModule SetForce(Curve x = null, Curve y = null, Curve z = null) => SetForce((x, y, z));
                public ForceOverLifetimeModule SetSpace(ParticleSystemSimulationSpace space) => Set(m => m.space = space);
                public ForceOverLifetimeModule SetRandomize(bool value) => Set(m => m.randomized = value);
            }
            public ForceOverLifetimeModule ForceOverLifetime;

            public class ColorOverLifetimeModule : Module<ColorOverLifetimeModule, ParticleSystem.ColorOverLifetimeModule>
            {
                protected override ParticleSystem.ColorOverLifetimeModule ParticleModule => handler.System.colorOverLifetime;

                protected override Action<ParticleSystem.ColorOverLifetimeModule, bool> SetActive => (m, enabled) => m.enabled = enabled;

                public ColorOverLifetimeModule(SystemHandler handler) : base(handler) { }

                public ColorOverLifetimeModule SetColor(MinMaxGradient value) => Set(m => m.color = value);
            }
            public ColorOverLifetimeModule ColorOverLifetime;

            public class ColorBySpeedModule : Module<ColorBySpeedModule, ParticleSystem.ColorBySpeedModule>
            {
                protected override ParticleSystem.ColorBySpeedModule ParticleModule => handler.System.colorBySpeed;

                protected override Action<ParticleSystem.ColorBySpeedModule, bool> SetActive => (m, enabled) => m.enabled = enabled;

                public ColorBySpeedModule(SystemHandler handler) : base(handler) { }

                public ColorBySpeedModule SetColor(MinMaxGradient value) => Set(m => m.color = value);
                public ColorBySpeedModule SetSpeed(float? x = null, float? y = null) => Set((m, v) => m.range = v, m => m.range, (x, y));
            }
            public ColorBySpeedModule ColorBySpeed;

            public class SizeOverLifetimeModule : Module<SizeOverLifetimeModule, ParticleSystem.SizeOverLifetimeModule>
            {
                protected override ParticleSystem.SizeOverLifetimeModule ParticleModule => handler.System.sizeOverLifetime;

                protected override Action<ParticleSystem.SizeOverLifetimeModule, bool> SetActive => (m, enabled) => m.enabled = enabled;

                public SizeOverLifetimeModule(SystemHandler handler) : base(handler) { }

                public SizeOverLifetimeModule SetSize(Curve value) => Set((m, v) => (m.size, m.separateAxes) = v, m => m.size, value);
                public SizeOverLifetimeModule SetSize(Curve3D value) => Set((m, v) => (m.x, m.y, m.z, m.separateAxes) = v, m => (m.x, m.y, m.z), value);
                public SizeOverLifetimeModule SetSize(Curve x = null, Curve y = null, Curve z = null) => SetSize((x, y, z));
            }
            public SizeOverLifetimeModule SizeOverLifetime;

            public class SizeBySpeedModule : Module<SizeBySpeedModule, ParticleSystem.SizeBySpeedModule>
            {
                protected override ParticleSystem.SizeBySpeedModule ParticleModule => handler.System.sizeBySpeed;

                protected override Action<ParticleSystem.SizeBySpeedModule, bool> SetActive => (m, enabled) => m.enabled = enabled;

                public SizeBySpeedModule(SystemHandler handler) : base(handler) { }

                public SizeBySpeedModule SetSize(Curve value) => Set((m, v) => (m.size, m.separateAxes) = v, m => m.size, value);
                public SizeBySpeedModule SetSize(Curve3D value) => Set((m, v) => (m.x, m.y, m.z, m.separateAxes) = v, m => (m.x, m.y, m.z), value);
                public SizeBySpeedModule SetSize(Curve x = null, Curve y = null, Curve z = null) => SetSize((x, y, z));
                public SizeBySpeedModule SetRange(float? x = null, float? y = null) => Set((m, v) => m.range = v, m => m.range, (x, y));
            }
            public SizeBySpeedModule SizeBySpeed;

            public class RotationOverLifetimeModule : Module<RotationOverLifetimeModule, ParticleSystem.RotationOverLifetimeModule>
            {
                protected override ParticleSystem.RotationOverLifetimeModule ParticleModule => handler.System.rotationOverLifetime;

                protected override Action<ParticleSystem.RotationOverLifetimeModule, bool> SetActive => (m, enabled) => m.enabled = enabled;

                public RotationOverLifetimeModule(SystemHandler handler) : base(handler) { }

                public RotationOverLifetimeModule SetRotation(Curve value) => Set((m, v) => (m.z, m.separateAxes) = v, m => m.z, value);
                public RotationOverLifetimeModule SetRotation(Curve3D value) => Set((m, v) => (m.x, m.y, m.z, m.separateAxes) = v, m => (m.x, m.y, m.z), value);
                public RotationOverLifetimeModule SetRotation(Curve x = null, Curve y = null, Curve z = null) => SetRotation((x, y, z));
            }
            public RotationOverLifetimeModule RotationOverLifetime;

            public class RotationBySpeedModule : Module<RotationBySpeedModule, ParticleSystem.RotationBySpeedModule>
            {
                protected override ParticleSystem.RotationBySpeedModule ParticleModule => handler.System.rotationBySpeed;

                protected override Action<ParticleSystem.RotationBySpeedModule, bool> SetActive => (m, enabled) => m.enabled = enabled;

                public RotationBySpeedModule(SystemHandler handler) : base(handler) { }

                public RotationBySpeedModule SetRotation(Curve value) => Set((m, v) => (m.z, m.separateAxes) = v, m => m.z, value);
                public RotationBySpeedModule SetRotation(Curve3D value) => Set((m, v) => (m.x, m.y, m.z, m.separateAxes) = v, m => (m.x, m.y, m.z), value);
                public RotationBySpeedModule SetRotation(Curve x = null, Curve y = null, Curve z = null) => SetRotation((x, y, z));
                public RotationBySpeedModule SetRange(float? x = null, float? y = null) => Set((m, v) => m.range = v, m => m.range, (x, y));
            }
            public RotationBySpeedModule RotationBySpeed;

            public class ExternalForcesModule : Module<ExternalForcesModule, ParticleSystem.ExternalForcesModule>
            {
                protected override ParticleSystem.ExternalForcesModule ParticleModule => handler.System.externalForces;

                protected override Action<ParticleSystem.ExternalForcesModule, bool> SetActive => (m, enabled) => m.enabled = enabled;

                public ExternalForcesModule(SystemHandler handler) : base(handler) { }

                public ExternalForcesModule SetMultiplier(Curve value) => Set(m => m.multiplierCurve = value);
                public ExternalForcesModule SetInfluence(LayerMask mask) => Set(m => m.influenceFilter = ParticleSystemGameObjectFilter.LayerMask).Set(m => m.influenceMask = mask);
                public ExternalForcesModule SetInfluence(params ParticleSystemForceField[] values) => Set(m => m.influenceFilter = ParticleSystemGameObjectFilter.List).Set(m =>
                {
                    m.RemoveAllInfluences();
                    foreach (var value in values) m.AddInfluence(value);
                });
                public ExternalForcesModule SetInfluence(LayerMask mask, params ParticleSystemForceField[] values) => Set(m => m.influenceFilter = ParticleSystemGameObjectFilter.LayerMaskAndList)
                    .SetInfluence(mask).SetInfluence(values);
            }
            public ExternalForcesModule ExternalForces;

            public class NoiseModule : Module<NoiseModule, ParticleSystem.NoiseModule>
            {
                protected override ParticleSystem.NoiseModule ParticleModule => handler.System.noise;

                protected override Action<ParticleSystem.NoiseModule, bool> SetActive => (m, enabled) => m.enabled = enabled;

                public NoiseModule(SystemHandler handler) : base(handler) { }

                public NoiseModule SetStrength(Curve value) => Set((m, v) => (m.strength, m.separateAxes) = v, m => m.strength, value);
                public NoiseModule SetStrength(Curve3D value) => Set((m, v) => (m.strengthX, m.strengthY, m.strengthZ, m.separateAxes) = v, m => (m.strengthX, m.strengthY, m.strengthZ), value);
                public NoiseModule SetStrength(Curve x = null, Curve y = null, Curve z = null) => SetStrength((x, y, z));
                public NoiseModule SetFrequency(float value) => Set(m => m.frequency = value);
                public NoiseModule SetScrollSpeed(Curve value) => Set(m => m.scrollSpeed = value);
                public NoiseModule SetDamping(bool value) => Set(m => m.damping = value);
                public NoiseModule SetOctaves(int count, float multiplier, float scale) => Set(m => { m.octaveCount = count; m.octaveMultiplier = multiplier; m.octaveScale = scale; });
                public NoiseModule SetQuality(ParticleSystemNoiseQuality quality) => Set(m => m.quality = quality);
                public NoiseModule SetPositionAmount(Curve value) => Set(m => m.positionAmount = value);
                public NoiseModule SetRotationnAmount(Curve value) => Set(m => m.rotationAmount = value);
                public NoiseModule SetSizeAmount(Curve value) => Set(m => m.sizeAmount = value);
            }
            public NoiseModule Noise;

            public class CollisionModule : Module<CollisionModule, ParticleSystem.CollisionModule>
            {
                protected override ParticleSystem.CollisionModule ParticleModule => handler.System.collision;

                protected override Action<ParticleSystem.CollisionModule, bool> SetActive => (m, enabled) => m.enabled = enabled;

                public CollisionModule(SystemHandler handler) : base(handler) { }

                public CollisionModule SetQuality(Settings.CollisionQuality quality) => Set(m =>
                {
                    switch (quality)
                    {
                        case Data.Settings.CollisionQuality.Off:
                            m.enabled = false;
                            break;
                        case Data.Settings.CollisionQuality.Low:
                            m.quality = ParticleSystemCollisionQuality.Low;
                            break;
                        case Data.Settings.CollisionQuality.Medium:
                            m.quality = ParticleSystemCollisionQuality.Medium;
                            break;
                        case Data.Settings.CollisionQuality.High:
                            m.quality = ParticleSystemCollisionQuality.High;
                            break;
                    }
                });
            }
            public CollisionModule Collision;

            public class TriggerModule : Module<TriggerModule, ParticleSystem.TriggerModule>
            {
                protected override ParticleSystem.TriggerModule ParticleModule => handler.System.trigger;

                protected override Action<ParticleSystem.TriggerModule, bool> SetActive => (m, enabled) => m.enabled = enabled;

                public TriggerModule(SystemHandler handler) : base(handler) { }

                public TriggerModule AddTriggers(params Component[] values) => Set(m =>
                {
                    foreach (var value in values) m.AddCollider(value);
                });
                public TriggerModule SetTriggers(params Component[] values) => Set(m =>
                {
                    while (m.colliderCount > 0) m.RemoveCollider(0);
                    AddTriggers(values);
                });
                public TriggerModule SetInside(ParticleSystemOverlapAction value) => Set(m => m.inside = value);
                public TriggerModule SetOutside(ParticleSystemOverlapAction value) => Set(m => m.outside = value);
                public TriggerModule SetEnter(ParticleSystemOverlapAction value) => Set(m => m.enter = value);
                public TriggerModule SetExit(ParticleSystemOverlapAction value) => Set(m => m.exit = value);
                public TriggerModule SetQueryMode(ParticleSystemColliderQueryMode mode) => Set(m => m.colliderQueryMode = mode);
                public TriggerModule SetRadius(float value) => Set(m => m.radiusScale = value);
            }
            public TriggerModule Trigger;

            public class SubEmittersModule : Module<SubEmittersModule, ParticleSystem.SubEmittersModule>
            {
                protected override ParticleSystem.SubEmittersModule ParticleModule => handler.System.subEmitters;

                protected override Action<ParticleSystem.SubEmittersModule, bool> SetActive => (m, enabled) => m.enabled = enabled;

                public SubEmittersModule(SystemHandler handler) : base(handler) { }

                public SubEmittersModule AddEmitters(params (ParticleSystem system, ParticleSystemSubEmitterType type, ParticleSystemSubEmitterProperties properties, float probability)[] values) => Set(m =>
                {
                    foreach (var value in values) m.AddSubEmitter(value.system, value.type, value.properties, value.probability);
                });
                public SubEmittersModule SetEmitters(params (ParticleSystem system, ParticleSystemSubEmitterType type, ParticleSystemSubEmitterProperties properties, float probability)[] values) => Set(m =>
                {
                    while (m.subEmittersCount > 0) m.RemoveSubEmitter(0);
                    AddEmitters(values);
                });
            }
            public SubEmittersModule SubEmitters;

            public class TextureSheetAnimationModule : Module<TextureSheetAnimationModule, ParticleSystem.TextureSheetAnimationModule>
            {
                protected override ParticleSystem.TextureSheetAnimationModule ParticleModule => handler.System.textureSheetAnimation;

                protected override Action<ParticleSystem.TextureSheetAnimationModule, bool> SetActive => (m, enabled) => m.enabled = enabled;

                public TextureSheetAnimationModule(SystemHandler handler) : base(handler) { }

                public TextureSheetAnimationModule SetAnimation(int x, int y, ParticleSystemAnimationType type) => Set(m => m.mode = ParticleSystemAnimationMode.Grid)
                    .Set(m => { m.animation = type; m.numTilesX = x; m.numTilesY = y; });
                public TextureSheetAnimationModule SetAnimation(params Sprite[] values) => Set(m =>
                {
                    while (m.spriteCount > 0) m.RemoveSprite(0);
                    foreach (var value in values) m.AddSprite(value);
                });
                public TextureSheetAnimationModule SetTime(Curve lifetime) => Set(m => m.timeMode = ParticleSystemAnimationTimeMode.Lifetime).Set(m => m.frameOverTime = lifetime);
                public TextureSheetAnimationModule SetTime(float min, float max) => Set(m => m.timeMode = ParticleSystemAnimationTimeMode.Speed).Set(m => m.speedRange = new Vector2(min, max));
                public TextureSheetAnimationModule SetTime(float fps) => Set(m => m.timeMode = ParticleSystemAnimationTimeMode.FPS).Set(m => m.fps = fps);
                public TextureSheetAnimationModule SetStartFrame(Curve value) => Set(m => m.startFrame = value);
                public TextureSheetAnimationModule SetUV(UVChannelFlags mask) => Set(m => m.uvChannelMask = mask);
            }
            public TextureSheetAnimationModule TextureSheetAnimation;

            public class LightsModule : Module<LightsModule, ParticleSystem.LightsModule>
            {
                protected override ParticleSystem.LightsModule ParticleModule => handler.System.lights;

                protected override Action<ParticleSystem.LightsModule, bool> SetActive => (m, enabled) => m.enabled = enabled;

                public LightsModule(SystemHandler handler) : base(handler) { }

                public LightsModule SetLight(Light value) => Set(m => m.light = value);
                public LightsModule SetRatio(float value) => Set(m => m.ratio = value);
                public LightsModule SetUseRandomDistribution(bool value) => Set(m => m.useRandomDistribution = value);
                public LightsModule SetUseParticleColor(bool value) => Set(m => m.useParticleColor = value);
                public LightsModule SetSizeAffectsRange(bool value) => Set(m => m.sizeAffectsRange = value);
                public LightsModule SetAlphaAffectsIntensity(bool value) => Set(m => m.alphaAffectsIntensity = value);
                public LightsModule SetRange(Curve value) => Set(m => m.range = value);
                public LightsModule SetIntensity(Curve value) => Set(m => m.intensity = value);
                public LightsModule SetMaximumLights(int value) => Set(m => m.maxLights = value);
            }
            public LightsModule Lights;

            public class TrailModule : Module<TrailModule, ParticleSystem.TrailModule>
            {
                protected override ParticleSystem.TrailModule ParticleModule => handler.System.trails;

                protected override Action<ParticleSystem.TrailModule, bool> SetActive => (m, enabled) => m.enabled = enabled;

                public TrailModule(SystemHandler handler) : base(handler) { }

                public TrailModule SetParticles(float ratio, Curve lifetime, float minDistance, bool worldSpace, bool dieWithParticles) => Set(m => m.mode = ParticleSystemTrailMode.PerParticle)
                    .Set(m => { m.ratio = ratio; m.lifetime = lifetime; m.minVertexDistance = minDistance; m.worldSpace = worldSpace; m.dieWithParticles = dieWithParticles; });
                public TrailModule SetRibbon(int count, bool splitSubEmitter, bool attachRibbonsToTransform) => Set(m => m.mode = ParticleSystemTrailMode.Ribbon)
                    .Set(m => { m.ribbonCount = count; m.splitSubEmitterRibbons = splitSubEmitter; m.attachRibbonsToTransform = attachRibbonsToTransform; });
                public TrailModule SetTextureMode(ParticleSystemTrailTextureMode mode) => Set(m => m.textureMode = mode);
                public TrailModule SetTextureScale(float x, float y) => Set(m => m.textureScale = new Vector2(x, y));
                public TrailModule SetSizeAffectsWidth(bool value) => Set(m => m.sizeAffectsWidth = value);
                public TrailModule SetInheritParticleColor(bool value) => Set(m => m.inheritParticleColor = value);
                public TrailModule SetColorOverLifetime(MinMaxGradient value) => Set(m => m.colorOverLifetime = value);
                public TrailModule SetWidth(Curve value) => Set(m => m.widthOverTrail = value);
                public TrailModule SetColorOverTrail(MinMaxGradient value) => Set(m => m.colorOverTrail = value);
                public TrailModule SetGenerateLightingData(bool value) => Set(m => m.generateLightingData = value);
                public TrailModule SetShadowBias(float value) => Set(m => m.shadowBias = value);
            }
            public TrailModule Trails;

            public class CustomDataModule : Module<CustomDataModule, ParticleSystem.CustomDataModule>
            {
                protected override ParticleSystem.CustomDataModule ParticleModule => handler.System.customData;

                protected override Action<ParticleSystem.CustomDataModule, bool> SetActive => (m, enabled) => m.enabled = enabled;

                public CustomDataModule(SystemHandler handler) : base(handler) { }

                private CustomDataModule Set(ParticleSystemCustomData stream, ParticleSystemCustomDataMode mode) => Set(m => m.SetMode(stream, mode));
                public CustomDataModule Set(ParticleSystemCustomData stream, MinMaxGradient color) => Set(stream, ParticleSystemCustomDataMode.Color).Set(m => m.SetColor(stream, color));
                public CustomDataModule Set(ParticleSystemCustomData stream, params Curve[] values) => Set(stream, ParticleSystemCustomDataMode.Vector).Set(m =>
                {
                    m.SetVectorComponentCount(stream, values.Length);
                    for (int i = 0; i < values.Length; i++)
                        m.SetVector(stream, i, values[i]);
                });
            }
            public CustomDataModule CustomData;

            public class RendererModule : Module<RendererModule, ParticleSystemRenderer>
            {
                protected override ParticleSystemRenderer ParticleModule => handler.System.GetComponent<ParticleSystemRenderer>();

                public RendererModule(SystemHandler handler) : base(handler) { }

                public abstract class Renderer
                {
                    protected abstract ParticleSystemRenderMode Mode { get; }
                    internal virtual RendererModule Set(RendererModule module) => module.Set(m => m.renderMode = Mode);
                }

                public class Billboard : Renderer
                {
                    protected override ParticleSystemRenderMode Mode => ParticleSystemRenderMode.Billboard;
                    private readonly float NormalDirection;
                    public Billboard(float normalDirection = 1) { NormalDirection = normalDirection; }
                    internal override RendererModule Set(RendererModule module) => base.Set(module).Set(m => m.normalDirection = NormalDirection);
                }
                public class StretchedBillboard : Billboard
                {
                    protected override ParticleSystemRenderMode Mode => ParticleSystemRenderMode.Stretch;
                    private readonly float CameraScale, SpeedScale, LengthScale;
                    private readonly bool Freeform, RotateWithStretch;
                    public StretchedBillboard(float cameraScale, float speedScale, float lengthScale, bool freeform, bool rotateWithStretch, float normalDirection = 1) : base(normalDirection)
                    {
                        CameraScale = cameraScale; SpeedScale = speedScale; LengthScale = lengthScale;
                        Freeform = freeform; RotateWithStretch = rotateWithStretch;
                    }
                    internal override RendererModule Set(RendererModule module) => base.Set(module).Set(m =>
                    {
                        m.cameraVelocityScale = CameraScale; m.velocityScale = SpeedScale; m.lengthScale = LengthScale;
                        m.freeformStretching = Freeform; m.rotateWithStretchDirection = Freeform && RotateWithStretch;
                    });
                }
                public class HorizontalBillboard : Billboard
                {
                    protected override ParticleSystemRenderMode Mode => ParticleSystemRenderMode.HorizontalBillboard;
                    public HorizontalBillboard(float normalDirection = 1) : base(normalDirection) { }
                }
                public class VerticalBillboard : Billboard
                {
                    protected override ParticleSystemRenderMode Mode => ParticleSystemRenderMode.VerticalBillboard;
                    public VerticalBillboard(float normalDirection = 1) : base(normalDirection) { }
                }
                public class Mesh : Renderer
                {
                    protected override ParticleSystemRenderMode Mode => ParticleSystemRenderMode.Mesh;
                    private readonly ParticleSystemMeshDistribution Distribution;
                    private readonly UnityEngine.Mesh[] Meshes;
                    public Mesh(params UnityEngine.Mesh[] meshes) : this(ParticleSystemMeshDistribution.NonUniformRandom, meshes) { }
                    public Mesh(ParticleSystemMeshDistribution distribution, params UnityEngine.Mesh[] meshes) { Distribution = distribution; Meshes = meshes; }
                    internal override RendererModule Set(RendererModule module) => base.Set(module).Set(m =>
                    {
                        m.meshDistribution = Distribution;
                        m.SetMeshes(Meshes);
                    });
                }
                public class None : Renderer
                {
                    protected override ParticleSystemRenderMode Mode => ParticleSystemRenderMode.None;
                }

                public RendererModule SetRenderer(Renderer renderer) => renderer.Set(this);
                public RendererModule SetSharedMaterial(Material mat) => Set(m => m.sharedMaterial = mat);
                public RendererModule SetSortMode(ParticleSystemSortMode mode) => Set(m => m.sortMode = mode);
                public RendererModule SetMaxParticleSize(float value) => Set(m => m.maxParticleSize = value);
                public RendererModule SetRenderAlignment(ParticleSystemRenderSpace value) => Set(m => m.alignment = value);
                public RendererModule SetFlip(float? x = null, float? y = null, float? z = null) => Set((m, v) => m.flip = v, m => m.flip, (x, y, z));
                public RendererModule SetAllowRoll(bool value) => Set(m => m.allowRoll = value);
                public RendererModule SetPivot(float? x = null, float? y = null, float? z = null) => Set((m, v) => m.pivot = v, m => m.pivot, (x, y, z));
                public RendererModule SetMasking(SpriteMaskInteraction mask) => Set(m => m.maskInteraction = mask);
                public RendererModule SetCastShadows(ShadowCastingMode mode) => Set(m => m.shadowCastingMode = mode);
                public RendererModule SetShadowBias(float value) => Set(m => m.shadowBias = value);
                public RendererModule SetMotionVectors(MotionVectorGenerationMode mode) => Set(m => m.motionVectorGenerationMode = mode);
                public RendererModule SetSortingLayer(int layer) => Set(m => m.sortingLayerID = layer);
                public RendererModule SetOrder(int value) => Set(m => m.sortingOrder = value);
#if UNITY_6000_0_OR_NEWER
                public RendererModule SetMask(RenderingLayerMask mask) => Set(m => m.renderingLayerMask = mask);
#else
                public RendererModule SetMask(uint mask) => Set(m => m.renderingLayerMask = mask);
#endif
            }
            public RendererModule Renderer;
        }
    }
}
