using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RipVertices.QuickDestruction
{
    /// <summary>
    /// Minimal play-mode driver for the Quick Destruction demo scenes. It optionally provides a
    /// fly camera, center-screen aiming, click damage, physics projectiles, explosions and reset.
    /// This component is sample code: copy it into your project and replace the input handling
    /// with your own camera/weapon/gameplay logic.
    /// </summary>
    [AddComponentMenu("Quick Destruction/Demo/Destructible Demo Controller")]
    public class DestructibleDemoController : MonoBehaviour
    {
        [Header("Input Camera")]
        [Tooltip("Camera used for raycasts. Falls back to Camera.main when empty.")]
        public Camera RayCamera;

        [Header("Fly Camera")]
        [Tooltip("Enables beginner-friendly WASD + mouse fly controls for the demo camera.")]
        public bool EnableFlyCamera = true;
        [Tooltip("Locks and hides the cursor on Play so mouse movement rotates the camera. Escape releases it.")]
        public bool LockCursorOnPlay = true;
        [Tooltip("Aims click damage, projectiles and explosions through the crosshair instead of the free cursor.")]
        public bool AimAtScreenCenter = true;
        [Tooltip("Normal WASD/vertical flight speed in meters per second.")]
        [Min(0.1f)] public float CameraMoveSpeed = 6f;
        [Tooltip("Speed multiplier while Left Shift is held.")]
        [Min(1f)] public float CameraSprintMultiplier = 2.5f;
        [Tooltip("Mouse-look sensitivity in degrees per pointer-delta unit.")]
        [Min(0.001f)] public float CameraLookSensitivity = 0.12f;
        [Tooltip("Maximum camera pitch above and below the horizon.")]
        [Range(10f, 89f)] public float CameraPitchLimit = 85f;
        [Tooltip("Near clipping plane used by the demo camera for close fragment inspection.")]
        [Range(0.01f, 0.3f)] public float CameraNearClipPlane = 0.03f;

        [Header("Click Damage")]
        [Tooltip("Damage of a left-click hit. Respects the target's Strength / Health and Minimum Hit Damage, independently of the physical push.")]
        [Min(0.1f)] public float ClickDamage = 40f;
        [Tooltip("Showcase shortcut: raise click damage to the target's remaining Health and Minimum Hit Damage. Leave off to test actual durability and resistance to weak hits.")]
        public bool BreakOnEveryClick = false;
        [Tooltip("Physical impulse in N s applied by the raycast hit, independently of Health damage. " +
                 "0 disables push; damage spread can still apply. Heavier bodies gain less speed. " +
                 "Launch-speed caps still apply. Impact Spread Radius is a fixed distance, " +
                 "independent of Click Push Energy.")]
        [Min(0f)] public float ClickPushEnergy = 4f;
        [Tooltip("Local radius selecting fragments of the directly hit object. Nearby objects are affected separately when Spread Impact To Nearby Objects is enabled on the target profile; that wave stays inside the target's building hierarchy and exact Impact Spread Radius.")]
        [Min(0f)] public float ClickRadius = 0.75f;
        [Tooltip("Maximum left-click raycast distance.")]
        [Min(0.1f)] public float RayDistance = 200f;

        [Header("Explosion (E Key)")]
        [Tooltip("World-space radius of the E-key damage and Rigidbody shockwave. Distance is " +
                 "measured to colliders rather than object pivots. Quick Destruction objects " +
                 "receive Explosion Damage with the selected falloff; ordinary Dynamic " +
                 "Rigidbodies can receive the same radial impulse.")]
        [Min(0.1f)] public float ExplosionRange = 8f;
        [Tooltip("Health damage at the centre of the E-key blast. It is independent from the " +
                 "physical impulse, so a blast may damage without pushing or push without " +
                 "destroying.")]
        [Min(0f)] public float ExplosionDamage = 120f;
        [Tooltip("Distance curve applied to Explosion Damage. Physical force uses Unity's " +
                 "normal radial attenuation and is not multiplied by this curve a second time.")]
        public ExplosionFalloff ExplosionDamageFalloff = ExplosionFalloff.Linear;
        [Tooltip("Base radial impulse used by the E-key explosion, independently of Health " +
                 "damage. Breaking Quick Destruction targets still apply their profile's Break " +
                 "Push, Force Mode and launch-speed limits to fragments; surviving Dynamic " +
                 "targets receive one radius-aware shockwave.")]
        [Min(0f)] public float ExplosionForce = 25f;
        [InspectorName("No-Hit Aim Distance")]
        [Tooltip("Only used when the aim ray hits no collider. The blast point is placed this " +
                 "many metres in front of the camera; when a surface is hit, its exact hit " +
                 "point is used and this value is ignored. It never changes Explosion Damage, " +
                 "Range or Force.")]
        [Min(0.1f)] public float ExplosionFallbackDistance = 8f;
        [Tooltip("Optional ParticleSystem played once at the blast point. This is the explosion's " +
                 "own effect — the fireball. It is separate from the dust and debris particles " +
                 "each destructible raises through its own Break Effects component, which play " +
                 "whether this is assigned or not.")]
        public ParticleSystem ExplosionParticle;
        [Tooltip("Uniform scale applied to the spawned blast particle.")]
        [Min(0.01f)] public float ExplosionParticleScale = 1f;

        [Header("Projectile")]
        [Tooltip("Speed of the sphere fired with RMB or F. Impact damage grows with the SQUARE of this value (energy = 0.5 * mass * speed^2), so it is the strongest knob for how hard a shot hits.")]
        [Min(0.1f)] public float ProjectileSpeed = 70f;
        [Tooltip("Rigidbody mass of the fired sphere. Impact damage scales linearly with mass. The demo default is deliberately heavy so a single shot breaks the sample props; lower it for a multi-hit feel.")]
        [Min(0.01f)] public float ProjectileMass = 40f;
        [Tooltip("Diameter of the fired sphere in meters.")]
        [Min(0.01f)] public float ProjectileSize = 0.15f;
        [Tooltip("Additional directional impulse in N s applied at the projectile impact. Heavier targets gain less speed. Independent of Health damage and capped by the target's launch speed limit.")]
        [Min(0f)] public float ProjectileDirectionalForce = 7f;
        [Tooltip("Local radius around the projectile contact whose fragments may react. It does not turn the shot into an explosion or damage neighbouring objects.")]
        [Min(0.01f)] public float ProjectileImpactRadius = 0.75f;
        [Tooltip("Minimum time between two shots while RMB or F is held. Kept just above the profile's Damage Cooldown (0.15s on the sample profiles) so every rapid shot clears that cooldown and deals damage, instead of back-to-back shots landing inside the same window and being ignored.")]
        [Min(0.02f)] public float FireCooldown = 0.17f;
        [Tooltip("Off keeps demo shots on the cursor ray. Enable only when a visibly arcing projectile is desired.")]
        public bool ProjectileUseGravity = false;
        [Tooltip("Maximum lifetime of a missed projectile. Short values prevent physics accumulation during rapid fire.")]
        [Min(0.25f)] public float ProjectileLifetime = 3f;
        [Tooltip("Delay before removing a projectile after its first collision.")]
        [Min(0f)] public float ProjectileDestroyAfterImpact = 0.08f;

        Camera m_camera;
        float m_nextFireTime;
        float m_cameraYaw;
        float m_cameraPitch;
        bool m_cameraAnglesReady;
        bool m_ownsCursorLock;
        GUIStyle m_crosshairStyle;
        // A heavily fractured model can place many loose colliders in front of its
        // remaining shell. Keep this above the supplied high-piece presets so the
        // non-allocating raycast still receives the attached target behind the rubble.
        readonly RaycastHit[] m_damageHits = new RaycastHit[512];
        void Start()
        {
            ResolveCamera();
            InitializeCameraAngles();
            if (m_camera != null)
                m_camera.nearClipPlane = CameraNearClipPlane;
            if (EnableFlyCamera && LockCursorOnPlay)
                LockCursor();
        }

        void OnDisable()
        {
            if (!m_ownsCursorLock) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            m_ownsCursorLock = false;
        }

        void Update()
        {
            ResolveCamera();
            if (m_camera == null)
                return;

            if (!UpdateFlyCamera())
                return;

            if (DamagePressedThisFrame())
                HandleClick();

            if (ProjectileHeld() &&
                Time.time >= m_nextFireTime)
                FireProjectile();

            if (ExplosionPressedThisFrame())
                HandleExplosion();

            if (ResetPressedThisFrame())
                ResetAll();
        }

        void FireProjectile()
        {
            m_nextFireTime = Time.time + FireCooldown;

            Ray aimRay = GetAimRay();
            Vector3 aimPoint = Physics.Raycast(aimRay, out RaycastHit aimHit,
                    RayDistance, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore)
                ? aimHit.point
                : aimRay.GetPoint(RayDistance);

            float radius = ProjectileSize * 0.5f;
            float spawnDistance = Mathf.Max(
                m_camera.nearClipPlane + radius + 0.02f, 0.1f);
            Vector3 spawnPoint = aimRay.GetPoint(spawnDistance);
            Vector3 shotDirection = aimPoint - spawnPoint;
            if (shotDirection.sqrMagnitude < 1e-6f)
                shotDirection = aimRay.direction;
            shotDirection.Normalize();

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "DemoProjectile";
            go.transform.localScale = Vector3.one * ProjectileSize;
            go.transform.position = spawnPoint;
            Renderer projectileRenderer = go.GetComponent<Renderer>();
            if (projectileRenderer != null)
            {
                projectileRenderer.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;
                projectileRenderer.receiveShadows = false;
            }

            var body = go.AddComponent<Rigidbody>();
            body.mass = ProjectileMass;
            body.useGravity = ProjectileUseGravity;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
#if UNITY_6000_0_OR_NEWER
            body.linearVelocity = shotDirection * ProjectileSpeed;
#else
            body.velocity = shotDirection * ProjectileSpeed;
#endif

            var impactSource = go.AddComponent<DestructionImpactSource>();
            impactSource.DirectionalForce = ProjectileDirectionalForce;
            impactSource.OverrideImpactRadius = true;
            impactSource.ImpactRadius = ProjectileImpactRadius;

            var lifetime = go.AddComponent<DemoProjectileLifetime>();
            lifetime.DestroyAfterImpact = ProjectileDestroyAfterImpact;
            lifetime.TravelVelocity = shotDirection * ProjectileSpeed;
            // Auto-cleanup so long sessions do not accumulate ContinuousDynamic
            // spheres and collision pairs around the destructible props.
            UnityEngine.Object.Destroy(go, ProjectileLifetime);
        }

        void HandleClick()
        {
            Ray ray = GetAimRay();
            if (!TryGetDestructibleHit(ray, out RaycastHit hit,
                    out DestructibleRoot root))
                return;

            // Durability tests use the authored damage. The optional showcase shortcut
            // must stay separate from physical impulse, even on high-Health targets.
            float amount = ClickDamage;
            if (BreakOnEveryClick)
                amount = Mathf.Max(amount, root.Health,
                    root.Profile != null ? root.Profile.MinimumHitDamage : 0f);

            // GetHashCode on a UnityEngine.Object is the object's own id, so it is a
            // stable per-session SourceId without depending on the GetInstanceID /
            // GetEntityId API that shifted between Unity versions.
            root.ApplyDamage(new DamageContext(amount, hit.point, hit.normal,
                ClickRadius, 0, gameObject.GetHashCode(),
                Mathf.Max(0f, ClickPushEnergy),
                ray.direction, -1f,
                root.ResolveStructuralGroup(hit.collider)));
        }

        bool TryGetDestructibleHit(Ray ray, out RaycastHit bestHit,
            out DestructibleRoot bestRoot)
        {
            int count = Physics.RaycastNonAlloc(ray, m_damageHits, RayDistance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            bestHit = default;
            bestRoot = null;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit candidate = m_damageHits[i];
                if (candidate.collider == null || candidate.distance >= bestDistance)
                    continue;
                DestructibleRoot root =
                    candidate.collider.GetComponentInParent<DestructibleRoot>();
                if (root == null) continue;

                // What the crosshair covers is what gets hit, debris included. Skipping
                // loose pieces used to silently swallow the shot: no push, no impact
                // sound and no runtime re-fracture on the very piece being aimed at.
                bestDistance = candidate.distance;
                bestHit = candidate;
                bestRoot = root;
            }
            return bestRoot != null;
        }

        void HandleExplosion()
        {
            Ray ray = GetAimRay();
            bool rayHit = Physics.Raycast(ray, out RaycastHit hit, RayDistance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Vector3 point = ResolveExplosionPoint(ray, rayHit, hit.point,
                ExplosionFallbackDistance);
            DestructionExplosion.SpawnBlastParticle(ExplosionParticle, point,
                ExplosionParticleScale);
            DestructionExplosion.Detonate(point, ExplosionRange, ExplosionDamage,
                ExplosionForce, ExplosionDamageFalloff,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore,
                sourceId: gameObject.GetHashCode(),
                affectOtherRigidbodies: true);
        }

        static Vector3 ResolveExplosionPoint(Ray ray, bool rayHit, Vector3 hitPoint,
            float noHitDistance)
        {
            return rayHit
                ? hitPoint
                : ray.origin + ray.direction * noHitDistance;
        }

        void ResetAll()
        {
            DestructibleRoot[] roots = FindAllRoots();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null) continue;
                roots[i].ResetDestructible(ResetMode.Instant);
            }
        }

        static DestructibleRoot[] FindAllRoots()
        {
#if UNITY_6000_4_OR_NEWER
            return Object.FindObjectsByType<DestructibleRoot>();
#elif UNITY_2023_1_OR_NEWER
            return Object.FindObjectsByType<DestructibleRoot>(FindObjectsSortMode.None);
#else
            return Object.FindObjectsOfType<DestructibleRoot>();
#endif
        }

        void ResolveCamera()
        {
            if (m_camera == null)
                m_camera = RayCamera != null ? RayCamera : Camera.main;
        }

        void InitializeCameraAngles()
        {
            if (m_camera == null || m_cameraAnglesReady) return;
            Vector3 euler = m_camera.transform.eulerAngles;
            m_cameraYaw = euler.y;
            m_cameraPitch = euler.x > 180f ? euler.x - 360f : euler.x;
            m_cameraAnglesReady = true;
        }

        bool UpdateFlyCamera()
        {
            if (!EnableFlyCamera) return true;
            InitializeCameraAngles();

            if (ReleaseCursorPressedThisFrame())
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                m_ownsCursorLock = false;
                return false;
            }

            if (Cursor.lockState != CursorLockMode.Locked)
            {
                if (PointerLockPressedThisFrame())
                    LockCursor();
                // The click used to recapture the Game view is never also a shot.
                return false;
            }

            Vector2 look = LookDelta();
            m_cameraYaw += look.x * CameraLookSensitivity;
            m_cameraPitch = Mathf.Clamp(
                m_cameraPitch - look.y * CameraLookSensitivity,
                -CameraPitchLimit, CameraPitchLimit);
            m_camera.transform.rotation = Quaternion.Euler(
                m_cameraPitch, m_cameraYaw, 0f);

            Vector3 input = FlyMoveInput();
            Vector3 motion = m_camera.transform.right * input.x +
                             m_camera.transform.forward * input.z +
                             Vector3.up * input.y;
            if (motion.sqrMagnitude > 1f) motion.Normalize();
            float speed = CameraMoveSpeed *
                          (SprintHeld() ? CameraSprintMultiplier : 1f);
            m_camera.transform.position += motion * speed * Time.unscaledDeltaTime;
            return true;
        }

        void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            m_ownsCursorLock = true;
        }

        Ray GetAimRay()
        {
            return AimAtScreenCenter
                ? m_camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                : m_camera.ScreenPointToRay(PointerPosition());
        }

        // The sample works with Player Settings > Active Input Handling set to
        // Input System Package, Input Manager (Old), or Both. Prefer the new system
        // whenever it is enabled so an Input-System-only project never touches the
        // legacy UnityEngine.Input API and cannot throw InvalidOperationException.
        static bool DamagePressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown(0);
#else
            return false;
#endif
        }

        static bool ProjectileHeld()
        {
#if ENABLE_INPUT_SYSTEM
            bool mouse = Mouse.current != null && Mouse.current.rightButton.isPressed;
            bool keyboard = Keyboard.current != null && Keyboard.current.fKey.isPressed;
            return mouse || keyboard;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(1) || Input.GetKey(KeyCode.F);
#else
            return false;
#endif
        }

        static bool ExplosionPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.E);
#else
            return false;
#endif
        }

        static bool ResetPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.R);
#else
            return false;
#endif
        }

        static bool ReleaseCursorPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null &&
                   Keyboard.current.escapeKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Escape);
#else
            return false;
#endif
        }

        static bool PointerLockPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null &&
                   (Mouse.current.leftButton.wasPressedThisFrame ||
                    Mouse.current.rightButton.wasPressedThisFrame);
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
#else
            return false;
#endif
        }

        static Vector2 LookDelta()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null
                ? Mouse.current.delta.ReadValue()
                : Vector2.zero;
#elif ENABLE_LEGACY_INPUT_MANAGER
            // Legacy mouse axes are pre-scaled, so normalize them to roughly the
            // same range as Input System pointer delta.
            return new Vector2(Input.GetAxisRaw("Mouse X"),
                Input.GetAxisRaw("Mouse Y")) * 10f;
#else
            return Vector2.zero;
#endif
        }

        static Vector3 FlyMoveInput()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current == null) return Vector3.zero;
            float x = (Keyboard.current.dKey.isPressed ? 1f : 0f) -
                      (Keyboard.current.aKey.isPressed ? 1f : 0f);
            float y = (Keyboard.current.spaceKey.isPressed ? 1f : 0f) -
                      (Keyboard.current.cKey.isPressed ? 1f : 0f);
            float z = (Keyboard.current.wKey.isPressed ? 1f : 0f) -
                      (Keyboard.current.sKey.isPressed ? 1f : 0f);
            return new Vector3(x, y, z);
#elif ENABLE_LEGACY_INPUT_MANAGER
            float x = (Input.GetKey(KeyCode.D) ? 1f : 0f) -
                      (Input.GetKey(KeyCode.A) ? 1f : 0f);
            float y = (Input.GetKey(KeyCode.Space) ? 1f : 0f) -
                      (Input.GetKey(KeyCode.C) ? 1f : 0f);
            float z = (Input.GetKey(KeyCode.W) ? 1f : 0f) -
                      (Input.GetKey(KeyCode.S) ? 1f : 0f);
            return new Vector3(x, y, z);
#else
            return Vector3.zero;
#endif
        }

        static bool SprintHeld()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null &&
                   Keyboard.current.leftShiftKey.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(KeyCode.LeftShift);
#else
            return false;
#endif
        }

        static Vector2 PointerPosition()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null
                ? Mouse.current.position.ReadValue()
                : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.mousePosition;
#else
            return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
#endif
        }

        void OnGUI()
        {
            const float pad = 8f;
            var rect = new Rect(10f, 10f, 390f, 164f);
            GUI.Box(rect, "Quick Destruction Demo");
            GUILayout.BeginArea(new Rect(rect.x + pad, rect.y + 22f,
                rect.width - pad * 2f, rect.height - 26f));
            GUILayout.Label("WASD + Mouse - fly  |  Shift - faster  |  Space / C - up / down");
            GUILayout.Label("LMB - damage at crosshair");
            GUILayout.Label("RMB / F - fire a Rigidbody projectile at crosshair");
            GUILayout.Label("E - AOE damage + radial impulse at crosshair");
            GUILayout.Label("R - reset all destructibles");
            GUILayout.Label("Esc - release cursor  |  click Game view - capture again");
            GUILayout.EndArea();

            if (AimAtScreenCenter)
            {
                var center = new Rect(Screen.width * 0.5f - 10f,
                    Screen.height * 0.5f - 13f, 20f, 26f);
                if (m_crosshairStyle == null)
                {
                    m_crosshairStyle = new GUIStyle(GUI.skin.label)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontSize = 20,
                        fontStyle = FontStyle.Bold
                    };
                }
                Color previous = GUI.color;
                GUI.color = Color.black;
                GUI.Label(new Rect(center.x + 1f, center.y + 1f,
                    center.width, center.height), "+", m_crosshairStyle);
                GUI.color = Color.white;
                GUI.Label(center, "+", m_crosshairStyle);
                GUI.color = previous;
            }
        }
    }

    [AddComponentMenu("")]
    internal sealed class DemoProjectileLifetime : MonoBehaviour
    {
        public float DestroyAfterImpact = 0.08f;
        public Vector3 TravelVelocity;
        bool m_scheduled;

        void OnCollisionEnter(Collision collision)
        {
            if (m_scheduled) return;

            // A projectile hits whatever it reaches first, loose rubble included: passing
            // through debris made shots at fallen pieces do nothing at all.
            m_scheduled = true;
            // Do not disable the collider from inside the callback. Unity still has
            // to dispatch this contact to the destructible's relay/fragment component.
            Destroy(gameObject, Mathf.Max(0f, DestroyAfterImpact));
        }
    }
}
