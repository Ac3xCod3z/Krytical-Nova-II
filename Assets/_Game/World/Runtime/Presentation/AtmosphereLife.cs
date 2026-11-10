using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// Client-only air that makes a place feel lived in (owner picks K and L, 9 Nov 2026), driven by
    /// <see cref="EnvironmentPresenter"/>: one camera-following particle system (about 200 motes, 40 m box) whose look follows
    /// the zone and the time (pollen and dust by day, fireflies at night, drifting cloud wisps on Sky Concord), pushed by the
    /// weather's wind; and a few soft light shafts at chosen spots in the morning and after rain. Nothing here is networked
    /// and nothing runs on a headless server.
    /// </summary>
    /// <summary>Which part of Zone01 the local player stands in (set by <see cref="AtmosphereLife"/>, read by the audio zone beds).</summary>
    public enum PlaceArea { Town, Fields, Concord }

    /// <summary>The local player's place, for the client's sound beds (Audio sits above World, so it reads this, never the reverse).</summary>
    public static class PlaceNow
    {
        public static PlaceArea Area { get; internal set; } = PlaceArea.Town;
    }

    internal sealed class AtmosphereLife
    {
        public enum Zone { Town, Fields, Concord }

        private const int MaxMotes = 200;
        private const float BoxXZ = 40f, BoxY = 10f;
        /// <summary>Lamp within this many metres of the player = the town streets.</summary>
        private const float TownLampReach = 35f;
        private const float ConcordMinY = 150f;

        private ParticleSystem motes;
        private ParticleSystemRenderer moteRenderer;
        private GameObject shaftRoot;
        private Transform[] shafts;
        private MeshRenderer[] shaftRenderers;
        private MaterialPropertyBlock shaftBlock;
        private float shaftTimer;
        private float rainMemory;
        private int moteLook = -1;   // 0 day dust, 1 fireflies, 2 wisps
        private Gradient dustFade, bugBlink;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        /// <summary>Light-shaft spots: world XZ the shaft stands at (Zone01 town centre, the Concord deck).</summary>
        private static readonly Vector3[] ShaftSpots =
        {
            new Vector3(-7.5f, 0f, 7.2f), new Vector3(7.5f, 0f, 7.2f), new Vector3(-8.5f, 0f, 18.8f),
            new Vector3(-1.4f, 300f, -55f), new Vector3(8f, 300f, -50f),
        };

        public Zone CurrentZone { get; private set; }

        // ---------------------------------------------------------------- Sky Concord deck lamps: a little light on the ground
        // Lamp feet in the Concord prefab's own space (the same list as Concord040Setup.LampAxes, which shrank them to half size,
        // owner 10 Nov 2026) and the height of each shrunk head. Cyan for the gate row, amber for the plaza rings.
        private static readonly Vector4[] ConcordLamps =
        {
            new Vector4(6.405f, 0.62f, -61f, 0f), new Vector4(-6.405f, 0.62f, -61f, 0f), new Vector4(6.405f, 0.62f, -69f, 0f), new Vector4(-6.405f, 0.62f, -69f, 0f),
            new Vector4(-13.7f, 1.1f, -7.3f, 1f), new Vector4(-8.3f, 1.1f, -7.3f, 1f), new Vector4(-13.7f, 1.1f, 7.3f, 1f), new Vector4(-8.3f, 1.1f, 7.3f, 1f),
            new Vector4(8.3f, 1.1f, -7.3f, 1f), new Vector4(13.7f, 1.1f, -7.3f, 1f), new Vector4(8.3f, 1.1f, 7.3f, 1f), new Vector4(13.7f, 1.1f, 7.3f, 1f),
            new Vector4(-8f, 1.1f, -4.1f, 1f), new Vector4(-5.1f, 1.1f, -7f, 1f), new Vector4(-8f, 1.1f, 4.1f, 1f), new Vector4(-5.1f, 1.1f, 7f, 1f),
            new Vector4(8f, 1.1f, -4.1f, 1f), new Vector4(5.1f, 1.1f, -7f, 1f), new Vector4(8f, 1.1f, 4.1f, 1f), new Vector4(5.1f, 1.1f, 7f, 1f),
        };
        private Light[] concordLights;
        private float concordSearch;

        private void ConcordLampLight(float night, float dt)
        {
            if (concordLights == null)
            {
                concordSearch -= dt;
                if (concordSearch > 0f) return;
                concordSearch = 2f;
                var marker = GameObject.Find("Opaque_0_-4_0507");   // a chunk of the Concord prefab: its root is the prefab's space
                if (marker == null) return;
                Transform root = marker.transform.root;
                var holder = new GameObject("ConcordLampLights") { hideFlags = HideFlags.DontSave };
                concordLights = new Light[ConcordLamps.Length];
                for (int i = 0; i < ConcordLamps.Length; i++)
                {
                    Vector4 l = ConcordLamps[i];
                    var g = new GameObject("LampBounce " + i);
                    g.transform.SetParent(holder.transform, false);
                    g.transform.position = root.TransformPoint(new Vector3(l.x, l.y, l.z));
                    var li = g.AddComponent<Light>();
                    li.type = LightType.Point; li.range = 3.2f; li.shadows = LightShadows.None;
                    li.color = l.w > 0.5f ? new Color(1f, 0.62f, 0.25f) : new Color(0.3f, 0.85f, 1f);
                    concordLights[i] = li;
                }
                concordHolder = holder;
            }
            bool on = CurrentZone == Zone.Concord && night > 0.05f;
            for (int i = 0; i < concordLights.Length; i++)
            {
                if (concordLights[i] == null) continue;
                if (concordLights[i].enabled != on) concordLights[i].enabled = on;
                if (on) concordLights[i].intensity = 2.2f * night;
            }
        }
        private GameObject concordHolder;

        public void Update(Transform anchor, Vector3 sunDir, float night, Vector2 wind, float rain, float dt)
        {
            if (anchor == null) return;
            Vector3 me = anchor.position;
            CurrentZone = me.y > ConcordMinY ? Zone.Concord : NearLamp(me) ? Zone.Town : Zone.Fields;
            PlaceNow.Area = CurrentZone == Zone.Concord ? PlaceArea.Concord : CurrentZone == Zone.Fields ? PlaceArea.Fields : PlaceArea.Town;
            Motes(me, sunDir, night, wind, dt);
            Shafts(me, sunDir, night, rain, dt);
            ConcordLampLight(night, dt);
        }

        public void Dispose()
        {
            if (motes != null) Object.Destroy(motes.gameObject);
            if (shaftRoot != null) Object.Destroy(shaftRoot);
            if (concordHolder != null) Object.Destroy(concordHolder);
            concordLights = null; concordHolder = null;
            motes = null; shaftRoot = null; shafts = null;
        }

        private static bool NearLamp(Vector3 p)
        {
            var lamps = StreetLampController.All;
            for (int i = 0; i < lamps.Count; i++)
                if (lamps[i] != null && (lamps[i].LampPosition - p).sqrMagnitude < TownLampReach * TownLampReach) return true;
            return false;
        }

        // ---------------------------------------------------------------- K: motes

        private void Motes(Vector3 me, Vector3 sunDir, float night, Vector2 wind, float dt)
        {
            if (motes == null)
            {
                var mat = Resources.Load<Material>("KN/AirMote");
                if (mat == null) return;
                var go = new GameObject("AirMotes") { hideFlags = HideFlags.DontSave };
                motes = go.AddComponent<ParticleSystem>();
                moteRenderer = go.GetComponent<ParticleSystemRenderer>();
                moteRenderer.sharedMaterial = mat;
                moteRenderer.renderMode = ParticleSystemRenderMode.Billboard;
                moteRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                moteRenderer.receiveShadows = false;
                var main = motes.main;
                main.loop = true; main.playOnAwake = true; main.maxParticles = MaxMotes;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var shape = motes.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(BoxXZ * 2f, BoxY, BoxXZ * 2f);
                var col = motes.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
                motes.Play();
            }
            motes.transform.position = me + Vector3.up * 2f;

            float day = Mathf.Clamp01(sunDir.y * 3f);
            var m = motes.main;
            var em = motes.emission;
            var force = motes.forceOverLifetime;
            force.enabled = true;
            force.space = ParticleSystemSimulationSpace.World;
            Vector2 push = wind * 0.12f;
            force.x = push.x; force.z = push.y; force.y = 0f;

            bool bugs = CurrentZone != Zone.Concord && night > 0.5f;
            SetMoteLook(CurrentZone == Zone.Concord ? 2 : bugs ? 1 : 0);
            if (bugs) { force.x = 0f; force.z = 0f; }   // a firefly does not ride the wind
            switch (CurrentZone)
            {
                case Zone.Concord:
                    // Cloud wisps sliding past the deck edge: big, pale, fast, faint.
                    m.startSize = new ParticleSystem.MinMaxCurve(2.5f, 5f);
                    m.startLifetime = 9f; m.startSpeed = 0.2f;
                    m.startColor = Color.Lerp(new Color(0.12f, 0.16f, 0.3f, 0.07f), new Color(0.75f, 0.85f, 1f, 0.09f), day);
                    em.rateOverTime = 7f;
                    break;
                default:
                    if (night > 0.5f)
                    {
                        // Fireflies in the fields, a few cold flecks in town: small, slow, glowing.
                        bool fields = CurrentZone == Zone.Fields;
                        m.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.14f);
                        m.startLifetime = 6f; m.startSpeed = 0.25f;
                        m.startColor = fields ? new Color(0.7f, 1f, 0.35f, 0.9f) : new Color(0.55f, 0.8f, 1f, 0.4f);
                        em.rateOverTime = fields ? 9f : 4f;
                    }
                    else
                    {
                        // Pollen in the fields, dust in town: tiny, warm, drifting.
                        m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
                        m.startLifetime = 8f; m.startSpeed = 0.15f;
                        m.startColor = CurrentZone == Zone.Fields ? new Color(1f, 0.95f, 0.65f, 0.55f * day) : new Color(1f, 0.9f, 0.75f, 0.35f * day);
                        em.rateOverTime = 14f * day;
                    }
                    break;
            }
        }

        /// <summary>Dust and wisps drift smoothly; fireflies wander in erratic hops and blink on and off.</summary>
        private void SetMoteLook(int look)
        {
            if (look == moteLook) return;
            moteLook = look;
            var col = motes.colorOverLifetime;
            var noise = motes.noise;
            if (dustFade == null)
            {
                dustFade = new Gradient();
                dustFade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
                bugBlink = new Gradient();   // on, off, on, off ...: a glow that blinks about four times in a life
                bugBlink.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.06f), new GradientAlphaKey(1f, 0.16f), new GradientAlphaKey(0f, 0.2f),
                            new GradientAlphaKey(0f, 0.38f), new GradientAlphaKey(1f, 0.42f), new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0f, 0.6f) });
            }
            col.color = look == 1 ? bugBlink : dustFade;
            noise.enabled = look != 2;
            if (look == 1)
            {
                // Erratic: strong, quick, uneven wander (fast frequency, scrolling), so it hops and curls instead of drifting.
                noise.separateAxes = true;
                noise.strengthX = 1.6f; noise.strengthY = 0.9f; noise.strengthZ = 1.6f;
                noise.frequency = 1.4f; noise.scrollSpeed = 1.8f; noise.damping = false; noise.quality = ParticleSystemNoiseQuality.Low;
                noise.positionAmount = 1f; noise.octaveCount = 2;
            }
            else
            {
                noise.separateAxes = false;
                noise.strength = 0.12f; noise.frequency = 0.15f; noise.scrollSpeed = 0.1f; noise.damping = true;
                noise.positionAmount = 1f;
            }
        }

        // ---------------------------------------------------------------- L: light shafts

        private void Shafts(Vector3 me, Vector3 sunDir, float night, float rain, float dt)
        {
            rainMemory = rain > 0.05f ? 1f : Mathf.MoveTowards(rainMemory, 0f, dt / 240f);
            if (shaftRoot == null)
            {
                var mat = Resources.Load<Material>("KN/LightShaft");
                if (mat == null) return;
                shaftRoot = new GameObject("LightShafts") { hideFlags = HideFlags.DontSave };
                shafts = new Transform[ShaftSpots.Length];
                shaftRenderers = new MeshRenderer[ShaftSpots.Length];
                shaftBlock = new MaterialPropertyBlock();
                for (int i = 0; i < shafts.Length; i++)
                {
                    var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Object.Destroy(q.GetComponent<Collider>());
                    q.name = "Shaft " + i;
                    q.transform.SetParent(shaftRoot.transform, false);
                    var r = q.GetComponent<MeshRenderer>();
                    r.sharedMaterial = mat;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    shafts[i] = q.transform;
                    shaftRenderers[i] = r;
                }
            }

            // Strongest low in the morning, and in the hour after rain; gone at night and when the sun is high.
            float elevation = sunDir.y;
            float low = Mathf.Clamp01(1f - Mathf.Abs(elevation - 0.3f) / 0.3f);
            float strength = (night > 0.3f ? 0f : low) * Mathf.Lerp(0.55f, 1f, rainMemory) * (rain > 0.3f ? 0.3f : 1f);
            Vector3 along = -sunDir.normalized;              // from the sun toward the ground
            for (int i = 0; i < shafts.Length; i++)
            {
                Vector3 spot = ShaftSpots[i];
                bool onConcord = spot.y > ConcordMinY;
                bool near = (CurrentZone == Zone.Concord) == onConcord && (new Vector2(spot.x - me.x, spot.z - me.z)).sqrMagnitude < 90f * 90f;
                if (!near || strength < 0.02f || elevation < 0.05f) { shaftRenderers[i].enabled = false; continue; }
                shaftRenderers[i].enabled = true;
                float len = 14f, width = 4f;
                Vector3 top = spot + Vector3.up * 0.1f + (sunDir.normalized * 0.35f + Vector3.up).normalized * len;   // mostly upright, leaning toward the sun
                shafts[i].position = (top + spot) * 0.5f;
                Vector3 axis = (top - spot).normalized;
                // A strip standing on its long edge, facing the camera as far as its axis allows.
                Camera cam = Camera.main;
                Vector3 toCam = cam != null ? cam.transform.position - shafts[i].position : Vector3.back;
                Vector3 face = Vector3.ProjectOnPlane(toCam, axis);
                if (face.sqrMagnitude < 0.001f) face = Vector3.right;
                shafts[i].rotation = Quaternion.LookRotation(-face.normalized, axis);
                shafts[i].localScale = new Vector3(width, len, 1f);
                shaftBlock.SetColor(BaseColor, new Color(1f, 0.88f, 0.62f, 1f) * (1.3f * strength));
                shaftRenderers[i].SetPropertyBlock(shaftBlock);
            }
        }
    }
}
