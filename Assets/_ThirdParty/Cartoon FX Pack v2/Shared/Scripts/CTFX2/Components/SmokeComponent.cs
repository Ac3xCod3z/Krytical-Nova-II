using UnityEngine;
using CTFX2.Creator.Data;
using CTFX2.Creator.Objects;
using PSD.Creator.Components;
using PSD.Creator.Objects;

namespace CTFX2.Creator.Components
{
    public class SmokeComponent : AmountBillboardComponent<Smoke, SmokeObject, Smoke.SmokeModule>
    {
        public override Smoke Settings => Creator.smoke;
        public override ReferencesObject.System Reference => Creator.reference.references.smoke;

        protected override bool RandomStartFrame => true;

        protected override void ResetSystem()
        {
            base.ResetSystem();

            System.Noise.SetEnabled(false);

            System.Main.SetRotation(x: 0, y: 0, z: (Mathf.PI * 2, 0));

            System.LimitVelocityOverLifetime.SetSpeed(10000);
            System.LimitVelocityOverLifetime.SetDrag(module.drag);

            System.Main.SetSpeed((module.force, 1 - module.spread));

            float x = 0, y = 0, z = 0;
            SystemHandler.ShapeModule.Shape shape = new SystemHandler.ShapeModule.Sphere(0.001f, 1);
            System.Shape.SetRandomizeDirection(0);
            switch (module.shape)
            {
                case Smoke.SmokeModule.Shape.Volume:
                    {
                        break;
                    }

                case Smoke.SmokeModule.Shape.Donut:
                    {
                        shape = new SystemHandler.ShapeModule.Circle(0.001f, 1);
                        y = module.donutYOffset;
                        System.Shape.SetRotation(x: 90);
                        System.Shape.SetRandomizeDirection(0.1f);
                        break;
                    }

                case Smoke.SmokeModule.Shape.Mushroom:
                    {
                        shape = new SystemHandler.ShapeModule.Hemisphere(0.001f, 1);
                        System.Shape.SetRotation(x: -90);
                        System.Shape.SetRandomizeDirection(0.1f);
                        break;
                    }

                case Smoke.SmokeModule.Shape.Hat:
                    {
                        shape = new SystemHandler.ShapeModule.Cone(module.angle, 0.001f, 1);
                        System.Shape.SetRotation(x: -45, y: 45);
                        break;
                    }
            }

            System.Shape.SetShape(shape);
            System.Shape.SetPosition(x, y, z);
        }
    }
}