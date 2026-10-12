using System;
using KryticalNova.Core;
using KryticalNova.Factions;
using KryticalNova.Networking;
using KryticalNova.Player;
using Unity.AI.Navigation;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Housing
{
    /// <summary>
    /// A player's side of Crew Halls. For now that is the Hall sandbox: one Lot with a Hall on it, set down far
    /// from the starter town for the player to walk round and furnish. It is decided on the machine it is played
    /// on (offline or as host) and saved there; a player who has joined someone else's game is told it is not
    /// theirs to use yet. Shared Halls come with the server work (`_Docs/Plans/crew-halls-plan.md`).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCrate))]
    public sealed class PlayerHousing : NetworkBehaviour
    {
        public const string ReasonHostOnly = "The Hall sandbox is the host's for now: shared Halls are not built yet.";
        public const string ReasonNoLot = "There is no Lot to go to (run Krytical Nova ▸ Housing ▸ Build Sandbox Lot).";
        public const string ReasonNoGround = "The Lot could not be reached.";
        private const float NavSampleRadius = 4f;

        [SerializeField] private HousingSettings settings;
        [SerializeField] private FurnishingCatalog catalog;

        private PlayerCrate crate;
        private PlayerIdentity identity;
        private PointAndClickPlayer mover;
        private HousingLot lot;
        private HomeView lawnView;
        private LocalHomeAuthority authority;
        private Vector3 returnPosition;
        private Quaternion returnRotation;

        /// <summary>Raised on this machine when the player arrives at their Hall or leaves it.</summary>
        public event Action Changed;

        /// <summary>This machine's own player, or null.</summary>
        public static PlayerHousing Local =>
            KryticalNova.Items.PlayerInventory.Local != null ? KryticalNova.Items.PlayerInventory.Local.GetComponent<PlayerHousing>() : null;

        public HousingSettings Settings => settings;
        public FurnishingCatalog Catalog => catalog;
        public PlayerCrate Crate => crate;
        public FactionId Faction => identity != null ? identity.Faction : FactionId.None;
        /// <summary>True while the player is at their Hall.</summary>
        public bool InHome => lot != null;
        public HousingLot Lot => lot;
        /// <summary>Who decides what stands in the Hall the player is at, or null.</summary>
        public IHomeAuthority Authority => authority;
        public LocalHomeAuthority LocalAuthority => authority;
        public HomeView LawnView => lawnView;

        private bool HasAuthority => !IsSpawned || IsServer;

        private void Awake()
        {
            crate = GetComponent<PlayerCrate>();
            identity = GetComponent<PlayerIdentity>();
            mover = GetComponent<PointAndClickPlayer>();
        }

        public override void OnNetworkDespawn()
        {
            Close();
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            Close();
            base.OnDestroy();
        }

        /// <summary>Go to the Hall sandbox. Returns why not, or null.</summary>
        public string Enter()
        {
            if (!HasAuthority) return ReasonHostOnly;
            if (InHome) return null;
            if (settings == null || settings.lotPrefab == null) return ReasonNoLot;

            returnPosition = transform.position;
            returnRotation = transform.rotation;

            lot = Instantiate(settings.lotPrefab, settings.sandboxOrigin, Quaternion.identity);
            lot.name = "Hall Sandbox Lot";
            StandFacade();
            // The ground is walked on the moment it exists: its NavMesh is made here, from the Lot's own colliders.
            if (lot.TryGetComponent(out NavMeshSurface surface)) surface.BuildNavMesh();

            if (!Place(lot.Arrival.position, lot.Arrival.rotation))
            {
                Close();
                return ReasonNoGround;
            }

            authority = new LocalHomeAuthority(identity != null ? identity.CharacterId : null, () => Faction, crate, catalog,
                settings, lot.LawnShape, default);
            lawnView = lot.gameObject.AddComponent<HomeView>();
            lawnView.Bind(authority, HomeArea.Lawn, lot.PieceRoot);
            Changed?.Invoke();
            return null;
        }

        /// <summary>Back to where the player came from.</summary>
        public string Leave()
        {
            if (!HasAuthority) return ReasonHostOnly;
            if (!InHome) return null;
            Vector3 position = returnPosition;
            Quaternion rotation = returnRotation;
            Close();
            Place(position, rotation, addOffset: false);
            return null;
        }

        private void Close()
        {
            bool was = lot != null;
            authority = null;
            lawnView = null;
            if (lot != null) Destroy(lot.gameObject);
            lot = null;
            if (was) Changed?.Invoke();
        }

        // The Hall of the player's faction, stood on the pad: its floor on the ground, its middle on the pad's,
        // and made smaller only when it would not fit. Facades of one fixed footprint come with the Facade baker.
        private void StandFacade()
        {
            GameObject prefab = settings.FacadeFor(Faction);
            if (prefab == null) return;
            Transform anchor = lot.FacadeAnchor;
            GameObject facade = Instantiate(prefab, anchor, false);
            facade.name = "Hall Facade";
            facade.transform.localPosition = Vector3.zero;
            facade.transform.localRotation = Quaternion.identity;

            Renderer[] renderers = facade.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            Bounds bounds = renderers[0].bounds;
            for (int n = 1; n < renderers.Length; n++) bounds.Encapsulate(renderers[n].bounds);
            float fit = Mathf.Min(1f, lot.PadSize.x / Mathf.Max(0.01f, bounds.size.x), lot.PadSize.y / Mathf.Max(0.01f, bounds.size.z));
            facade.transform.localScale *= fit;
            Vector3 middle = anchor.position + (bounds.center - anchor.position) * fit;
            float floor = anchor.position.y + (bounds.min.y - anchor.position.y) * fit;
            facade.transform.position += new Vector3(anchor.position.x - middle.x, anchor.position.y - floor, anchor.position.z - middle.z);
        }

        private bool Place(Vector3 position, Quaternion rotation, bool addOffset = true)
        {
            if (addOffset)
            {
                if (!NavMesh.SamplePosition(position, out NavMeshHit hit, NavSampleRadius, NavMesh.AllAreas)) return false;
                position = hit.position;
                if (TryGetComponent(out NavMeshAgent agent)) position += Vector3.up * agent.baseOffset;
            }
            if (mover != null) mover.ServerStopMoving();
            NetworkPlacement.Place(transform, position, rotation);
            return true;
        }
    }
}
