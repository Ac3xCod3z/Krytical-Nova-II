using KryticalNova.Audio;
using KryticalNova.Combat;
using KryticalNova.Core;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;
using KryticalNova.Pets;
using KryticalNova.Player;
using KryticalNova.UI;
using Unity.Cinemachine;
using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// The Beat Ring (owner, 22 Oct 2026): a small equaliser round this machine's own pet, shown only in a fight, so
    /// the player can see the beat Cadence is judged by. Its bars stand in a ring at the pet's feet and dance to the
    /// fight music (its spectrum on this machine, each band measured against its own loudest of late so every bar
    /// moves), rising quickly and settling smoothly; every bar jumps on the beat and the whole ring pulses out, more on
    /// the first beat of a bar; a bright comet runs round the ring once a beat and is at the front (toward the camera)
    /// on the beat, and the whole ring flares in the Cadence window, which is when to press. Its colours are its own,
    /// whatever the pet (owner, 23 Oct 2026). A skill pressed on the beat sends a burst of the faction's colour out of
    /// it; a step of the Cadence combo landing on the beat does more, bigger at each step: the bars leap, a shock ring
    /// runs out over the ground, the camera kicks and a note rises. A press off the beat shows nothing. With no pet out
    /// it stands at the player's feet. Drawn as one mesh, one material, one draw call; nothing of it is networked.
    /// Settings ▸ Audio can switch it off.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BeatRing : MonoBehaviour
    {
        private const int SpectrumSize = 512;
        // How often the pet is measured again (a pet that grows or changes its model).
        private const float MeasureEvery = 1f;

        [SerializeField] private BeatRingSettings settings;
        [Tooltip("An additive, unlit material that reads vertex colours (URP Particles/Unlit).")]
        [SerializeField] private Material material;

        private GameObject ring;
        private Mesh mesh;
        private Vector3[] vertices;
        private Color[] colours;
        private float[] spectrum, level, peak, peakAge, loudest;
        private int builtColumns, builtSegments, builtShock;
        private Transform anchor;
        private float anchorRadius = 0.6f, anchorHeight = 0.6f, measuredAt = -10f;
        // With no pet out the ring lies on the ground round the player's feet (owner, 24 Oct 2026: at the waist it is
        // distracting): the root stands a metre up, so the ground under it is found.
        private bool onPlayer;
        private const float GroundLift = 0.04f, GroundProbe = 3f;
        private float burst, pulse, shock = 1f, leap, caught;
        private int step;
        private long lastBeat = -1;
        private float beatKickNow;
        private AudioSource stepAudio;
        private CinemachineImpulseSource kick;

        private void OnEnable()
        {
            PlayerCadence.LocalPress += OnPress;
            PlayerCadence.LocalCombo += OnCombo;
        }

        private void OnDisable()
        {
            PlayerCadence.LocalPress -= OnPress;
            PlayerCadence.LocalCombo -= OnCombo;
            if (ring != null) ring.SetActive(false);
        }

        private void OnDestroy()
        {
            if (ring != null) Destroy(ring);
            if (mesh != null) Destroy(mesh);
        }

        private void OnPress(double error, bool onBeat)
        {
            if (onBeat) { burst = 1f; caught = 1f; }
        }

        // A step of the combo landed on the beat: the bigger moment, bigger at each step.
        private void OnCombo(int next, float seconds, bool redline)
        {
            if (next <= 0 || settings == null || ring == null || !ring.activeSelf) return;
            step = Mathf.Clamp(next, 1, CadenceCombo.Top);
            burst = 1f;
            shock = 0f;
            leap = 1f;
            PlayStepSound();
            Kick();
        }

        private void LateUpdate()
        {
            if (settings == null || material == null || Application.isBatchMode) return;
            float live = AudioPreferences.BeatRing ? CombatBeat.Live01 : 0f;
            float dt = Time.unscaledDeltaTime;
            burst = Mathf.MoveTowards(burst, 0f, dt / settings.burstSeconds);
            shock = Mathf.MoveTowards(shock, 1f, dt / settings.shockSeconds);
            leap = Mathf.MoveTowards(leap, 0f, dt / 0.35f);
            caught = Mathf.MoveTowards(caught, 0f, dt / settings.caughtSeconds);
            if (live <= 0.01f || !FindAnchor())
            {
                if (ring != null && ring.activeSelf) ring.SetActive(false);
                return;
            }
            Ensure();
            if (!ring.activeSelf) ring.SetActive(true);
            Listen(dt);
            Draw(live, dt);
        }

        // ------------------------------------------------------------------ where it stands

        // The pet that is out, else the player itself; measured when a new one is found and again now and then.
        private bool FindAnchor()
        {
            Transform now = null;
            PlayerPet pet = PlayerPet.Local;
            if (pet != null && pet.Pet != null && pet.TryGetComponent(out PetFollower follower)) now = follower.PetTransform;
            onPlayer = now == null;
            if (now == null && PlayerInventory.Local != null) now = PlayerInventory.Local.transform;
            if (now == null) return false;
            if (now != anchor || Time.unscaledTime - measuredAt > MeasureEvery)
            {
                anchor = now;
                Measure();
            }
            return true;
        }

        private void Measure()
        {
            measuredAt = Time.unscaledTime;
            var bounds = new Bounds(anchor.position, Vector3.zero);
            bool any = false;
            foreach (Renderer each in anchor.GetComponentsInChildren<Renderer>())
            {
                if (each is ParticleSystemRenderer || each is TrailRenderer || each is LineRenderer || !each.enabled) continue;
                if (ring != null && each.transform.IsChildOf(ring.transform)) continue;
                if (!any) { bounds = each.bounds; any = true; } else bounds.Encapsulate(each.bounds);
            }
            float width = any ? Mathf.Max(bounds.extents.x, bounds.extents.z) : 0.5f;
            anchorRadius = Mathf.Clamp(width * settings.radiusScale * 2f, settings.radiusLimits.x, settings.radiusLimits.y);
            anchorHeight = any ? bounds.size.y : 1f;
        }

        // ------------------------------------------------------------------ what it hears

        private void Listen(float dt)
        {
            int columns = settings.columns, bands = columns / 2;
            bool heard = CombatMusicPlayer.TrySpectrum(spectrum);
            // On each new beat every bar jumps and the ring pulses, more on the first beat of a bar.
            if (CombatBeat.HasBeat && CombatBeat.Index != lastBeat)
            {
                lastBeat = CombatBeat.Index;
                beatKickNow = CombatBeat.OnBar ? settings.barKick : settings.beatKick;
                pulse = CombatBeat.OnBar ? settings.barPulse : settings.beatPulse;
            }
            beatKickNow = Mathf.MoveTowards(beatKickNow, 0f, dt * 2.5f);
            pulse = Mathf.MoveTowards(pulse, 0f, dt * 0.9f);

            // Each band against its own loudest of late (which slowly lets go), and all of them against the music's
            // loudest band, so a quiet band stays low but every band that is playing moves.
            float decay = Mathf.Pow(0.5f, dt / settings.adaptHalfLife);
            float overall = 1e-5f;
            for (int b = 0; b < bands; b++)
            {
                float now = heard ? Band(b, bands) : 0f;
                loudest[b] = Mathf.Max(now, loudest[b] * decay, 1e-5f);
                overall = Mathf.Max(overall, loudest[b]);
            }
            float rise = 1f - Mathf.Exp(-dt / settings.attackSeconds);
            float settle = 1f - Mathf.Exp(-dt / settings.releaseSeconds);
            for (int c = 0; c < columns; c++)
            {
                // The ring is mirrored left to right, so the bass sits at the back and the treble meets at the front.
                int band = c < bands ? c : columns - 1 - c;
                float target = 0f;
                if (heard)
                {
                    float now = Band(band, bands);
                    float share = now / loudest[band];
                    float presence = Mathf.InverseLerp(settings.adaptFloor, 1f, loudest[band] / overall);
                    target = Smooth(Mathf.Clamp01(share)) * Mathf.Lerp(0.35f, 1f, presence);
                }
                target = Mathf.Clamp01(target + beatKickNow + leap * (0.6f + 0.2f * step));
                level[c] += (target - level[c]) * (target > level[c] ? rise : settle);
                if (level[c] >= peak[c]) { peak[c] = level[c]; peakAge[c] = 0f; }
                else
                {
                    peakAge[c] += dt;
                    if (peakAge[c] > settings.peakHold) peak[c] = Mathf.Lerp(peak[c], level[c], 1f - Mathf.Exp(-dt * settings.peakFall * 3f));
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
            int columns = settings.columns, segments = settings.segments, shockQuads = settings.shockSegments;
            if (ring != null && builtColumns == columns && builtSegments == segments && builtShock == shockQuads) return;
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
                stepAudio = ring.AddComponent<AudioSource>();
                stepAudio.playOnAwake = false;
                stepAudio.spatialBlend = 0f;
                kick = ring.AddComponent<CinemachineImpulseSource>();
                kick.ImpulseDefinition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump;
                kick.ImpulseDefinition.ImpulseDuration = 0.18f;
            }
            builtColumns = columns;
            builtSegments = segments;
            builtShock = shockQuads;
            int quads = columns * segments + shockQuads * 2; // the shock ring, then the closing ring
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
            loudest = new float[columns / 2 + 1];
        }

        private void Draw(float live, float dt)
        {
            int columns = settings.columns, segments = settings.segments;
            Camera view = Camera.main;
            Vector3 centre = onPlayer ? GroundUnder(anchor) : anchor.position + Vector3.up * (anchorHeight * settings.standAt);
            ring.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Vector3 toCamera = view != null ? view.transform.position - centre : -Vector3.forward;
            toCamera.y = 0f;
            if (toCamera.sqrMagnitude < 1e-4f) toCamera = Vector3.back;
            toCamera.Normalize();
            float front = Mathf.Atan2(toCamera.z, toCamera.x);
            Vector3 right = view != null ? view.transform.right : Vector3.right;

            float swell = settings.burstSwell + (step > 0 ? StepValue(settings.stepSwell, step) * leap : 0f);
            float radius = anchorRadius * (1f + swell * Smooth(burst) + pulse);
            float barWidth = 2f * Mathf.PI * radius / columns * settings.barWidth;
            float full = anchorRadius * settings.barHeight * (1f + 0.5f * leap);
            float segment = full / segments;
            float gap = segment * settings.segmentGap;

            // The comet: at the front on the beat, once round in a beat.
            float phase = CombatBeat.HasBeat ? CombatBeat.Phase01 : 0f;
            float comet = front + phase * Mathf.PI * 2f;
            bool inWindow = false;
            PlayerCadence cadence = LocalPlayer.Get<PlayerCadence>();
            if (cadence != null && cadence.Settings != null && CombatBeat.TryError(out double error))
                inWindow = System.Math.Abs(error) <= cadence.Settings.WindowSeconds;

            Color burstColour = BurstColour();
            float alpha = settings.alpha * live;
            float flash = Smooth(burst);
            int v = 0;
            for (int c = 0; c < columns; c++)
            {
                float angle = front + Mathf.PI + (c + 0.5f) / columns * Mathf.PI * 2f; // column 0 at the back
                Vector3 foot = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                Vector3 half = right * (barWidth * 0.5f);
                float near = Mathf.DeltaAngle(angle * Mathf.Rad2Deg, comet * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                float boost = 1f + settings.cometBoost * Mathf.Exp(-(near * near) / (settings.cometWidth * settings.cometWidth));
                // A lit segment fades in over its own height, so a bar moves smoothly instead of a block at a time.
                float litExact = level[c] * segments;
                int cap = Mathf.Clamp(Mathf.RoundToInt(peak[c] * segments) - 1, 0, segments - 1);
                for (int s = 0; s < segments; s++)
                {
                    float y0 = s * segment, y1 = y0 + segment - gap;
                    float share = s / (float)Mathf.Max(1, segments - 1);
                    Color colour = share < 0.5f ? Color.Lerp(settings.low, settings.mid, share * 2f) : Color.Lerp(settings.mid, settings.high, (share - 0.5f) * 2f);
                    float on = Mathf.Clamp01(litExact - s);
                    bool isCap = s == cap && peak[c] > 0.02f && on < 0.5f;
                    float lit = isCap ? 1f : on;
                    float a = Mathf.Lerp(alpha * settings.ghost, alpha, lit);
                    if (isCap) colour = settings.high;
                    if (inWindow) colour = Color.Lerp(colour, settings.windowFlare, settings.windowFlareStrength);
                    if (flash > 0f) colour = Color.Lerp(colour, burstColour, flash * 0.8f);
                    colour *= boost * (1f + flash * 0.6f);
                    colour.a = Mathf.Clamp01(a * (lit > 0f ? Mathf.Min(boost, 1.6f) : 1f));
                    vertices[v] = foot - half + Vector3.up * y0;
                    vertices[v + 1] = foot - half + Vector3.up * y1;
                    vertices[v + 2] = foot + half + Vector3.up * y1;
                    vertices[v + 3] = foot + half + Vector3.up * y0;
                    colours[v] = colours[v + 1] = colours[v + 2] = colours[v + 3] = colour;
                    v += 4;
                }
            }
            DrawShock(centre, burstColour, live, ref v);
            DrawApproach(centre, live, inWindow, ref v);
            mesh.vertices = vertices;
            mesh.colors = colours;
            mesh.RecalculateBounds();
        }

        // The closing ring (owner, 24 Oct 2026: "I have no way to truly understand when to hit on beat"): a flat ring that
        // shrinks onto the Beat Ring once a beat and meets it on the beat, as rhythm games show when to press. In the
        // Cadence window it sits on the ring in gold; before the first beat of a bar it is wider and warmer (the count-in).
        private void DrawApproach(Vector3 centre, float live, bool inWindow, ref int v)
        {
            int count = settings.shockSegments;
            bool shown = settings.approach && CombatBeat.HasBeat && live > 0.01f;
            MusicTrack track = CombatBeat.Track;
            bool barNext = shown && settings.countIn && track != null && BeatMath.IsBarBeat(CombatBeat.Index + 1, track.barBeat, track.beatsPerBar);
            float toBeat = 1f - CombatBeat.Phase01; // 1 just after a beat, 0 on the next
            float reach = anchorRadius * (1f + settings.approachReach * (barNext ? 1.35f : 1f) * Smooth(toBeat));
            float width = settings.approachWidth * (barNext ? 1.5f : 1f);
            Color c;
            if (caught > 0f)
            {
                // Caught on the beat: the closing ring locks onto the Beat Ring in bright gold and pulses out of it.
                float pulseOut = Smooth(caught);
                reach = anchorRadius * (1f + 0.1f * (1f - pulseOut));
                width *= 1.8f + 1.4f * pulseOut;
                c = settings.approachWindow * (1.6f + 1.2f * pulseOut);
                c.a = live * Mathf.Clamp01(0.4f + pulseOut);
            }
            else if (inWindow)
            {
                reach = anchorRadius;
                width *= 1.8f;
                c = settings.approachWindow * 1.8f;
                c.a = live;
            }
            else
            {
                c = barNext ? settings.approachBar : settings.approachColour;
                // Faint as it sets out, brightest as it closes in.
                c.a = live * settings.approachAlpha * Mathf.Lerp(0.15f, 1f, 1f - toBeat);
            }
            if (!shown && caught <= 0f) c.a = 0f;
            Vector3 ground = centre + Vector3.up * 0.025f;
            for (int i = 0; i < count; i++)
            {
                float a0 = i / (float)count * Mathf.PI * 2f, a1 = (i + 1) / (float)count * Mathf.PI * 2f;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                vertices[v] = ground + d0 * (reach - width * 0.5f);
                vertices[v + 1] = ground + d0 * (reach + width * 0.5f);
                vertices[v + 2] = ground + d1 * (reach + width * 0.5f);
                vertices[v + 3] = ground + d1 * (reach - width * 0.5f);
                colours[v] = colours[v + 1] = colours[v + 2] = colours[v + 3] = c;
                v += 4;
            }
        }

        // The ground under the player, a hair above it, so the ring lies at their feet; the root's own height less the
        // agent's offset when nothing is hit (in the air over a drop).
        private static Vector3 GroundUnder(Transform player)
        {
            Vector3 from = player.position + Vector3.up * 0.2f;
            int mask = ~(1 << player.gameObject.layer);
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, GroundProbe, mask, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * GroundLift;
            float offset = player.TryGetComponent(out UnityEngine.AI.NavMeshAgent agent) ? agent.baseOffset : 1f;
            return player.position + Vector3.up * (GroundLift - offset);
        }

        // The shock ring of a combo step: a flat band that runs out over the ground and fades, further at each step.
        private void DrawShock(Vector3 centre, Color colour, float live, ref int v)
        {
            int count = settings.shockSegments;
            float t = Smooth(shock);
            bool shown = shock < 1f && step > 0;
            float reach = anchorRadius * Mathf.Lerp(1f, settings.shockReach * (1f + 0.35f * (step - 1)), t);
            float width = settings.shockWidth * (1f + 0.5f * (step - 1)) * (1f - 0.5f * t);
            Color c = colour * 1.6f;
            c.a = shown ? (1f - t) * live : 0f;
            Vector3 ground = centre + Vector3.up * 0.02f;
            for (int i = 0; i < count; i++)
            {
                float a0 = i / (float)count * Mathf.PI * 2f, a1 = (i + 1) / (float)count * Mathf.PI * 2f;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                vertices[v] = ground + d0 * reach;
                vertices[v + 1] = ground + d0 * (reach + width);
                vertices[v + 2] = ground + d1 * (reach + width);
                vertices[v + 3] = ground + d1 * reach;
                colours[v] = colours[v + 1] = colours[v + 2] = colours[v + 3] = c;
                v += 4;
            }
        }

        private void PlayStepSound()
        {
            if (stepAudio == null || settings.stepSound == null) return;
            stepAudio.outputAudioMixerGroup = settings.stepSoundGroup;
            stepAudio.pitch = 1f + settings.stepPitchRise * (step - 1);
            stepAudio.PlayOneShot(settings.stepSound, settings.stepSoundVolume);
        }

        private void Kick()
        {
            if (kick == null || settings.stepKick <= 0f || !HudPreferences.ScreenShake) return;
            float force = settings.stepKick * (0.6f + 0.4f * step);
            kick.GenerateImpulseWithVelocity(new Vector3(0f, -force, 0f));
        }

        private static float StepValue(Vector3 perStep, int at) => at <= 1 ? perStep.x : at == 2 ? perStep.y : perStep.z;

        private static float Smooth(float x) => x * x * (3f - 2f * x);

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
