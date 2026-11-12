using KryticalNova.Core;
using KryticalNova.NPC;
using UnityEngine;

namespace KryticalNova.UI
{
    /// <summary>
    /// TEMPORARY (owner, 9 Nov 2026): the Marshal's "SKY LIFT (CONCORD 040)" button. The Marshal down in Zone01's Concord sends the
    /// player up to the Concord 040 preview that floats above it, the Marshal up there sends them back down. The server does the
    /// move (like a GM teleport); this only asks. Not real travel: the real Marshal service replaces it.
    /// </summary>
    public static class SkyLiftService
    {
        public const string Id = "skylift";
        // Markers sit in the scene; a Marshal standing above this height is the one up in the preview.
        private const float PreviewHeight = 100f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Register()
        {
            if (!SkyLiftLink.Enabled) return;   // switched off in this build (KN_SKY_LIFT_OFF): the Marshal shows no Sky Lift button
            NpcServices.Register(Id, NpcOffer.SkyLift, "SKY LIFT (CONCORD 040)", Ride);
        }

        private static void Ride()
        {
            if (SkyLiftLink.Requester == null) { Debug.LogWarning("[SkyLift] Not in a game."); return; }
            NpcInteractable opener = NpcServices.Opener;
            bool goDown = opener != null && opener.transform.position.y > PreviewHeight;
            SkyLiftLink.Requester(goDown);
        }
    }
}
