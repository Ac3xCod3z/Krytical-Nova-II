using System;

namespace KryticalNova.Core
{
    /// <summary>
    /// TEMPORARY (owner, 9 Nov 2026): the link between the Marshal's SKY LIFT button (UI) and the entity netcode's request to the
    /// server (Persistence.Entities fills <see cref="Requester"/>). Marker names are the scene objects the server moves the player to.
    /// </summary>
    public static class SkyLiftLink
    {
        public const string ArrivalMarker = "Concord040_Arrival";
        public const string ReturnMarker = "Concord040_Return";

        /// <summary>The define that switches the Sky Lift off in a build (cutover, 12 Nov 2026: Sky Concord is unfinished and unreachable). Nothing is deleted.</summary>
        public const string OffDefine = "KN_SKY_LIFT_OFF";

        /// <summary>False in a build made with <see cref="OffDefine"/>: no Sky Lift button at the Marshal, no client request, the server refuses a ride.</summary>
        public const bool Enabled =
#if KN_SKY_LIFT_OFF
            false;
#else
            true;
#endif

        /// <summary>Asks the server for the ride: true = down to town, false = up to Concord 040. Null while no game is joined.</summary>
        public static Action<bool> Requester;
    }
}
