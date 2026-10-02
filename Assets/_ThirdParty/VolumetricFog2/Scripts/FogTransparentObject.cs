#if UNITY_EDITOR
using UnityEditor.SceneManagement;
using UnityEditor;
#endif
using System.Collections.Generic;
using UnityEngine;

namespace VolumetricFogAndMist2 {

    [ExecuteAlways]
    public class FogTransparentObject : MonoBehaviour {

        [Tooltip("Automatically uses the fog volume that contains this object, switching volumes as the object or the fog volumes move. Disable to assign a specific fog volume.")]
        public bool autoFogVolume;

        public VolumetricFog fogVolume;

        Renderer thisRenderer;
        Material[] registeredMats;
        VolumetricFog registeredFogVolume;
        MaterialPropertyBlock propertyBlock;
        bool hadPropertyBlock;

        static readonly List<string> volumeKeywords = new List<string>();
        static readonly List<string> otherVolumeKeywords = new List<string>();
        static readonly HashSet<Material> warnedMaterials = new HashSet<Material>();

        void OnEnable () {
            CheckSettings();
#if UNITY_EDITOR
            // workaround for volumetric effect disappearing when saving the scene
            if (!Application.isPlaying) {
                EditorSceneManager.sceneSaving += OnSceneSaving;
                EditorApplication.update += OnEditorUpdate;
            }
#endif
        }

        void OnDisable () {
#if UNITY_EDITOR
            if (!Application.isPlaying) {
                EditorSceneManager.sceneSaving -= OnSceneSaving;
                EditorApplication.update -= OnEditorUpdate;
            }
#endif
            UnregisterMats();
            ReleasePropertyBlock();
        }

        /// <summary>
        /// Called by the registered fog volume during its update. If the object is no longer inside that volume, looks for the fog volume that now contains it.
        /// </summary>
        public void CheckFogVolumeContainment (Bounds volumeBounds) {
            if (!autoFogVolume || thisRenderer == null) return;
            if (volumeBounds.Contains(thisRenderer.bounds.center)) return;
            AutoFogVolumeCheck();
        }

        public void AutoFogVolumeCheck () {
            if (!autoFogVolume) return;
            VolumetricFog containingVolume = FindContainingFogVolume();
            if (containingVolume != null && containingVolume != registeredFogVolume) {
                fogVolume = containingVolume;
                CheckSettings();
            }
        }

        VolumetricFog FindContainingFogVolume () {
            if (thisRenderer == null) return null;
            Vector3 pos = thisRenderer.bounds.center;
            int fogsCount = VolumetricFog.volumetricFogs.Count;
            VolumetricFog nearest = null;
            float minDistSqr = float.MaxValue;
            for (int k = 0; k < fogsCount; k++) {
                VolumetricFog fog = VolumetricFog.volumetricFogs[k];
                if (fog == null || !fog.isActiveAndEnabled) continue;
                Bounds bounds = fog.GetBounds();
                if (bounds.Contains(pos)) {
                    float distSqr = (bounds.center - pos).sqrMagnitude;
                    if (distSqr < minDistSqr) {
                        minDistSqr = distSqr;
                        nearest = fog;
                    }
                }
            }
            return nearest;
        }

        void OnSceneSaving (UnityEngine.SceneManagement.Scene scene, string path) {
            CheckSettings();
        }


#if UNITY_EDITOR
        void OnEditorUpdate () {
            if (registeredMats == null || registeredMats.Length == 0) return;
            if (registeredMats[0] == null || !registeredMats[0].HasProperty(ShaderParams.Density)) {
                CheckSettings();
            }
        }
#endif

        void OnValidate () {
            CheckSettings();
        }

        void UnregisterMats () {
            if (registeredFogVolume != null) {
                if (registeredMats != null) {
                    for (int i = 0; i < registeredMats.Length; i++) {
                        registeredFogVolume.UnregisterFogMat(registeredMats[i]);
                    }
                }
                registeredFogVolume.UnregisterTransparentObject(this);
            }
            registeredMats = null;
            registeredFogVolume = null;
        }

        void CheckSettings () {
            if (thisRenderer == null) {
                thisRenderer = GetComponent<Renderer>();
                if (thisRenderer == null) return;
            }

            Material[] materials = thisRenderer.sharedMaterials;
            if (materials.Length == 0) return;

            if (autoFogVolume) {
                VolumetricFog containingVolume = FindContainingFogVolume();
                if (containingVolume != null) {
                    fogVolume = containingVolume;
                }
            }
            if (fogVolume == null) {
                if (VolumetricFog.volumetricFogs.Count > 0) {
                    fogVolume = VolumetricFog.volumetricFogs[0];
                }
                if (fogVolume == null) return;
            }

            UnregisterMats();

            for (int i = 0; i < materials.Length; i++) {
                Material mat = materials[i];
                if (mat == null) continue;
                WarnIfSharedAcrossVolumes(mat);
                fogVolume.RegisterFogMat(mat);
            }
            registeredMats = materials;
            registeredFogVolume = fogVolume;
            fogVolume.RegisterTransparentObject(this);
            fogVolume.UpdateMaterialProperties();
        }

        internal bool UsesMaterial (Material mat) {
            if (mat == null || registeredMats == null) return false;
            for (int k = 0; k < registeredMats.Length; k++) {
                if (registeredMats[k] == mat) return true;
            }
            return false;
        }

        // Per-renderer fog data goes into a material property block so the SRP Batcher can't reuse another volume's values (keywords stay on the material)
        public MaterialPropertyBlock PreparePropertyBlock () {
            if (thisRenderer == null) return null;
            if (propertyBlock == null) {
                hadPropertyBlock = thisRenderer.HasPropertyBlock();
                propertyBlock = new MaterialPropertyBlock();
            }
            thisRenderer.GetPropertyBlock(propertyBlock);
            return propertyBlock;
        }

        public void ApplyPropertyBlock () {
            if (thisRenderer != null && propertyBlock != null) {
                thisRenderer.SetPropertyBlock(propertyBlock);
            }
        }

        void ReleasePropertyBlock () {
            if (thisRenderer != null && propertyBlock != null && !hadPropertyBlock) {
                thisRenderer.SetPropertyBlock(null);
            }
            propertyBlock = null;
        }

        void WarnIfSharedAcrossVolumes (Material mat) {
            if (warnedMaterials.Contains(mat)) return;
            int fogsCount = VolumetricFog.volumetricFogs.Count;
            for (int k = 0; k < fogsCount; k++) {
                VolumetricFog other = VolumetricFog.volumetricFogs[k];
                if (other == null || other == fogVolume || !other.UsesFogMat(mat)) continue;
                fogVolume.GetFogMaterialKeywords(volumeKeywords);
                other.GetFogMaterialKeywords(otherVolumeKeywords);
                if (KeywordsMatch(volumeKeywords, otherVolumeKeywords)) continue;
                warnedMaterials.Add(mat);
                Debug.LogWarning($"Volumetric Fog: material '{mat.name}' is shared by transparent objects assigned to fog volumes '{other.name}' and '{fogVolume.name}' which require different shader keywords ({string.Join(", ", otherVolumeKeywords)} vs {string.Join(", ", volumeKeywords)}). Assign a duplicate of the material to the objects of each fog volume to avoid rendering artifacts.", this);
                return;
            }
        }

        static bool KeywordsMatch (List<string> a, List<string> b) {
            int count = a.Count;
            if (count != b.Count) return false;
            for (int k = 0; k < count; k++) {
                if (a[k] != b[k]) return false;
            }
            return true;
        }
    }
}
