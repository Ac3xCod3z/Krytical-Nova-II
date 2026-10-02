using UnityEngine;

namespace KryticalNova.World.Portals
{
    public readonly struct ZoneTransferResult
    {
        public readonly bool Transferred;
        /// <summary>Optional notice for the player who used the portal.</summary>
        public readonly string Message;

        public ZoneTransferResult(bool transferred, string message)
        {
            Transferred = transferred;
            Message = message;
        }
    }

    /// <summary>
    /// Moves a player through a portal. Runs on the server (or offline). The default is
    /// <see cref="SealedPortalHandler"/> until the next zone exists.
    /// </summary>
    public interface IZoneTransferHandler
    {
        ZoneTransferResult TryTransfer(ZonePortal portal, GameObject player);
    }

    /// <summary>Placeholder handler: every destination is sealed and the player is told so.</summary>
    public sealed class SealedPortalHandler : IZoneTransferHandler
    {
        public ZoneTransferResult TryTransfer(ZonePortal portal, GameObject player)
        {
            // TODO(zone2): hand off to ZoneSessionManager (or a scene load) once the next map exists.
            return new ZoneTransferResult(false, portal.SealedMessage);
        }
    }
}
