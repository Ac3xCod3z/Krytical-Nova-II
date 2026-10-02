using System.Collections.Generic;
using UnityEngine;

namespace GI
{
    public class Lights
    {
        public List<Light> Directional = new List<Light>();
        public List<Light> Point = new List<Light>();

        public void Update()
        {
            var array = GameObject.FindObjectsOfType<Light>();

            Directional.Clear();
            Point.Clear();

            for (int i = 0; i < array.Length; i++)
            {
                var light = array[i];

                switch (light.type)
                {
                    case LightType.Directional:
                        Directional.Add(light);
                        break;

                    case LightType.Point:
                        Point.Add(light);
                        break;
                }
            }
        }
    }
}
