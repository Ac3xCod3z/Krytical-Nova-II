//------------------------------------------------------------------------------------------------------------------
// Volumetric Fog & Mist 2
// Created by Kronnect
//------------------------------------------------------------------------------------------------------------------
using UnityEngine;
using UnityEngine.Serialization;


namespace VolumetricFogAndMist2 {


    public enum MaskTextureBrushMode {
        AddFog = 0,
        RemoveFog = 1,
        ColorFog = 2
    }


    public enum FoWUpdateMethod {
        MainThread,
        BackgroundThread
    }

    public partial class VolumetricFog : MonoBehaviour {

        public bool enableFogOfWar;
        public Vector3 fogOfWarCenter;
        public bool fogOfWarIsLocal;
        public Vector3 fogOfWarSize = new Vector3(1024, 0, 1024);
        public bool fogOfWarShowCoverage;

        [FormerlySerializedAs("fogOfWarTextureSize")]
        [Range(32, 2048)] public int fogOfWarTextureWidth = 256;
        [Range(32, 2048)] public int fogOfWarTextureHeight;
        [Tooltip("Delay before the fog alpha is restored. A value of 0 keeps the fog cleared forever.")]
        [Range(0, 100)] public float fogOfWarRestoreDelay;
        [Range(0, 25)] public float fogOfWarRestoreDuration = 2f;
        [Range(0, 1)] public float fogOfWarSmoothness = 1f;
        public bool fogOfWarBlur;

        bool canDestroyFOWTexture;

        #region In-Editor fog of war painter

        public bool maskEditorEnabled;
        public MaskTextureBrushMode maskBrushMode = MaskTextureBrushMode.RemoveFog;
        public Color maskBrushColor = Color.white;
        [Range(1, 128)] public int maskBrushWidth = 20;
        [Range(0, 1)] public float maskBrushFuzziness = 0.5f;
        [Range(0, 1)] public float maskBrushOpacity = 0.15f;

        #endregion

        [SerializeField]
        Texture2D _fogOfWarTexture;

        public Vector3 anchoredFogOfWarCenter => fogOfWarIsLocal ? transform.position + fogOfWarCenter : fogOfWarCenter;


        public Texture2D fogOfWarTexture {
            get { return _fogOfWarTexture; }
            set {
                if (_fogOfWarTexture != value) {
                    if (value != null) {
                        _fogOfWarTexture = value;
                        canDestroyFOWTexture = false;
                        ReloadFogOfWarTexture();
                    } else {
                        if (canDestroyFOWTexture && _fogOfWarTexture != null) {
                            DestroyImmediate(_fogOfWarTexture);
                        }
                        _fogOfWarTexture = null;
                        canDestroyFOWTexture = false;
                    }
                    if (fogMat != null) {
                        fogMat.SetTexture(ShaderParams.FogOfWarTexture, _fogOfWarTexture);
                    }
                }
            }
        }


        Color32[] fogOfWarColorBuffer;
        readonly FogOfWarEngine fowEngine = new FogOfWarEngine();
        Material fowBlurMat;
        RenderTexture fowBlur1, fowBlur2;
        bool fogOfWarInitialized;


        void FogOfWarInit () {
            if (fogOfWarInitialized) return;
            if (fogOfWarTextureHeight == 0) {
                fogOfWarTextureHeight = fogOfWarTextureWidth;
            }

            if (_fogOfWarTexture == null) {
                FogOfWarUpdateTexture();
            } else if (enableFogOfWar && (fogOfWarColorBuffer == null || fogOfWarColorBuffer.Length == 0)) {
                ReloadFogOfWarTexture();
            }
            if (_fogOfWarTexture != null && fogOfWarColorBuffer != null && fogOfWarColorBuffer.Length > 0) {
                fowEngine.Setup(fogOfWarColorBuffer, _fogOfWarTexture.width, _fogOfWarTexture.height);
            }
            fogOfWarInitialized = true;
        }

        void EnsureFogOfWarInit () {
            if (_fogOfWarTexture == null) {
                fogOfWarInitialized = false;
                FogOfWarInit();
            }
        }

        void FogOfWarDestroy () {
            fowEngine.Stop();
            if (canDestroyFOWTexture && _fogOfWarTexture != null) {
                DestroyImmediate(_fogOfWarTexture);
                fogOfWarInitialized = false;
            }
            if (fowBlur1 != null) {
                fowBlur1.Release();
            }
            if (fowBlur2 != null) {
                fowBlur2.Release();
            }
            if (fowBlurMat != null) {
                DestroyImmediate(fowBlurMat);
            }
        }

        /// <summary>
        /// Reloads the current contents of the fog of war texture
        /// </summary>
        public void ReloadFogOfWarTexture () {
            if (_fogOfWarTexture == null || profile == null) return;
            fogOfWarTextureWidth = _fogOfWarTexture.width;
            fogOfWarTextureHeight = _fogOfWarTexture.height;
            EnsureTextureIsReadable(_fogOfWarTexture);
            fogOfWarColorBuffer = _fogOfWarTexture.GetPixels32();
            fowEngine.Setup(fogOfWarColorBuffer, _fogOfWarTexture.width, _fogOfWarTexture.height);
            if (!enableFogOfWar) {
                enableFogOfWar = true;
                UpdateMaterialPropertiesNow();
            }
        }


        void EnsureTextureIsReadable (Texture2D tex) {
#if UNITY_EDITOR
            string path = UnityEditor.AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(path))
                return;
            UnityEditor.TextureImporter imp = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
            if (imp != null && !imp.isReadable) {
                imp.isReadable = true;
                imp.SaveAndReimport();
            }
#endif
        }

        void FogOfWarUpdateTexture () {
            if (!enableFogOfWar)
                return;
            int width = GetScaledSize(fogOfWarTextureWidth, 1.0f);
            int height = GetScaledSize(fogOfWarTextureHeight, 1.0f);
            if (_fogOfWarTexture == null || _fogOfWarTexture.width != width || _fogOfWarTexture.height != height) {
                _fogOfWarTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                _fogOfWarTexture.hideFlags = HideFlags.DontSave;
                _fogOfWarTexture.filterMode = FilterMode.Bilinear;
                _fogOfWarTexture.wrapMode = TextureWrapMode.Clamp;
                canDestroyFOWTexture = true;
                ResetFogOfWar();
            }
        }

        int GetScaledSize (int size, float factor) {
            size = (int)(size / factor);
            size /= 4;
            if (size < 1)
                size = 1;
            return size * 4;
        }

        void UpdateFogOfWarMaterialBoundsProperties () {
            if (fogMat == null) return;
            UpdateFogOfWarMaterialBoundsProperties(new FogRenderTarget(fogMat));
        }

        void UpdateFogOfWarMaterialBoundsProperties (FogRenderTarget mat) {
            Vector3 fogOfWarCenter = anchoredFogOfWarCenter;
            mat.SetVector(ShaderParams.FogOfWarCenter, fogOfWarCenter);
            mat.SetVector(ShaderParams.FogOfWarSize, fogOfWarSize);
            Vector3 ca = fogOfWarCenter - 0.5f * fogOfWarSize;
            mat.SetVector(ShaderParams.FogOfWarCenterAdjusted, new Vector4(ca.x / fogOfWarSize.x, 1f, ca.z / (fogOfWarSize.z + 0.0001f), 0));
        }

        /// <summary>
        /// Updates fog of war transitions and uploads texture changes to GPU if required
        /// </summary>
        public void UpdateFogOfWar (bool forceUpload = false) {
            if (!enableFogOfWar) return;
            EnsureFogOfWarInit();
            if (_fogOfWarTexture == null)
                return;

            fowEngine.Tick(Time.time, Application.isPlaying);

            if (fowEngine.dirty || forceUpload) {
                UploadFogOfWarTextureEditsToGPU();
            }

            if (fogOfWarIsLocal) {
                UpdateFogOfWarMaterialBoundsProperties();
            }
        }


        void UploadFogOfWarTextureEditsToGPU () {
            fowEngine.EnterSyncPriority();
            try {
                _fogOfWarTexture.SetPixels32(fogOfWarColorBuffer);
                fowEngine.MarkUploaded();
            } finally {
                fowEngine.ExitSyncPriority();
            }
            _fogOfWarTexture.Apply();

            // Smooth texture
            if (fogOfWarBlur) {
                SetFowBlurTexture();
            }

#if UNITY_EDITOR
            if (!Application.isPlaying) {
                UnityEditor.EditorUtility.SetDirty(_fogOfWarTexture);
            }
#endif
        }

        void SetFowBlurTexture () {
            if (fogMat == null)
                return;

            if (fowBlurMat == null) {
                fowBlurMat = new Material(Shader.Find("Hidden/VolumetricFog2/FoWBlur"));
            }

            if (fowBlur1 == null || fowBlur1.width != _fogOfWarTexture.width || fowBlur2 == null || fowBlur2.width != _fogOfWarTexture.width) {
                CreateFoWBlurRTs();
            }
            fowBlur1.DiscardContents();
            Graphics.Blit(_fogOfWarTexture, fowBlur1, fowBlurMat, 0);
            fowBlur2.DiscardContents();
            Graphics.Blit(fowBlur1, fowBlur2, fowBlurMat, 1);
            fogMat.SetTexture(ShaderParams.FogOfWarTexture, fowBlur2);
        }


        void CreateFoWBlurRTs () {
            if (fowBlur1 != null) {
                fowBlur1.Release();
            }
            if (fowBlur2 != null) {
                fowBlur2.Release();
            }
            RenderTextureDescriptor desc = new RenderTextureDescriptor(_fogOfWarTexture.width, _fogOfWarTexture.height, RenderTextureFormat.ARGB32, 0);
            fowBlur1 = new RenderTexture(desc);
            fowBlur2 = new RenderTexture(desc);
        }

        bool RunsSynchronously (FoWUpdateMethod updateMethod) {
            return updateMethod == FoWUpdateMethod.MainThread || !Application.isPlaying;
        }

        /// <summary>
        /// Changes the alpha value of the fog of war at world position creating a transition from current alpha value to specified target alpha. It takes into account FogOfWarCenter and FogOfWarSize.
        /// Note that only x and z coordinates are used. Y (vertical) coordinate is ignored.
        /// </summary>
        /// <param name="worldPosition">in world space coordinates.</param>
        /// <param name="radius">radius of application in world units.</param>
        /// <param name="fogNewAlpha">target alpha value.</param>
        /// <param name="duration">duration of transition in seconds (0 = apply fogNewAlpha instantly).</param>
        public void SetFogOfWarAlpha (Vector3 worldPosition, float radius, float fogNewAlpha, float duration = 0) {
            SetFogOfWarAlpha(worldPosition, radius, fogNewAlpha, true, duration, fogOfWarSmoothness, fogOfWarRestoreDelay, fogOfWarRestoreDuration);
        }


        /// <summary>
        /// Changes the alpha value of the fog of war at world position creating a transition from current alpha value to specified target alpha. It takes into account FogOfWarCenter and FogOfWarSize.
        /// Note that only x and z coordinates are used. Y (vertical) coordinate is ignored.
        /// </summary>
        /// <param name="worldPosition">in world space coordinates.</param>
        /// <param name="radius">radius of application in world units.</param>
        /// <param name="fogNewAlpha">target alpha value.</param>
        /// <param name="duration">duration of transition in seconds (0 = apply fogNewAlpha instantly).</param>
        /// <param name="smoothness">border smoothness (0 = sharp borders, 1 = smooth transition)</param>
        public void SetFogOfWarAlpha (Vector3 worldPosition, float radius, float fogNewAlpha, float duration, float smoothness) {
            SetFogOfWarAlpha(worldPosition, radius, fogNewAlpha, true, duration, smoothness, fogOfWarRestoreDelay, fogOfWarRestoreDuration);
        }

        /// <summary>
        /// Changes the alpha value of the fog of war at world position creating a transition from current alpha value to specified target alpha. It takes into account FogOfWarCenter and FogOfWarSize.
        /// Note that only x and z coordinates are used. Y (vertical) coordinate is ignored.
        /// </summary>
        /// <param name="worldPosition">in world space coordinates.</param>
        /// <param name="radius">radius of application in world units.</param>
        /// <param name="blendAlpha">if new alpha is combined with preexisting alpha value or replaced.</param>
        /// <param name="fogNewAlpha">target alpha value.</param>
        /// <param name="duration">duration of transition in seconds (0 = apply fogNewAlpha instantly).</param>
        /// <param name="smoothness">border smoothness (0 = sharp borders, 1 = smooth transition)</param>
        /// <param name="updateMethod">if method should run on main thread (immediate effect) or in a background thread (update can be slower but doesn't affect main thread performance)</param>
        public void SetFogOfWarAlpha (Vector3 worldPosition, float radius, float fogNewAlpha, float duration, float smoothness, FoWUpdateMethod updateMethod = FoWUpdateMethod.BackgroundThread, bool blendAlpha = true) {
            SetFogOfWarAlpha(worldPosition, radius, fogNewAlpha, blendAlpha, duration, smoothness, fogOfWarRestoreDelay, fogOfWarRestoreDuration, restoreToAlphaValue: 1f, updateMethod);
        }

        /// <summary>
        /// Changes the alpha value of the fog of war at world position creating a transition from current alpha value to specified target alpha. It takes into account FogOfWarCenter and FogOfWarSize.
        /// Note that only x and z coordinates are used. Y (vertical) coordinate is ignored.
        /// </summary>
        /// <param name="worldPosition">in world space coordinates.</param>
        /// <param name="radius">radius of application in world units.</param>
        /// <param name="fogNewAlpha">target alpha value.</param>
        /// <param name="blendAlpha">if new alpha is combined with preexisting alpha value or replaced.</param>
        /// <param name="duration">duration of transition in seconds (0 = apply fogNewAlpha instantly).</param>
        /// <param name="smoothness">border smoothness.</param>
        /// <param name="restoreDelay">delay before the fog alpha is restored. Pass 0 to keep change forever.</param>
        /// <param name="restoreDuration">restore duration in seconds.</param>
        /// <param name="restoreToAlphaValue">final alpha value when fog restores.</param>
        /// <param name="updateMethod">if method should run on main thread (immediate effect) or in a background thread (update can be slower but doesn't affect main thread performance)</param>
        public void SetFogOfWarAlpha (Vector3 worldPosition, float radius, float fogNewAlpha, bool blendAlpha, float duration, float smoothness, float restoreDelay, float restoreDuration, float restoreToAlphaValue = 1f, FoWUpdateMethod updateMethod = FoWUpdateMethod.BackgroundThread) {

            EnsureFogOfWarInit();
            if (_fogOfWarTexture == null || fogOfWarColorBuffer == null || fogOfWarColorBuffer.Length == 0)
                return;

            FowCommand cmd = new FowCommand {
                type = FowCommandType.StampCircle,
                fowCenter = anchoredFogOfWarCenter,
                fowSize = fogOfWarSize,
                position = worldPosition,
                radius = radius,
                newAlpha = fogNewAlpha,
                blendAlpha = blendAlpha,
                duration = duration,
                smoothness = smoothness,
                restoreDelay = restoreDelay,
                restoreDuration = restoreDuration,
                restoreToAlpha = restoreToAlphaValue,
                timestamp = Time.time
            };
            fowEngine.Submit(cmd, RunsSynchronously(updateMethod));
        }

        /// <summary>
        /// Changes the alpha value of the fog of war within bounds creating a transition from current alpha value to specified target alpha. It takes into account FogOfWarCenter and FogOfWarSize.
        /// Note that only x and z coordinates are used. Y (vertical) coordinate is ignored.
        /// </summary>
        /// <param name="bounds">in world space coordinates.</param>
        /// <param name="fogNewAlpha">target alpha value.</param>
        /// <param name="duration">duration of transition in seconds (0 = apply fogNewAlpha instantly).</param>
        public void SetFogOfWarAlpha (Bounds bounds, float fogNewAlpha, float duration = 0) {
            SetFogOfWarAlpha(bounds, fogNewAlpha, false, duration, fogOfWarSmoothness, fogOfWarRestoreDelay, fogOfWarRestoreDuration);
        }

        /// <summary>
        /// Changes the alpha value of the fog of war within bounds creating a transition from current alpha value to specified target alpha. It takes into account FogOfWarCenter and FogOfWarSize.
        /// Note that only x and z coordinates are used. Y (vertical) coordinate is ignored.
        /// </summary>
        /// <param name="bounds">in world space coordinates.</param>
        /// <param name="fogNewAlpha">target alpha value.</param>
        /// <param name="duration">duration of transition in seconds (0 = apply fogNewAlpha instantly).</param>
        /// <param name="smoothness">border smoothness.</param>
        public void SetFogOfWarAlpha (Bounds bounds, float fogNewAlpha, float duration, float smoothness) {
            SetFogOfWarAlpha(bounds, fogNewAlpha, false, duration, smoothness, fogOfWarRestoreDelay, fogOfWarRestoreDuration);
        }


        /// <summary>
        /// Changes the alpha value of the fog of war within bounds creating a transition from current alpha value to specified target alpha. It takes into account FogOfWarCenter and FogOfWarSize.
        /// Note that only x and z coordinates are used. Y (vertical) coordinate is ignored.
        /// </summary>
        /// <param name="bounds">in world space coordinates.</param>
        /// <param name="fogNewAlpha">target alpha value (0-1).</param>
        /// <param name="blendAlpha">if new alpha is combined with preexisting alpha value or replaced.</param>
        /// <param name="duration">duration of transition in seconds (0 = apply fogNewAlpha instantly).</param>
        public void SetFogOfWarAlpha (Bounds bounds, float fogNewAlpha, bool blendAlpha, float duration) {
            SetFogOfWarAlpha(bounds, fogNewAlpha, blendAlpha, 0f, fogOfWarSmoothness, fogOfWarRestoreDelay, fogOfWarRestoreDuration);
        }

        /// <summary>
        /// Changes the alpha value of the fog of war within bounds creating a transition from current alpha value to specified target alpha. It takes into account FogOfWarCenter and FogOfWarSize.
        /// Note that only x and z coordinates are used. Y (vertical) coordinate is ignored.
        /// </summary>
        /// <param name="bounds">in world space coordinates.</param>
        /// <param name="smoothness">border smoothness.</param>
        /// <param name="fuzzyness">randomization of border noise.</param>
        /// <param name="restoreDelay">delay before the fog alpha is restored. Pass 0 to keep change forever.</param>
        /// <param name="restoreDuration">restore duration in seconds.</param>
        /// <param name="restoreToAlpha">alpha value (0-1) for the fog when restore is completed.</param>
        public void SetFogOfWarAlpha (Bounds bounds, float fogNewAlpha, bool blendAlpha, float duration, float smoothness, float restoreDelay, float restoreDuration, float restoreToAlpha = 1f, FoWUpdateMethod updateMethod = FoWUpdateMethod.BackgroundThread) {
            EnsureFogOfWarInit();
            if (_fogOfWarTexture == null || fogOfWarColorBuffer == null || fogOfWarColorBuffer.Length == 0)
                return;

            FowCommand cmd = new FowCommand {
                type = FowCommandType.StampBounds,
                fowCenter = anchoredFogOfWarCenter,
                fowSize = fogOfWarSize,
                position = bounds.center,
                extents = bounds.extents,
                newAlpha = fogNewAlpha,
                blendAlpha = blendAlpha,
                duration = duration,
                smoothness = smoothness,
                restoreDelay = restoreDelay,
                restoreDuration = restoreDuration,
                restoreToAlpha = restoreToAlpha,
                timestamp = Time.time
            };
            fowEngine.Submit(cmd, RunsSynchronously(updateMethod));
        }


        /// <summary>
        /// Changes the alpha value of the fog of war within collider creating a transition from current alpha value to specified target alpha. It takes into account FogOfWarCenter and FogOfWarSize.
        /// Note that only x and z coordinates are used. Y (vertical) coordinate is ignored.
        /// </summary>
        /// <param name="collider">collider used to define the shape of the area where fog of war alpha will be set. Collider must be convex.</param>
        /// <param name="fogNewAlpha">target alpha value (0-1).</param>
        public void SetFogOfWarAlpha (Collider collider, float fogNewAlpha) {
            SetFogOfWarAlpha(collider, fogNewAlpha, false, 0f, fogOfWarSmoothness, fogOfWarRestoreDelay, fogOfWarRestoreDuration, 1f);
        }

        /// <summary>
        /// Changes the alpha value of the fog of war within collider creating a transition from current alpha value to specified target alpha. It takes into account FogOfWarCenter and FogOfWarSize.
        /// Note that only x and z coordinates are used. Y (vertical) coordinate is ignored.
        /// </summary>
        /// <param name="collider">collider used to define the shape of the area where fog of war alpha will be set. Collider must be convex.</param>
        /// <param name="blendAlpha">if new alpha is combined with preexisting alpha value or replaced.</param>
        /// <param name="duration">duration of transition in seconds (0 = apply fogNewAlpha instantly).</param>
        /// <param name="smoothness">border smoothness.</param>
        /// <param name="restoreDelay">delay before the fog alpha is restored. Pass 0 to keep change forever.</param>
        /// <param name="restoreDuration">restore duration in seconds.</param>
        /// <param name="restoreToAlpha">alpha value (0-1) for the fog when restore is completed.</param>
        public void SetFogOfWarAlpha (Collider collider, float fogNewAlpha, bool blendAlpha = false, float duration = 0, float smoothness = 0, float restoreDelay = 0, float restoreDuration = 2, float restoreToAlpha = 1f) {
            EnsureFogOfWarInit();
            if (_fogOfWarTexture == null || fogOfWarColorBuffer == null || fogOfWarColorBuffer.Length == 0)
                return;

            Bounds bounds = collider.bounds;
            FowCommand cmd = new FowCommand {
                fowCenter = anchoredFogOfWarCenter,
                fowSize = fogOfWarSize,
                position = bounds.center,
                extents = bounds.extents,
                newAlpha = fogNewAlpha,
                blendAlpha = blendAlpha,
                duration = duration,
                smoothness = smoothness,
                restoreDelay = restoreDelay,
                restoreDuration = restoreDuration,
                restoreToAlpha = restoreToAlpha,
                timestamp = Time.time
            };
            fowEngine.StampColliderSync(ref cmd, collider, bounds);
        }

        /// <summary>
        /// Changes the alpha value of the fog of war within bounds creating a transition from current alpha value to specified target alpha. It takes into account FogOfWarCenter and FogOfWarSize.
        /// Note that only x and z coordinates are used. Y (vertical) coordinate is ignored.
        /// </summary>
        /// <param name="go">gameobject used to define the shape of the area where fog of war alpha will be set. The gameobject must have a mesh associated.</param>
        /// <param name="fogNewAlpha">target alpha value (0-1).</param>
        public void SetFogOfWarAlpha (GameObject go, float fogNewAlpha) {
            SetFogOfWarAlpha(go, fogNewAlpha, 0f, fogOfWarRestoreDelay, fogOfWarRestoreDuration, 1f);
        }

        /// <summary>
        /// Changes the alpha value of the fog of war within bounds creating a transition from current alpha value to specified target alpha. It takes into account FogOfWarCenter and FogOfWarSize.
        /// Note that only x and z coordinates are used. Y (vertical) coordinate is ignored.
        /// </summary>
        /// <param name="go">gameobject used to define the shape of the area where fog of war alpha will be set. The gameobject must have a mesh associated.</param>
        /// <param name="duration">duration of transition in seconds (0 = apply fogNewAlpha instantly).</param>
        /// <param name="restoreDelay">delay before the fog alpha is restored. Pass 0 to keep change forever.</param>
        /// <param name="restoreDuration">restore duration in seconds.</param>
        /// <param name="restoreToAlpha">alpha value (0-1) for the fog when restore is completed.</param>
        public void SetFogOfWarAlpha (GameObject go, float fogNewAlpha, float duration = 0, float restoreDelay = 0, float restoreDuration = 2, float restoreToAlpha = 1f, FoWUpdateMethod updateMethod = FoWUpdateMethod.BackgroundThread) {
            if (go == null) return;
            SetFogOfWarAlpha(go, null, null, fogNewAlpha, duration, restoreDelay, restoreDuration, restoreToAlpha, updateMethod);
        }

        /// <summary>
        /// Changes the alpha value of the fog of war using a mesh shape at multiple world positions.
        /// The mesh is extracted once and stamped at each position, making this much more efficient than calling the single-position overload in a loop.
        /// Useful for tile-based games (hex grids, etc.) where the same shape is repeated at many locations.
        /// </summary>
        /// <param name="go">gameobject whose mesh defines the shape. The gameobject must have a mesh associated.</param>
        /// <param name="positions">array of world positions where the shape should be applied.</param>
        /// <param name="fogNewAlpha">target alpha value (0-1) applied to all positions.</param>
        /// <param name="duration">duration of transition in seconds (0 = apply fogNewAlpha instantly).</param>
        /// <param name="restoreDelay">delay before the fog alpha is restored. Pass 0 to keep change forever.</param>
        /// <param name="restoreDuration">restore duration in seconds.</param>
        /// <param name="restoreToAlpha">alpha value (0-1) for the fog when restore is completed.</param>
        /// <param name="updateMethod">if method should run on main thread or background thread.</param>
        public void SetFogOfWarAlpha (GameObject go, Vector3[] positions, float fogNewAlpha, float duration = 0, float restoreDelay = 0, float restoreDuration = 2, float restoreToAlpha = 1f, FoWUpdateMethod updateMethod = FoWUpdateMethod.BackgroundThread) {
            if (positions == null || positions.Length == 0) return;
            float[] fogNewAlphas = new float[positions.Length];
            for (int i = 0; i < positions.Length; i++) fogNewAlphas[i] = fogNewAlpha;
            SetFogOfWarAlpha(go, positions, fogNewAlphas, duration, restoreDelay, restoreDuration, restoreToAlpha, updateMethod);
        }

        /// <summary>
        /// Changes the alpha value of the fog of war using a mesh shape at multiple world positions with per-position alpha values.
        /// The mesh is extracted once and stamped at each position, making this much more efficient than calling the single-position overload in a loop.
        /// Useful for tile-based games (hex grids, etc.) where the same shape is repeated at many locations.
        /// </summary>
        /// <param name="go">gameobject whose mesh defines the shape. The gameobject must have a mesh associated.</param>
        /// <param name="positions">array of world positions where the shape should be applied.</param>
        /// <param name="fogNewAlphas">array of target alpha values (0-1), one per position.</param>
        /// <param name="duration">duration of transition in seconds (0 = apply fogNewAlpha instantly).</param>
        /// <param name="restoreDelay">delay before the fog alpha is restored. Pass 0 to keep change forever.</param>
        /// <param name="restoreDuration">restore duration in seconds.</param>
        /// <param name="restoreToAlpha">alpha value (0-1) for the fog when restore is completed.</param>
        /// <param name="updateMethod">if method should run on main thread or background thread.</param>
        public void SetFogOfWarAlpha (GameObject go, Vector3[] positions, float[] fogNewAlphas, float duration = 0, float restoreDelay = 0, float restoreDuration = 2, float restoreToAlpha = 1f, FoWUpdateMethod updateMethod = FoWUpdateMethod.BackgroundThread) {
            if (go == null || positions == null || positions.Length == 0 || fogNewAlphas == null || fogNewAlphas.Length != positions.Length) return;
            SetFogOfWarAlpha(go, positions, fogNewAlphas, 0, duration, restoreDelay, restoreDuration, restoreToAlpha, updateMethod);
        }

        // shared mesh extraction: with null positions the mesh is stamped in place at its own bounds center
        void SetFogOfWarAlpha (GameObject go, Vector3[] positions, float[] fogNewAlphas, float singleAlpha, float duration, float restoreDelay, float restoreDuration, float restoreToAlpha, FoWUpdateMethod updateMethod) {
            EnsureFogOfWarInit();
            if (_fogOfWarTexture == null || fogOfWarColorBuffer == null || fogOfWarColorBuffer.Length == 0)
                return;

            MeshRenderer meshRenderer = go.GetComponentInChildren<MeshRenderer>();
            if (meshRenderer == null) {
                Debug.LogError("No MeshRenderer found on this object.");
                return;
            }

            MeshFilter mf = meshRenderer.GetComponent<MeshFilter>();
            if (mf == null) {
                Debug.LogError("No MeshFilter found on this object.");
                return;
            }
            Mesh mesh = mf.sharedMesh;
            if (mesh == null) {
                Debug.LogError("No Mesh found on this object.");
                return;
            }
            if (mesh.GetTopology(0) != MeshTopology.Triangles) {
                Debug.LogError("Only triangle topology is supported by this tool.");
                return;
            }

            // Get triangle info and transform vertices to world space
            int[] indices = mesh.triangles;
            Vector3[] vertices = mesh.vertices;
            int verticesLength = vertices.Length;
            Transform t = meshRenderer.transform;
            for (int k = 0; k < verticesLength; k++) {
                vertices[k] = t.TransformPoint(vertices[k]);
            }

            // Build 2D triangles in local space (relative to mesh center)
            Bounds bounds = meshRenderer.bounds;
            Vector3 meshCenter = bounds.center;
            int indicesLength = indices.Length;
            Vector2[] localTriangles = new Vector2[indicesLength];
            for (int k = 0; k < indicesLength; k += 3) {
                localTriangles[k].x = vertices[indices[k]].x - meshCenter.x;
                localTriangles[k].y = vertices[indices[k]].z - meshCenter.z;
                localTriangles[k + 1].x = vertices[indices[k + 1]].x - meshCenter.x;
                localTriangles[k + 1].y = vertices[indices[k + 1]].z - meshCenter.z;
                localTriangles[k + 2].x = vertices[indices[k + 2]].x - meshCenter.x;
                localTriangles[k + 2].y = vertices[indices[k + 2]].z - meshCenter.z;
            }

            if (positions == null) {
                positions = new Vector3[] { meshCenter };
                fogNewAlphas = new float[] { singleAlpha };
            }

            FowCommand cmd = new FowCommand {
                type = FowCommandType.StampMeshBatch,
                fowCenter = anchoredFogOfWarCenter,
                fowSize = fogOfWarSize,
                extents = bounds.extents,
                localTriangles = localTriangles,
                indicesLength = indicesLength,
                positions = positions,
                alphas = fogNewAlphas,
                duration = duration,
                restoreDelay = restoreDelay,
                restoreDuration = restoreDuration,
                restoreToAlpha = restoreToAlpha,
                timestamp = Time.time
            };
            fowEngine.Submit(cmd, RunsSynchronously(updateMethod));
        }


        /// <summary>
        /// Restores fog of war to full opacity
        /// </summary>
        /// <param name="worldPosition">World position.</param>
        /// <param name="radius">Radius.</param>
        /// <param name="alpha">Alpha value (1f = fully opaque)</param>
        public void ResetFogOfWarAlpha (Vector3 worldPosition, float radius, float alpha = 1f) {
            EnsureFogOfWarInit();
            if (_fogOfWarTexture == null || fogOfWarColorBuffer == null || fogOfWarColorBuffer.Length == 0)
                return;

            fowEngine.ResetCircleSync(worldPosition, radius, anchoredFogOfWarCenter, fogOfWarSize, alpha);
        }


        /// <summary>
        /// Restores fog of war to full opacity
        /// </summary>
        public void ResetFogOfWarAlpha (Bounds bounds, float alpha = 1f) {
            ResetFogOfWarAlpha(bounds.center, bounds.extents.x, bounds.extents.z, alpha);
        }

        /// <summary>
        /// Restores fog of war to full opacity
        /// </summary>
        public void ResetFogOfWarAlpha (Collider collider, float alpha = 1f) {
            EnsureFogOfWarInit();
            if (collider == null || _fogOfWarTexture == null || fogOfWarColorBuffer == null || fogOfWarColorBuffer.Length == 0)
                return;

            fowEngine.ResetColliderSync(collider, collider.bounds, anchoredFogOfWarCenter, fogOfWarSize, alpha);
        }

        /// <summary>
        /// Restores fog of war to full opacity
        /// </summary>
        public void ResetFogOfWarAlpha (GameObject go, float alpha = 1f) {
            if (_fogOfWarTexture == null || fogOfWarColorBuffer == null || fogOfWarColorBuffer.Length == 0)
                return;
            if (go == null)
                return;

            SetFogOfWarAlpha(go, null, null, alpha, 0, 0, 0, alpha, FoWUpdateMethod.MainThread);
        }

        /// <summary>
        /// Restores fog of war to full opacity
        /// </summary>
        public void ResetFogOfWarAlpha (Vector3 position, Vector3 size, float alpha = 1f) {
            ResetFogOfWarAlpha(position, size.x * 0.5f, size.z * 0.5f, alpha);
        }

        /// <summary>
        /// Restores fog of war to full opacity
        /// </summary>
        /// <param name="position">Position in world space.</param>
        /// <param name="extentsX">Half of the length of the rectangle in X-Axis.</param>
        /// <param name="extentsZ">Half of the length of the rectangle in Z-Axis.</param>
        /// <param name="alpha">Alpha value (1f = fully opaque)</param>
        public void ResetFogOfWarAlpha (Vector3 position, float extentsX, float extentsZ, float alpha = 1f) {
            if (_fogOfWarTexture == null || fogOfWarColorBuffer == null || fogOfWarColorBuffer.Length == 0)
                return;

            fowEngine.ResetRectSync(position, extentsX, extentsZ, anchoredFogOfWarCenter, fogOfWarSize, alpha);
        }


        public void ResetFogOfWar (float alpha = 1f) {
            if (_fogOfWarTexture == null)
                return;
            int h = _fogOfWarTexture.height;
            int w = _fogOfWarTexture.width;
            int newLength = h * w;
            if (fogOfWarColorBuffer == null || fogOfWarColorBuffer.Length != newLength) {
                fogOfWarColorBuffer = new Color32[newLength];
            }
            Color32 opaque = new Color32(255, 255, 255, (byte)(alpha * 255));
            for (int k = 0; k < newLength; k++) {
                fogOfWarColorBuffer[k] = opaque;
            }
            fowEngine.Setup(fogOfWarColorBuffer, w, h);
            UploadFogOfWarTextureEditsToGPU();
        }

        /// <summary>
        /// Gets or set fog of war state as a Color32 buffer. The alpha channel stores the transparency of the fog at that position (0 = no fog, 1 = opaque).
        /// </summary>
        public Color32[] fogOfWarTextureData {
            get {
                fowEngine.FlushQueue();
                return fogOfWarColorBuffer;
            }
            set {
                EnsureFogOfWarInit();
                enableFogOfWar = true;
                fogOfWarColorBuffer = value;
                if (value == null || _fogOfWarTexture == null)
                    return;
                if (value.Length != _fogOfWarTexture.width * _fogOfWarTexture.height)
                    return;
                fowEngine.SetBuffer(value);
                UploadFogOfWarTextureEditsToGPU();
            }
        }

        /// <summary>
        /// Gets the current alpha value of the Fog of War at a given world position
        /// </summary>
        /// <returns>The fog of war alpha.</returns>
        /// <param name="worldPosition">World position.</param>
        public float GetFogOfWarAlpha (Vector3 worldPosition) {
            if (fogOfWarColorBuffer == null || fogOfWarColorBuffer.Length == 0 || _fogOfWarTexture == null)
                return 1f;

            float tx = (worldPosition.x - fogOfWarCenter.x) / fogOfWarSize.x + 0.5f;
            if (tx < 0 || tx > 1f)
                return 1f;
            float tz = (worldPosition.z - fogOfWarCenter.z) / fogOfWarSize.z + 0.5f;
            if (tz < 0 || tz > 1f)
                return 1f;

            int tw = _fogOfWarTexture.width;
            int th = _fogOfWarTexture.height;
            int px = (int)(tx * tw);
            int pz = (int)(tz * th);
            int colorBufferPos = pz * tw + px;
            if (colorBufferPos < 0 || colorBufferPos >= fogOfWarColorBuffer.Length)
                return 1f;
            return fogOfWarColorBuffer[colorBufferPos].a / 255f;
        }

    }

}
