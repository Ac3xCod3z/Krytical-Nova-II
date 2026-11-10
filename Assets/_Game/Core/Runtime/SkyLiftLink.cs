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

        /// <summary>Asks the server for the ride: true = down to town, false = up to Concord 040. Null while no game is joined.</summary>
        public static Action<bool> Requester;
    }
}
