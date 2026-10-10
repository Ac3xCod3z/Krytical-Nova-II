using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KryticalNova.Rendering
{
    /// <summary>
    /// Where the Ascension Novas in view are, for what is drawn over the frame afterwards and so cannot be hidden by
    /// a Nova's own depth: the glow halo and the ink. Each Nova reports itself every frame it is shown; the shaders
    /// get the list as <c>_KN_NovaDiscs</c> (xyz = its middle in the world, w = its radius in metres) and
    /// <c>_KN_NovaDiscCount</c>, and leave out what lies behind one.
    /// <para>The ink is drawn from the depth texture, which still holds the actors a Nova hides and has nothing to
    /// tell them from the wall behind. So the actors that stand behind a Nova are found here, as balls
    /// (<c>_KN_NovaHidden</c>: xyz = middle, w = radius; <c>_KN_NovaHiddenCount</c>), and the ink is left out only
    /// inside one of them: a wall or the ground behind a Nova keeps its lines. <c>_KN_NovaFeather</c> is the share of
    /// a disc's radius over which what it hides comes back into view. See Assets/_Game/_Docs/Design/Rendering-Bible.md.</para>
    /// </summary>
    public static class NovaCull
    {
        /// <summary>The most Novas that hide the light behind them in one frame. Matches the shader's array.</summary>
        public const int MaxDiscs = 8;
        /// <summary>The most actors behind a Nova whose ink is left out in one frame. Matches the shader's array.</summary>
        public const int MaxHidden = 8;
        /// <summary>A ball is drawn this much larger than the models it holds: an outline is drawn a little outside them.</summary>
        private const float BallSlack = 1.1f;

        private static readonly int DiscsId = Shader.PropertyToID("_KN_NovaDiscs");
        private static readonly int CountId = Shader.PropertyToID("_KN_NovaDiscCount");
        private static readonly int HiddenId = Shader.PropertyToID("_KN_NovaHidden");
        private static readonly int HiddenCountId = Shader.PropertyToID("_KN_NovaHiddenCount");
        private static readonly int FeatherId = Shader.PropertyToID("_KN_NovaFeather");
        private static readonly Vector4[] Discs = new Vector4[MaxDiscs];
        private static readonly ActorDrawOrder[] Owners = new ActorDrawOrder[MaxDiscs];
        private static readonly Vector4[] Hidden = new Vector4[MaxHidden];
        private static int count;
        private static int frame = -1;
        private static float feather;
        private static Camera eye;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Begin()
        {
            count = 0;
            frame = -1;
            RenderPipelineManager.beginContextRendering -= Push;
            RenderPipelineManager.beginContextRendering += Push;
        }

        /// <summary>
        /// A Nova that is shown this frame: its middle, the radius of the disc that hides what is behind it (metres),
        /// the share of that radius over which the hidden comes back into view, and the character that wears it
        /// (never hidden by its own Nova).
        /// </summary>
        public static void Report(Vector3 centre, float radius, float rimShare = 0f, ActorDrawOrder owner = null)
        {
            if (frame != Time.frameCount)
            {
                frame = Time.frameCount;
                count = 0;
            }
            if (count >= MaxDiscs || radius <= 0f) return;
            Owners[count] = owner;
            Discs[count++] = new Vector4(centre.x, centre.y, centre.z, radius);
            feather = Mathf.Clamp01(rimShare);
        }

        private static void Push(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (frame != Time.frameCount) count = 0; // none was shown this frame
            Shader.SetGlobalVectorArray(DiscsId, Discs);
            Shader.SetGlobalFloat(CountId, count);
            Shader.SetGlobalFloat(FeatherId, feather);
            Shader.SetGlobalVectorArray(HiddenId, Hidden);
            Shader.SetGlobalFloat(HiddenCountId, count > 0 ? FindHidden() : 0);
        }

        /// <summary>
        /// Fills <see cref="Hidden"/> with the actors that stand behind a Nova as the player's camera sees it: any
        /// part of the actor's ball farther off than the Nova, and inside the cone the Nova's disc covers.
        /// </summary>
        private static int FindHidden()
        {
            if (eye == null) eye = Camera.main;
            if (eye == null) return 0;
            Vector3 from = eye.transform.position;
            int hidden = 0;
            IReadOnlyList<ActorDrawOrder> actors = ActorDrawOrder.All;
            for (int a = 0; a < actors.Count && hidden < MaxHidden; a++)
            {
                ActorDrawOrder actor = actors[a];
                if (actor == null || actor.DrawWithScenery || !actor.TryGetBall(out Vector3 centre, out float radius)) continue;
                radius *= BallSlack;
                for (int i = 0; i < count; i++)
                {
                    if (Owners[i] == actor) continue;
                    Vector4 disc = Discs[i];
                    Vector3 toNova = new Vector3(disc.x, disc.y, disc.z) - from;
                    float novaAway = toNova.magnitude;
                    if (novaAway < 0.05f) continue;
                    Vector3 line = toNova / novaAway;
                    Vector3 toActor = centre - from;
                    float along = Vector3.Dot(toActor, line);
                    if (along + radius < novaAway) continue; // all of it is nearer than the Nova
                    float aside = (toActor - line * along).magnitude;
                    float discThere = disc.w * Mathf.Max(along, novaAway) / novaAway; // the disc's cone, at the actor's depth
                    if (aside > discThere + radius) continue;
                    Hidden[hidden++] = new Vector4(centre.x, centre.y, centre.z, radius);
                    break;
                }
            }
            return hidden;
        }
    }
}
