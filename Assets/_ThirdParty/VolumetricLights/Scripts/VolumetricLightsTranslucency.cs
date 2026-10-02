using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VolumetricLights {

    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    [HelpURL("https://kronnect.com/docs/volumetric-lights-urp/")]
    public class VolumetricLightsTranslucency : MonoBehaviour {

        [Tooltip("Uses the same shader assigned to the transparent material to compute translucency colors instead of using an internal simple (but a bit faster) shader.")]
        public bool preserveOriginalShader;

        [Tooltip("Custom translucency intensity multiplier that only applies to this object")]
        public float intensityMultiplier = 1f;

        [Tooltip("The name of the texture property to use for the base texture.")]
        public string baseTexturePropertyName = "_BaseMap";

        [Tooltip("The name of the color property to use for the base color.")]
        public string baseColorPropertyName = "_BaseColor";

        public static readonly List<VolumetricLightsTranslucency> objects = new List<VolumetricLightsTranslucency>();

        [NonSerialized]
        public Renderer theRenderer;

        private void OnEnable () {
            theRenderer = GetComponent<Renderer>();
            if (!objects.Contains(this)) {
                objects.Add(this);
            }
        }

        private void OnDisable () {
            if (objects.Contains(this)) {
                objects.Remove(this);
            }
        }

        private void OnValidate () {
            intensityMultiplier = Mathf.Max(intensityMultiplier, 0);

        }

    }

}

