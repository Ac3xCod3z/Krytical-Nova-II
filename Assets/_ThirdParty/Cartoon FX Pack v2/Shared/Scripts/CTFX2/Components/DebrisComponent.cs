using System;
using UnityEngine;
using CTFX2.Creator.Data;
using CTFX2.Creator.Objects;
using PSD.Creator.Components;
using PSD.Creator.Objects;

namespace CTFX2.Creator.Components
{
    public class DebrisComponent : AmountBillboardComponent<Debris, DebrisObject, Debris.DebrisModule>
    {
        public override Debris Settings => Creator.debris;
        public override ReferencesObject.System Reference => Creator.reference.references.debris;

        private T Get<T>(Func<DebrisBillboardObject, T> billboardValue, Func<DebrisMeshObject, T> meshValue)
        {
            if (Object is DebrisBillboardObject b) return billboardValue(b);
            return meshValue((DebrisMeshObject)Object);
        }
        protected override float? AnimationSpeed => Get<float?>(b => b.speed, m => null);
        protected override bool RandomStartFrame => true;
        protected override bool UseMeshIndexAsFrame => Get(b => false, m => true);

        protected override void ResetSystem()
        {
            base.ResetSystem();

            System.Noise.SetEnabled(false);

            System.Collision.SetQuality(module.overrideCollision ? module.collision : Settings.collision);

            System.Main.SetGravity((Settings.gravity, 2 / 3f));
            System.Main.SetSpeed((module.force, 1 - module.spread));
            System.RotationOverLifetime.SetRotation((module.angularVelocity, -1));

            System.Shape.SetShape(new SystemHandler.ShapeModule.Hemisphere(1, 1));
            System.Shape.SetAbsolutePosition(0, 0, 0);
            System.Shape.SetRotation(x: -90);

            System.Renderer.SetRenderer(Get<SystemHandler.RendererModule.Renderer>(b => new SystemHandler.RendererModule.Billboard(), m => new SystemHandler.RendererModule.Mesh(m.meshes)));

            System.Main.SetRotation(z: (Mathf.PI * 2, 0));

            System.VelocityOverLifetime.SetVelocity(((0, 0), (module.upForce, 1 - module.spread), (0, 0)));
        }
    }
}