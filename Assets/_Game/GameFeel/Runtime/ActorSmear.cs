using System.Collections.Generic;
using KryticalNova.Items.Visuals;
using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// The motion smear on one character (<see cref="SmearSettings"/>). While a smear plays, a second skin is drawn
    /// over each of the character's skinned meshes with the smear package's shader: it shares the body's mesh and
    /// bones, so it is the body, with its trailing side pulled back to where the body was a moment ago. The skins are
    /// made on the first smear, kept, switched off between smears and drawn only while there is a stretch to see, so
    /// a character that is not smearing costs nothing. Purely local: added on demand by <see cref="MMF_Smear"/>, like
    /// <see cref="ActorVfx"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ActorSmear : MonoBehaviour
    {
        // The smear package's shader properties (smear_urp.shadergraph).
        private static readonly int PositionCurrentId = Shader.PropertyToID("position_current");
        private static readonly int PositionPreviousId = Shader.PropertyToID("position_previous");
        private static readonly int StrengthId = Shader.PropertyToID("_smear_strength");
        private static readonly int NoiseScaleId = Shader.PropertyToID("_noise_scale");
        private static readonly int MainTextureId = Shader.PropertyToID("_Main_texture");
        private static readonly int MainColorId = Shader.PropertyToID("_main_color");
        // Where a body's colours live, in the shaders the characters use (Sidekick first).
        private static readonly int[] ColourTextureIds =
        {
            Shader.PropertyToID("_ColorMap"), Shader.PropertyToID("_BaseMap"), Shader.PropertyToID("_MainTex"),
        };
        /// <summary>What a smear skin's object is called: other effects that walk a character's renderers leave it alone.</summary>
        public const string SkinName = "Smear Skin";
        private const int HistoryLength = 48;
        private const float ShortestSeen = 0.01f;       // a stretch shorter than this (metres) cannot be seen: the skin is not drawn
        private static readonly List<SkinnedMeshRenderer> Found = new List<SkinnedMeshRenderer>(8);

        private struct Skin
        {
            public SkinnedMeshRenderer body;
            public SkinnedMeshRenderer skin;
            public Material material;
        }

        private readonly List<Skin> skins = new List<Skin>(2);
        private readonly Vector3[] historyPoints = new Vector3[HistoryLength];
        private readonly float[] historyTimes = new float[HistoryLength];
        private int historyNewest, historyCount;

        private SmearSettings settings;
        private SmearProfile profile;
        private float started;
        private bool playing;
        // A smear with a sustain goes on after its life until it is let go (Release), then fades out.
        private float releasedAt = -1f;

        /// <summary>The character's smear, created on first use. Moments reported from a child land on the root.</summary>
        public static ActorSmear For(Transform actor)
        {
            if (actor == null) return null;
            actor = WorldTargets.RootOf(actor);
            return actor.TryGetComponent(out ActorSmear smear) ? smear : actor.gameObject.AddComponent<ActorSmear>();
        }

        /// <summary>The character's smear if it has one; nothing is made.</summary>
        public static ActorSmear Find(Transform actor)
        {
            if (actor == null) return null;
            actor = WorldTargets.RootOf(actor);
            return actor.TryGetComponent(out ActorSmear smear) ? smear : null;
        }

        /// <summary>
        /// The motion the smear belongs to is over (the run stopped, the feet landed): a smear kept up by its sustain
        /// fades out now. A smear still in its take-off plays that out and ends as it would have.
        /// </summary>
        public void Release()
        {
            if (playing && releasedAt < 0f) releasedAt = Time.time;
        }

        /// <summary>Starts a smear from where the character is now. A smear already playing is replaced.</summary>
        public void Play(SmearSettings with, in SmearProfile kind)
        {
            if (with == null || with.shader == null || kind.seconds <= 0f || !isActiveAndEnabled) return;
            settings = with;
            profile = kind;
            if (!Dress()) return;
            started = Time.time;
            releasedAt = -1f;
            historyCount = 0;
            Remember(Tracked());
            Show(true);
            Apply(Tracked(), Vector3.zero);
            playing = true;
        }

        public void Stop()
        {
            if (!playing) return;
            playing = false;
            Show(false);
        }

        private void LateUpdate()
        {
            if (!playing) return;
            float life = (Time.time - started) / profile.seconds;
            if (skins.Count == 0 || skins[0].body == null) { Stop(); return; }
            // Past its life the smear stays at its sustain while the motion goes on, and fades once it is let go.
            float floor = 0f;
            if (life >= 1f)
            {
                if (profile.sustain <= 0f) { Stop(); return; }
                float fade = releasedAt < 0f ? 1f : settings.releaseSeconds > 0f ? 1f - (Time.time - releasedAt) / settings.releaseSeconds : 0f;
                if (fade <= 0f) { Stop(); return; }
                floor = profile.sustain * fade;
            }

            Vector3 now = Tracked();
            // Put somewhere in one step: that is not motion, and a smear across it would be a spike through the world.
            if (settings.WasPlaced(historyPoints[historyNewest], now, Time.deltaTime)) { Stop(); return; }
            Remember(now);

            Vector3 back = Recall(Time.time - profile.trailSeconds, out float span) - now;
            Apply(now, profile.Stretch(back, span, life, floor));
        }

        private void OnDisable() => Stop();

        private void OnDestroy()
        {
            foreach (Skin skin in skins)
            {
                if (skin.skin != null) Destroy(skin.skin.gameObject);
                if (skin.material != null) Destroy(skin.material);
            }
            skins.Clear();
        }

        // ---------------------------------------------------------------- the second skin

        /// <summary>
        /// Makes sure every skinned mesh of the body has its smear skin, and that each skin wears the body's current
        /// mesh, bones and colours (gear changes rebuild the body). False when there is nothing to smear.
        /// </summary>
        private bool Dress()
        {
            GetComponentsInChildren(false, Found);
            // Bodies only: not the skins themselves, and not what the weapon mount carries (a bow is skinned too).
            for (int i = Found.Count - 1; i >= 0; i--)
            {
                SkinnedMeshRenderer body = Found[i];
                if (body.name == SkinName || body.sharedMesh == null || !body.enabled || body.GetComponentInParent<WeaponVisualMount>() != null)
                    Found.RemoveAt(i);
            }
            // Drop skins whose body is gone or no longer part of the character.
            for (int i = skins.Count - 1; i >= 0; i--)
            {
                if (skins[i].body != null && Found.Contains(skins[i].body)) continue;
                if (skins[i].skin != null) Destroy(skins[i].skin.gameObject);
                if (skins[i].material != null) Destroy(skins[i].material);
                skins.RemoveAt(i);
            }
            foreach (SkinnedMeshRenderer body in Found)
            {
                int at = skins.FindIndex(s => s.body == body);
                Skin skin = at >= 0 ? skins[at] : Make(body);
                skin.skin.sharedMesh = body.sharedMesh;
                skin.skin.bones = body.bones;
                skin.skin.rootBone = body.rootBone;
                skin.skin.localBounds = body.localBounds;
                Material look = body.sharedMaterial;
                Texture colours = null;
                if (look != null)
                    foreach (int id in ColourTextureIds)
                        if (look.HasProperty(id) && (colours = look.GetTexture(id)) != null) break;
                skin.material.SetTexture(MainTextureId, colours);
                skin.material.SetColor(MainColorId, Color.white);
                skin.material.SetFloat(NoiseScaleId, settings.noiseScale);
                if (at >= 0) skins[at] = skin; else skins.Add(skin);
            }
            Found.Clear();
            return skins.Count > 0;
        }

        private Skin Make(SkinnedMeshRenderer body)
        {
            var holder = new GameObject(SkinName) { layer = body.gameObject.layer, hideFlags = HideFlags.DontSave };
            holder.transform.SetParent(body.transform.parent, false);
            holder.transform.localPosition = body.transform.localPosition;
            holder.transform.localRotation = body.transform.localRotation;
            holder.transform.localScale = body.transform.localScale;
            var skin = holder.AddComponent<SkinnedMeshRenderer>();
            skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            skin.updateWhenOffscreen = body.updateWhenOffscreen;
            var material = new Material(settings.shader) { name = "Smear (" + body.name + ")", hideFlags = HideFlags.DontSave };
            skin.sharedMaterial = material;
            holder.SetActive(false);
            return new Skin { body = body, skin = skin, material = material };
        }

        private void Show(bool on)
        {
            foreach (Skin skin in skins)
                if (skin.skin != null) skin.skin.gameObject.SetActive(on);
        }

        /// <summary>Hands the package's shader what it works from: where the body is, and where it is stretched back to.</summary>
        private void Apply(Vector3 now, Vector3 back)
        {
            // With nothing to show the skin would only be the body drawn a second time, in the package's shading.
            bool seen = back.sqrMagnitude > ShortestSeen * ShortestSeen;
            foreach (Skin skin in skins)
            {
                if (skin.material == null || skin.skin == null) continue;
                skin.skin.enabled = seen;
                // An effect that swaps or copies a character's materials must not leave the skin on a copy we do not drive.
                if (skin.skin.sharedMaterial != skin.material) skin.skin.sharedMaterial = skin.material;
                skin.material.SetVector(PositionCurrentId, now);
                skin.material.SetVector(PositionPreviousId, now + back);
                skin.material.SetFloat(StrengthId, profile.strength);
            }
        }

        // ---------------------------------------------------------------- where the body has been

        /// <summary>The middle of the body, as the package tracks it: it follows the hips, so a thrown body is followed too.</summary>
        private Vector3 Tracked() => skins.Count > 0 && skins[0].body != null ? skins[0].body.bounds.center : transform.position;

        private void Remember(Vector3 point)
        {
            historyNewest = historyCount == 0 ? 0 : (historyNewest + 1) % HistoryLength;
            historyPoints[historyNewest] = point;
            historyTimes[historyNewest] = Time.time;
            if (historyCount < HistoryLength) historyCount++;
        }

        /// <summary>
        /// Where the body was at a moment in the past, or at the start of this smear when that is more recent.
        /// <paramref name="span"/> is how long ago the point returned really is.
        /// </summary>
        private Vector3 Recall(float when, out float span)
        {
            int index = historyNewest;
            for (int i = 1; i < historyCount; i++)
            {
                int older = (historyNewest - i + HistoryLength) % HistoryLength;
                if (historyTimes[older] < when)
                {
                    // Between this sample and the one after it.
                    float t = Mathf.InverseLerp(historyTimes[older], historyTimes[index], when);
                    span = Time.time - when;
                    return Vector3.Lerp(historyPoints[older], historyPoints[index], t);
                }
                index = older;
            }
            span = Time.time - historyTimes[index];
            return historyPoints[index];
        }
    }
}
