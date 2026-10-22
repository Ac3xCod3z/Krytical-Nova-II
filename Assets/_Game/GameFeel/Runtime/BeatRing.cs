using KryticalNova.Audio;
using KryticalNova.Combat;
using KryticalNova.Core;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;
using KryticalNova.Pets;
using KryticalNova.Player;
using KryticalNova.UI;
using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// The Beat Ring (owner, 22 Oct 2026): a small equaliser round this machine's own pet, shown only in a fight, so
    /// the player can see the beat Cadence is judged by. Its bars stand in a ring at the pet's feet and dance to the
    /// fight music (its spectrum on this machine), every bar jumps on the beat and higher on the first of a bar, a
    /// bright comet runs round the ring once a beat and is at the front (toward the camera) on the beat, and the whole
    /// ring flares in the Cadence window, which is when to press. A skill pressed on the beat sends a burst of the
    /// faction's colour out of it; one off the beat greys it for a moment. With no pet out it stands at the player's
    /// feet. Drawn as one mesh, one material, one draw call; nothing of it is networked. Settings ▸ Audio can switch it off.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BeatRing : MonoBehaviour
    {
        private const int SpectrumSize = 512;

        [SerializeField] private BeatRingSettings settings;
        [Tooltip("An additive, unlit material that reads vertex colours (URP Particles/Unlit).")]
        [SerializeField] private Material material;

        private GameObject ring;
        private Mesh mesh;
        private Vector3[] vertices;
        private Color[] colours;
        private float[] spectrum, level, peak, peakAge;
        private int builtColumns, builtSegments;
        private Transform anchor;
        private float anchorRadius = 0.6f, anchorHeight = 0.6f;
        private float burst, offBeat;
        private long lastBeat = -1;
        private float beatKickNow;

        private void OnEnable() => PlayerCadence.LocalPress += OnPress;

        private void OnDisable()
        {
            PlayerCadence.LocalPress -= OnPress;
            if (ring != null) ring.SetActive(false);
        }

        private void OnDestroy()
        {
            if (ring != null) Destroy(ring);
            if (mesh != null) Destroy(mesh);
        }

        private void OnPress(double error, bool onBeat)
        {
            if (onBeat) burst = 1f;
            else offBeat = 1f;
        }

        private void LateUpdate()
        {
            if (settings == null || material == null || Application.isBatchMode) return;
            float live = AudioPreferences.BeatRing ? CombatBeat.Live01 : 0f;
            float dt = Time.unscaledDeltaTime;
            burst = Mathf.MoveTowards(burst, 0f, dt / settings.burstSeconds);
            offBeat = Mathf.MoveTowards(offBeat, 0f, dt / settings.offBeatSeconds);
            if (live <= 0.01f || !FindAnchor())
            {
                if (ring != null && ring.activeSelf) ring.SetActive(false);
                return;
            }
            Ensure();
            if (!ring.activeSelf) ring.SetActive(true);
            Listen(dt);
            Draw(live);
        }

        // ------------------------------------------------------------------ where it stands

        // The pet that is out, else the player itself; its size is measured once a new one is found.
        private bool FindAnchor()
        {
            Transform now = null;
            PlayerPet pet = PlayerPet.Local;
            if (pet != null && pet.Pet != null && pet.TryGetComponent(out PetFollower follower)) now = follower.PetTransform;
            if (now == null && PlayerInventory.Local != null) now = PlayerInventory.Local.transform;
            if (now == null) return false;
            if (now != anchor)
            {
                anchor = now;
                Measure();
            }
            return true;
        }

        private void Measure()
        {
            var bounds = new Bounds(anchor.position, Vector3.zero);
            bool any = false;
            foreach (Renderer each in anchor.GetComponentsInChildren<Renderer>())
            {
                if (each is ParticleSystemRenderer || each is TrailRenderer || each is LineRenderer || !each.enabled) continue;
                if (!any) { bounds = each.bounds; any = true; } else bounds.Encapsulate(each.bounds);
            }
            float width = any ? Mathf.Max(bounds.extents.x, bounds.extents.z) : 0.5f;
            anchorRadius = Mathf.Clamp(width * settings.radiusScale * 2f, settings.radiusLimits.x, settings.radiusLimits.y);
            anchorHeight = any ? bounds.size.y : 1f;
        }

        // ------------------------------------------------------------------ what it hears

        private void Listen(float dt)
        {
            int columns = settings.columns;
            bool heard = CombatMusicPlayer.TrySpectrum(spectrum);
            // On each new beat every bar jumps, more on the first beat of a bar.
            if (CombatBeat.HasBeat && CombatBeat.Index != lastBeat)
            {
                lastBeat = CombatBeat.Index;
                beatKickNow = CombatBeat.OnBar ? settings.barKick : settings.beatKick;
            }
            beatKickNow = Mathf.MoveTowards(beatKickNow, 0f, dt * 2.5f);
            for (int c = 0; c < columns; c++)
            {
                // The ring is mirrored left to right, so the bass sits at the back and the treble meets at the front.
                int band = c < columns / 2 ? c : columns - 1 - c;
                float target = heard ? Band(band, columns / 2) : 0f;
                target = Mathf.Clamp01(target + beatKickNow);
                level[c] = target > level[c] ? Mathf.MoveTowards(level[c], target, dt * settings.rise)
                                             : Mathf.MoveTowards(level[c], target, dt * settings.fall);
                if (level[c] >= peak[c]) { peak[c] = level[c]; peakAge[c] = 0f; }
                else
                {
                    peakAge[c] += dt;
                    if (peakAge[c] > settings.peakHold) peak[c] = Mathf.MoveTowards(peak[c], level[c], dt * settings.peakFall);
                }
            }
        }

        // One band of a log scale over the spectrum, from about 40 Hz to about 11 kHz.
        private float Band(int band, int bands)
        {
            float from = Mathf.Pow(2f, Mathf.Lerp(1f, 8.2f, band / (float)bands));
            float to = Mathf.Pow(2f, Mathf.Lerp(1f, 8.2f, (band + 1) / (float)bands));
            int start = Mathf.Clamp((int)from, 1, SpectrumSize - 1), end = Mathf.Clamp(Mathf.CeilToInt(to), start + 1, SpectrumSize);
            float sum = 0f;
            for (int i = start; i < end; i++) sum += spectrum[i];
            // Higher bands are quieter: lift them so the treble shows.
            float tilt = 1f + band * 0.35f;
            return Mathf.Sqrt(sum / (end - start)) * settings.gain * tilt;
        }

        // ------------------------------------------------------------------ the mesh

        private void Ensure()
        {
            int columns = settings.columns, segments = settings.segments;
            if (ring != null && builtColumns == columns && builtSegments == segments) return;
            if (ring == null)
            {
                ring = new GameObject("Beat Ring");
                ring.transform.SetParent(transform, false);
                mesh = new Mesh { name = "Beat Ring" };
                mesh.MarkDynamic();
                ring.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = ring.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            builtColumns = columns;
            builtSegments = segments;
            int quads = columns * segments;
            vertices = new Vector3[quads * 4];
            colours = new Color[quads * 4];
            var triangles = new int[quads * 6];
            for (int q = 0; q < quads; q++)
            {
                triangles[q * 6] = q * 4; triangles[q * 6 + 1] = q * 4 + 1; triangles[q * 6 + 2] = q * 4 + 2;
                triangles[q * 6 + 3] = q * 4; triangles[q * 6 + 4] = q * 4 + 2; triangles[q * 6 + 5] = q * 4 + 3;
            }
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.colors = colours;
            mesh.triangles = triangles;
            spectrum = new float[SpectrumSize];
            level = new float[columns];
            peak = new float[columns];
            peakAge = new float[columns];
        }

        private void Draw(float live)
        {
            int columns = settings.columns, segments = settings.segments;
            Camera view = Camera.main;
            Vector3 centre = anchor.position + Vector3.up * (anchorHeight * settings.standAt);
            ring.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Vector3 toCamera = view != null ? view.transform.position - centre : -Vector3.forward;
            toCamera.y = 0f;
            if (toCamera.sqrMagnitude < 1e-4f) toCamera = Vector3.back;
            toCamera.Normalize();
            float front = Mathf.Atan2(toCamera.z, toCamera.x);
            Vector3 right = view != null ? view.transform.right : Vector3.right;

            float radius = anchorRadius * (1f + settings.burstSwell * Smooth(burst));
            float barWidth = 2f * Mathf.PI * radius / columns * settings.barWidth;
            float full = anchorRadius * settings.barHeight;
            float segment = full / segments;
            float gap = segment * settings.segmentGap;

            // The comet: at the front on the beat, once round in a beat.
            float phase = CombatBeat.HasBeat ? CombatBeat.Phase01 : 0f;
            float comet = front + phase * Mathf.PI * 2f;
            bool inWindow = false;
            PlayerCadence cadence = LocalPlayer.Get<PlayerCadence>();
            if (cadence != null && cadence.Settings != null && CombatBeat.TryError(out double error))
                inWindow = System.Math.Abs(error) <= cadence.Settings.WindowSeconds;

            Color lowColour = PetGlow(settings.low);
            Color burstColour = BurstColour();
            float alpha = settings.alpha * live;
            int v = 0;
            for (int c = 0; c < columns; c++)
            {
                float angle = front + Mathf.PI + (c + 0.5f) / columns * Mathf.PI * 2f; // column 0 at the back
                Vector3 foot = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                Vector3 half = right * (barWidth * 0.5f);
                float near = Mathf.DeltaAngle(angle * Mathf.Rad2Deg, comet * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                float boost = 1f + settings.cometBoost * Mathf.Exp(-(near * near) / (settings.cometWidth * settings.cometWidth));
                int lit = Mathf.RoundToInt(level[c] * segments);
                int cap = Mathf.Clamp(Mathf.RoundToInt(peak[c] * segments) - 1, 0, segments - 1);
                for (int s = 0; s < segments; s++)
                {
                    float y0 = s * segment, y1 = y0 + segment - gap;
                    float share = s / (float)Mathf.Max(1, segments - 1);
                    Color colour = share < 0.5f ? Color.Lerp(lowColour, settings.mid, share * 2f) : Color.Lerp(settings.mid, settings.high, (share - 0.5f) * 2f);
                    bool on = s < lit, isCap = s == cap && peak[c] > 0.02f;
                    float a = on || isCap ? alpha : alpha * settings.ghost;
                    if (isCap && !on) colour = settings.high;
                    if (inWindow) colour = Color.Lerp(colour, settings.windowFlare, settings.windowFlareStrength);
                    if (burst > 0f) colour = Color.Lerp(colour, burstColour, Smooth(burst) * 0.8f);
                    if (offBeat > 0f) colour = Color.Lerp(colour, Grey(colour), offBeat);
                    colour *= boost;
                    colour.a = Mathf.Clamp01(a * (on || isCap ? Mathf.Min(boost, 1.6f) : 1f));
                    vertices[v] = foot - half + Vector3.up * y0;
                    vertices[v + 1] = foot - half + Vector3.up * y1;
                    vertices[v + 2] = foot + half + Vector3.up * y1;
                    vertices[v + 3] = foot + half + Vector3.up * y0;
                    colours[v] = colours[v + 1] = colours[v + 2] = colours[v + 3] = colour;
                    v += 4;
                }
            }
            mesh.vertices = vertices;
            mesh.colors = colours;
            mesh.RecalculateBounds();
        }

        private static float Smooth(float x) => x * x * (3f - 2f * x);

        private static Color Grey(Color c)
        {
            float l = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
            return new Color(l, l, l, c.a) * 0.6f;
        }

        // The bottom of the bars wears the pet's own glow, when it has one.
        private static Color PetGlow(Color fallback)
        {
            PlayerPet pet = PlayerPet.Local;
            if (pet == null || pet.Pet == null || pet.Pet.stages == null) return fallback;
            int stage = Mathf.Clamp(pet.Stage, 0, pet.Pet.stages.Length - 1);
            Color glow = pet.Pet.stages[stage].glow;
            return glow.maxColorComponent > 0.05f ? glow : fallback;
        }

        private Color BurstColour()
        {
            PlayerIdentity identity = PlayerInventory.Local != null ? PlayerInventory.Local.GetComponent<PlayerIdentity>() : null;
            switch (identity != null ? identity.Faction : FactionId.Hegemony)
            {
                case FactionId.Null: return settings.burstNull;
                case FactionId.Covenant: return settings.burstCovenant;
                default: return settings.burstHegemony;
            }
        }
    }
}
