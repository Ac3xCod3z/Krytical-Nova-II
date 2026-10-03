using KryticalNova.Core;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.UI
{
    /// <summary>
    /// Client-only ground rings built from the Synty SciFi HUD ring sprites: one at the chosen move destination
    /// (hidden once the player arrives) and one under the selected target (hidden when it dies or is deselected).
    /// Each ring is a pair of flat quads; nothing here has a collider, so markers never intercept clicks.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldMarkerPresenter : MonoBehaviour, IWorldMarkerSink
    {
        private const float GroundLift = 0.04f;
        private const float ArriveDistance = 0.6f;
        private const float MoveMarkerLifetime = 12f;
        private const float AppearSeconds = 0.22f;
        private const float FadeSeconds = 0.2f;
        private const float AppearScale = 1.9f;
        private const float MinTargetDiameter = 1.2f;
        private const float MaxTargetDiameter = 6f;
        private const float TargetPadding = 1.45f;

        [Header("Assets")]
        [SerializeField] private Shader markerShader;
        [Tooltip("Outer, rotating ring of the move destination marker.")]
        [SerializeField] private Texture2D moveOuterRing;
        [Tooltip("Inner, pulsing ring of the move destination marker.")]
        [SerializeField] private Texture2D moveInnerRing;
        [Tooltip("Main ring drawn under the selected target.")]
        [SerializeField] private Texture2D targetRing;
        [Tooltip("Slowly counter-rotating outer ring around the selected target.")]
        [SerializeField] private Texture2D targetOuterRing;

        [Header("Look")]
        [SerializeField, Min(0.1f)] private float moveMarkerDiameter = 1.6f;
        [SerializeField, ColorUsage(true, true)] private Color moveColor = new Color(0.3f, 1.4f, 1.15f, 1f);
        [SerializeField, ColorUsage(true, true)] private Color hostileColor = new Color(1.7f, 0.18f, 0.14f, 1f);
        [SerializeField, ColorUsage(true, true)] private Color friendlyColor = new Color(1.6f, 1.1f, 0.2f, 1f);
        [SerializeField] private float moveSpinDegreesPerSecond = 90f;
        [SerializeField] private float targetSpinDegreesPerSecond = 25f;

        private sealed class Ring
        {
            public Transform Root;
            public Transform Outer;
            public Transform Inner;
            public Material OuterMaterial;
            public Material InnerMaterial;
            public float Age;
            public float FadeOut = -1f;
            public bool Active;
        }

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");

        private Ring moveRing;
        private Ring targetRing3D;
        private Transform mover;
        private Transform target;
        private IDamageable targetHealth;
        private float targetFootOffset;
        private float targetDiameter;
        private Mesh quad;

        private void Awake()
        {
            if (markerShader == null || moveOuterRing == null || targetRing == null)
            {
                Debug.LogWarning("WorldMarkerPresenter is missing its shader or ring textures; ground markers are disabled. "
                    + "Run Tools > Krytical Nova > UI > Set Up HUD In Open Scene.", this);
                enabled = false;
                return;
            }

            quad = BuildFlatQuad();
            moveRing = CreateRing("Move Marker", moveOuterRing, moveInnerRing != null ? moveInnerRing : moveOuterRing, moveColor);
            targetRing3D = CreateRing("Target Marker", targetOuterRing != null ? targetOuterRing : targetRing, targetRing, hostileColor);
        }

        private void OnEnable() => ClientPresentationRegistry.RegisterMarkers(this);

        private void OnDisable()
        {
            ClientPresentationRegistry.UnregisterMarkers(this);
            if (moveRing != null) SetRingVisible(moveRing, false);
            if (targetRing3D != null) SetRingVisible(targetRing3D, false);
        }

        private void OnDestroy()
        {
            DestroyRing(moveRing);
            DestroyRing(targetRing3D);
            if (quad != null) Destroy(quad);
        }

        // ------------------------------------------------------------------ IWorldMarkerSink

        public void ShowMoveMarker(Transform moverTransform, Vector3 point, Vector3 normal)
        {
            // Destinations come from the NavMesh, which sits below the paving: put the ring on the visible surface.
            point = GroundSnap.Snap(point, out normal, moverTransform);
            if (isActiveAndEnabled) ClientPresentationRegistry.Feel?.MoveCommand(point, normal);
            if (!isActiveAndEnabled || !HudPreferences.ShowMoveMarker) return;
            mover = moverTransform;
            if (normal.sqrMagnitude < 0.01f || Vector3.Angle(normal, Vector3.up) > 50f) normal = Vector3.up;
            moveRing.Root.SetPositionAndRotation(point + normal * GroundLift, Quaternion.FromToRotation(Vector3.up, normal));
            moveRing.Root.localScale = Vector3.one * moveMarkerDiameter;
            moveRing.Age = 0f;
            moveRing.FadeOut = -1f;
            SetRingVisible(moveRing, true);
        }

        public void HideMoveMarker()
        {
            if (moveRing != null && moveRing.Active && moveRing.FadeOut < 0f) moveRing.FadeOut = 0f;
        }

        public void SetTarget(Transform newTarget, WorldTargetKind kind)
        {
            if (!isActiveAndEnabled || newTarget == null) return;
            if (newTarget != target) ClientPresentationRegistry.Feel?.TargetSelected(newTarget, kind);
            target = newTarget;
            targetHealth = newTarget.GetComponentInParent<IDamageable>();
            MeasureTarget(newTarget, out targetFootOffset, out targetDiameter);
            Color tint = kind == WorldTargetKind.Hostile ? hostileColor : friendlyColor;
            targetRing3D.InnerMaterial.SetColor(ColorId, tint);
            targetRing3D.OuterMaterial.SetColor(ColorId, tint * 0.8f);
            targetRing3D.Age = 0f;
            targetRing3D.FadeOut = -1f;
            SetRingVisible(targetRing3D, HudPreferences.ShowTargetRing);
            UpdateTargetPose();
        }

        public void ClearTarget()
        {
            target = null;
            targetHealth = null;
            if (targetRing3D != null && targetRing3D.Active && targetRing3D.FadeOut < 0f) targetRing3D.FadeOut = 0f;
        }

        // ------------------------------------------------------------------ animation

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            UpdateMoveRing(dt);
            UpdateTargetRing(dt);
        }

        private void UpdateMoveRing(float dt)
        {
            if (!moveRing.Active) return;
            moveRing.Age += dt;

            if (moveRing.FadeOut < 0f)
            {
                bool moverGone = mover == null || !mover.gameObject.activeInHierarchy;
                Vector3 offset = moverGone ? Vector3.zero : mover.position - moveRing.Root.position;
                offset.y = 0f;
                if (moverGone || offset.sqrMagnitude <= ArriveDistance * ArriveDistance || moveRing.Age > MoveMarkerLifetime
                    || !HudPreferences.ShowMoveMarker)
                    moveRing.FadeOut = 0f;
            }

            float appear = Mathf.Clamp01(moveRing.Age / AppearSeconds);
            float eased = 1f - (1f - appear) * (1f - appear);
            float outerScale = Mathf.Lerp(AppearScale, 1f, eased);
            float pulse = 0.62f + 0.06f * Mathf.Sin(moveRing.Age * 7f);
            moveRing.Outer.localScale = Vector3.one * outerScale;
            moveRing.Outer.localRotation = Quaternion.Euler(0f, moveRing.Age * moveSpinDegreesPerSecond, 0f);
            moveRing.Inner.localScale = Vector3.one * pulse;

            float alpha = eased * FadeAlpha(moveRing, dt);
            moveRing.OuterMaterial.SetFloat(OpacityId, alpha);
            moveRing.InnerMaterial.SetFloat(OpacityId, alpha * 0.85f);
        }

        private void UpdateTargetRing(float dt)
        {
            if (!targetRing3D.Active) return;
            targetRing3D.Age += dt;

            if (targetRing3D.FadeOut < 0f)
            {
                bool lost = target == null || !target.gameObject.activeInHierarchy
                    || (targetHealth != null && !targetHealth.IsAlive) || !HudPreferences.ShowTargetRing;
                if (lost) ClearTarget();
                else UpdateTargetPose();
            }

            float appear = Mathf.Clamp01(targetRing3D.Age / AppearSeconds);
            float eased = 1f - (1f - appear) * (1f - appear);
            float breathe = 1f + 0.035f * Mathf.Sin(targetRing3D.Age * 3.2f);
            targetRing3D.Inner.localScale = Vector3.one * Mathf.Lerp(1.35f, 1f, eased) * breathe;
            targetRing3D.Inner.localRotation = Quaternion.identity;
            targetRing3D.Outer.localScale = Vector3.one * 1.22f;
            targetRing3D.Outer.localRotation = Quaternion.Euler(0f, -targetRing3D.Age * targetSpinDegreesPerSecond, 0f);

            float alpha = eased * FadeAlpha(targetRing3D, dt);
            targetRing3D.InnerMaterial.SetFloat(OpacityId, alpha);
            targetRing3D.OuterMaterial.SetFloat(OpacityId, alpha * 0.55f);
        }

        private void UpdateTargetPose()
        {
            if (target == null) return;
            // Pivots and collider bottoms can sit below the tiles; ride the visible ground under the target instead.
            Vector3 feet = GroundSnap.Snap(target.position + Vector3.up * targetFootOffset, target);
            feet.y += GroundLift;
            targetRing3D.Root.SetPositionAndRotation(feet, Quaternion.identity);
            targetRing3D.Root.localScale = Vector3.one * targetDiameter;
        }

        /// <summary>Advances a fade-out if one is running; deactivates the ring when it finishes.</summary>
        private static float FadeAlpha(Ring ring, float dt)
        {
            if (ring.FadeOut < 0f) return 1f;
            ring.FadeOut += dt;
            float alpha = 1f - Mathf.Clamp01(ring.FadeOut / FadeSeconds);
            if (alpha <= 0f) SetRingVisible(ring, false);
            return alpha;
        }

        private static void MeasureTarget(Transform t, out float footOffset, out float diameter)
        {
            footOffset = 0f;
            diameter = MinTargetDiameter * 1.5f;
            Bounds bounds = default;
            bool hasBounds = false;
            foreach (var col in t.GetComponentsInChildren<Collider>())
            {
                if (!col.enabled || col.isTrigger) continue;
                if (!hasBounds) { bounds = col.bounds; hasBounds = true; }
                else bounds.Encapsulate(col.bounds);
            }
            if (!hasBounds)
            {
                foreach (var r in t.GetComponentsInChildren<Renderer>())
                {
                    if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
                    else bounds.Encapsulate(r.bounds);
                }
            }
            if (!hasBounds) return;
            footOffset = bounds.min.y - t.position.y;
            float radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
            diameter = Mathf.Clamp(radius * 2f * TargetPadding, MinTargetDiameter, MaxTargetDiameter);
        }

        // ------------------------------------------------------------------ construction

        private Ring CreateRing(string ringName, Texture2D outerTexture, Texture2D innerTexture, Color tint)
        {
            var root = new GameObject(ringName).transform;
            root.SetParent(transform, false);
            var ring = new Ring
            {
                Root = root,
                OuterMaterial = CreateMaterial(outerTexture, tint),
                InnerMaterial = CreateMaterial(innerTexture, tint)
            };
            ring.Outer = CreateQuad("Outer", root, ring.OuterMaterial);
            ring.Inner = CreateQuad("Inner", root, ring.InnerMaterial);
            SetRingVisible(ring, false);
            return ring;
        }

        private Material CreateMaterial(Texture2D texture, Color tint)
        {
            var material = new Material(markerShader) { name = $"{texture.name} (Marker)", mainTexture = texture };
            material.SetColor(ColorId, tint);
            return material;
        }

        private Transform CreateQuad(string quadName, Transform parent, Material material)
        {
            var go = new GameObject(quadName, typeof(MeshFilter), typeof(MeshRenderer));
            go.layer = gameObject.layer;
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = quad;
            var meshRenderer = go.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return go.transform;
        }

        private static void SetRingVisible(Ring ring, bool visible)
        {
            ring.Active = visible;
            ring.Root.gameObject.SetActive(visible);
            if (!visible) ring.FadeOut = -1f;
        }

        private static void DestroyRing(Ring ring)
        {
            if (ring == null) return;
            if (ring.OuterMaterial != null) Destroy(ring.OuterMaterial);
            if (ring.InnerMaterial != null) Destroy(ring.InnerMaterial);
        }

        /// <summary>A unit quad lying in the XZ plane, facing up.</summary>
        private static Mesh BuildFlatQuad()
        {
            var mesh = new Mesh { name = "Ground Marker Quad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f)
            };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
