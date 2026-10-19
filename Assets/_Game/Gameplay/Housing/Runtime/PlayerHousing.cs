using System;
using System.Collections;
using KryticalNova.Core;
using KryticalNova.Factions;
using KryticalNova.Networking;
using KryticalNova.Player;
using KryticalNova.Rendering;
using KryticalNova.World;
using Unity.AI.Navigation;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Housing
{
    /// <summary>
    /// A player's side of Crew Halls. For now that is the Hall sandbox: one Lot with a Hall on it, set down far
    /// from the starter town for the player to walk round, go into and furnish.
    /// <para>The inside of a Hall is an instance: it does not exist until someone goes through the door, it is
    /// made and warmed during the trip in (its three floors, their Finishes, every furnishing, the ground to walk
    /// on, and one unseen drawing of each so that nothing is loaded in front of the player), and it is gone again
    /// when they leave. It has three floors, one above the other: the one walked into from outside, the upstairs
    /// and a basement, joined by stairs that are stepped onto. Every trip here (to the Hall, through its door, back) is the same thing: the curtain closes, the
    /// place arrived at is made ready over as many frames as it needs, the curtain opens.</para>
    /// It is decided on the machine it is played on (offline or as host) and saved there; a player who has joined
    /// someone else's game is told it is not theirs to use yet. Shared Halls, and instances a server opens, come
    /// with the server work (`_Docs/Plans/crew-halls-plan.md`).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCrate))]
    public sealed class PlayerHousing : NetworkBehaviour
    {
        public const string ReasonHostOnly = "The Hall sandbox is the host's for now: shared Halls are not built yet.";
        public const string ReasonNoLot = "There is no Lot to go to (run Krytical Nova ▸ Housing ▸ Build Sandbox Lot).";
        public const string ReasonNoGround = "The way there could not be found.";
        public const string ReasonNotAtHall = "You are not at your Hall.";
        public const string ReasonNotInside = "You are not inside your Hall.";
        public const string ReasonOnTheWay = "You are already on your way.";
        private const float NavSampleRadius = 4f;
        private const float DoorRearm = 0.8f;   // metres beyond a door's reach a player goes before it takes them again
        private const int WarmPixels = 128;

        [SerializeField] private HousingSettings settings;
        [SerializeField] private FurnishingCatalog catalog;

        private PlayerCrate crate;
        private PlayerIdentity identity;
        private PointAndClickPlayer mover;
        private NavMeshAgent agent;
        private HousingLot lot;
        private readonly HallRoom[] rooms = new HallRoom[HomeAreas.Count];   // by floor; the lawns have none
        private readonly HomeView[] views = new HomeView[HomeAreas.Count];   // by area
        private GameObject facade;
        private LocalHomeAuthority authority;
        private HomeArea area;
        private bool doorArmed;
        private Coroutine trip;
        private Vector3 returnPosition;
        private Quaternion returnRotation;

        /// <summary>Raised on this machine when the player arrives at their Hall, leaves it, or goes in or out of its door.</summary>
        public event Action Changed;

        /// <summary>This machine's own player, or null.</summary>
        public static PlayerHousing Local =>
            LocalPlayer.Get<PlayerHousing>();

        public HousingSettings Settings => settings;
        public FurnishingCatalog Catalog => catalog;
        public PlayerCrate Crate => crate;
        public FactionId Faction => identity != null ? identity.Faction : FactionId.None;
        /// <summary>True while the player is at their Hall.</summary>
        public bool InHome => lot != null && !arriving;
        /// <summary>Which part of the Hall the player is in: on its lawns, or inside.</summary>
        public HomeArea Area => area;
        public bool Indoors => InHome && HomeAreas.IsInside(area);
        public HousingLot Lot => lot;
        /// <summary>The floor the player is on, while they are inside. Null on the lawns: the inside is an instance.</summary>
        public HallRoom Room => HomeAreas.IsInside(area) ? rooms[(int)area] : null;
        /// <summary>Whether the floor the player is on has a stair to another, and which.</summary>
        public bool HasStairTo(HomeArea floor)
        {
            HallRoom here = Room;
            if (here == null) return false;
            foreach (HallRoom.Stair stair in here.Stairs)
                if (stair.leadsTo == floor) return true;
            return false;
        }
        /// <summary>Who decides what stands in the Hall the player is at, or null.</summary>
        public IHomeAuthority Authority => InHome ? authority : null;
        public LocalHomeAuthority LocalAuthority => InHome ? authority : null;
        public HomeView LawnView => views[(int)HomeArea.Lawn];

        /// <summary>True from the curtain closing to its opening again.</summary>
        public bool Travelling => trip != null;
        /// <summary>How far the curtain is closed, 0 to 1. Whoever draws the HUD draws it.</summary>
        public float Curtain { get; private set; }
        /// <summary>What the curtain says.</summary>
        public string TravelLabel { get; private set; }

        private bool arriving; // the Lot exists but its player has not been set down on it yet

        /// <summary>What draws the pieces of one part of the Hall.</summary>
        public HomeView View(HomeArea which) => views[(int)which];

        /// <summary>What the pieces of one part of the Hall are measured from.</summary>
        public Transform PieceRoot(HomeArea which)
        {
            if (!HomeAreas.IsInside(which)) return lot != null ? lot.PieceRoot : null;
            HallRoom floor = rooms[(int)which];
            return floor != null ? floor.PieceRoot : null;
        }

        /// <summary>Where the player's feet are, in the world.</summary>
        public Vector3 Feet => transform.position - Vector3.up * (agent != null ? agent.baseOffset : 0f);

        private bool HasAuthority => !IsSpawned || IsServer;

        private void Awake()
        {
            crate = GetComponent<PlayerCrate>();
            identity = GetComponent<PlayerIdentity>();
            mover = GetComponent<PointAndClickPlayer>();
            agent = GetComponent<NavMeshAgent>();
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

        // ------------------------------------------------------------------ the four trips

        /// <summary>Go to the Hall sandbox. Returns why not, or null when the trip has begun.</summary>
        public string Enter()
        {
            if (!HasAuthority) return ReasonHostOnly;
            if (Travelling) return ReasonOnTheWay;
            if (lot != null) return null;
            if (settings == null || settings.lotPrefab == null) return ReasonNoLot;

            returnPosition = transform.position;
            returnRotation = transform.rotation;
            Begin("TO YOUR HALL", OpenLot, () =>
            {
                arriving = false;
                if (!Place(lot.Arrival.position, lot.Arrival.rotation))
                {
                    Close(duringTrip: true);
                    Tell(ReasonNoGround);
                    return;
                }
                SetArea(HomeArea.Lawn);
            });
            return null;
        }

        /// <summary>Back to where the player came from. Returns why not, or null when the trip has begun.</summary>
        public string Leave()
        {
            if (!HasAuthority) return ReasonHostOnly;
            if (Travelling) return ReasonOnTheWay;
            if (lot == null) return null;
            Begin("BACK TO TOWN", null, () =>
            {
                Vector3 position = returnPosition;
                Quaternion rotation = returnRotation;
                Close(duringTrip: true);
                Place(position, rotation, addOffset: false);
            });
            return null;
        }

        /// <summary>Through the Hall's door into the room. Returns why not, or null when the trip has begun.</summary>
        public string GoInside()
        {
            if (!HasAuthority) return ReasonHostOnly;
            if (!InHome) return ReasonNotAtHall;
            if (Travelling) return ReasonOnTheWay;
            if (HomeAreas.IsInside(area)) return null;
            Begin("INTO THE HALL", OpenRoom, () =>
            {
                HallRoom ground = rooms[(int)HomeArea.Interior];
                if (ground == null || !Place(ground.Entry.position, ground.transform.rotation))
                {
                    CloseRoom();
                    Tell(ReasonNoGround);
                    return;
                }
                SetArea(HomeArea.Interior);
            });
            return null;
        }

        /// <summary>
        /// Up or down a stair to another floor inside. Returns why not, or null when the trip has begun. The floors
        /// are all there already, so it is the shortest of trips: the curtain closes and opens.
        /// </summary>
        public string GoToFloor(HomeArea floor)
        {
            if (!HasAuthority) return ReasonHostOnly;
            if (!InHome || !HomeAreas.IsInside(area) || !HomeAreas.IsInside(floor)) return ReasonNotInside;
            if (Travelling) return ReasonOnTheWay;
            if (floor == area) return null;
            HomeArea from = area;
            HallRoom there = rooms[(int)floor];
            if (there == null) return ReasonNoGround;
            Begin(floor == HomeArea.Upper ? "UPSTAIRS" : floor == HomeArea.Basement ? "TO THE BASEMENT" : "TO THE GROUND FLOOR", null, () =>
            {
                // Off the stair that leads back where the player came from, or by the floor's own way in.
                bool linked = false;
                foreach (HallRoom.Stair stair in there.Stairs) linked |= stair.leadsTo == from;
                if (!Place(linked ? there.StairArrival(from) : there.Entry.position, there.transform.rotation)) return;
                SetArea(floor);
            }, least: 0f);
            return null;
        }

        /// <summary>Out of the room to the Hall's doorstep. Returns why not, or null when the trip has begun.</summary>
        public string GoOutside()
        {
            if (!HasAuthority) return ReasonHostOnly;
            if (!InHome) return ReasonNotAtHall;
            if (Travelling) return ReasonOnTheWay;
            if (area == HomeArea.Lawn) return null;
            Begin("OUT TO THE LAWN", null, () =>
            {
                // Facing the gate, the Hall behind. The room is closed behind the player: it was theirs alone.
                Place(lot.DoorStep(settings.doorStep), lot.transform.rotation * Quaternion.Euler(0f, 180f, 0f));
                SetArea(HomeArea.Lawn);
                CloseRoom();
            });
            return null;
        }

        /// <summary>Back to the door of the part of the Hall the player is in: the way out of a corner built shut.</summary>
        public string BackToDoor()
        {
            if (!HasAuthority) return ReasonHostOnly;
            if (!InHome) return ReasonNotAtHall;
            if (Travelling) return ReasonOnTheWay;
            HallRoom here = Room;
            bool placed = here != null
                ? Place(here.Entry.position, here.transform.rotation)
                : Place(lot.DoorStep(settings.doorStep), lot.transform.rotation * Quaternion.Euler(0f, 180f, 0f));
            doorArmed = false;
            return placed ? null : ReasonNoGround;
        }

        private void Begin(string label, Func<IEnumerator> makeReady, Action arrive, float least = -1f)
        {
            TravelLabel = label;
            trip = StartCoroutine(Trip(makeReady, arrive, least < 0f ? settings.travelSeconds : least));
            Changed?.Invoke();
        }

        // The curtain closes, the place arrived at is made ready over as many frames as it takes (and never in less
        // than the trip's own time), the player is set down, the curtain opens.
        private IEnumerator Trip(Func<IEnumerator> makeReady, Action arrive, float least)
        {
            if (mover != null)
            {
                mover.ServerStopMoving();
                mover.WorldClicksParked = true;
            }
            float fade = Mathf.Max(0.01f, settings.curtainSeconds);
            for (float t = 0f; t < fade; t += Time.unscaledDeltaTime)
            {
                Curtain = t / fade;
                yield return null;
            }
            Curtain = 1f;
            yield return null; // the curtain is drawn closed before anything heavy begins

            float began = Time.unscaledTime;
            if (makeReady != null) yield return makeReady();
            while (Time.unscaledTime - began < least) yield return null;

            arrive?.Invoke();
            yield return null; // the camera has caught up with the player, and walls it looks through are down
            yield return null;

            for (float t = 0f; t < fade; t += Time.unscaledDeltaTime)
            {
                Curtain = 1f - t / fade;
                yield return null;
            }
            Curtain = 0f;
            if (mover != null) mover.WorldClicksParked = false;
            trip = null;
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ making the places ready

        private IEnumerator OpenLot()
        {
            arriving = true;
            lot = Instantiate(settings.lotPrefab, settings.sandboxOrigin, Quaternion.identity);
            lot.name = "Hall Sandbox Lot";
            authority = new LocalHomeAuthority(identity != null ? identity.CharacterId : null, () => Faction, crate, catalog,
                settings, which => HomeAreas.IsInside(which) ? HallRoom.ShapeFor(settings, which) : lot.LawnShape);
            authority.LooksChanged += OnLookChanged;
            WearFacade();
            yield return null;
            // The ground is walked on the moment it exists: its NavMesh is made here, from the Lot's own colliders.
            BuildGround();

            HomeView lawnView = views[(int)HomeArea.Lawn] = lot.gameObject.AddComponent<HomeView>();
            lawnView.Bind(authority, HomeArea.Lawn, lot.PieceRoot, fill: false);
            yield return lawnView.FillOverFrames(settings.warmPiecesPerFrame);

            // Tall furnishings between the camera and the player thin out like any other prop.
            Camera view = mover != null && mover.GameplayCamera != null ? mover.GameplayCamera : Camera.main;
            if (view != null && view.TryGetComponent(out OccluderFader fader)) fader.AlsoFade(1 << HomeView.FurnishingLayer);

            yield return null;
            Warm(lot.transform.position, Mathf.Max(lot.Size.x, lot.Size.y));
        }

        // The instance: the three floors are made for this visit and for nobody else, one above the other.
        private IEnumerator OpenRoom()
        {
            CloseRoom();
            Vector3 ground = lot.transform.position + settings.roomOffset;
            foreach (HomeArea floor in HomeAreas.Inside)
            {
                HallRoom room = rooms[(int)floor] = HallRoom.Create(ground + HallRoom.FloorOffset(settings, floor), lot.transform.rotation, settings, Faction, floor);
                yield return null;
                foreach (HomeLook slot in new[] { HomeLook.Floor, HomeLook.Wall, HomeLook.Ceiling })
                {
                    room.Wear(slot, WornLook(slot));
                    yield return null;
                }

                HomeView view = views[(int)floor] = room.gameObject.AddComponent<HomeView>();
                view.Bind(authority, floor, room.PieceRoot, fill: false);
                yield return view.FillOverFrames(settings.warmPiecesPerFrame);

                // Its own ground, apart from the Lot's and from the other floors': made from its floor and walls.
                var surface = room.gameObject.AddComponent<NavMeshSurface>();
                if (lot.TryGetComponent(out NavMeshSurface outside))
                {
                    surface.agentTypeID = outside.agentTypeID;
                    surface.layerMask = outside.layerMask;
                }
                surface.collectObjects = CollectObjects.Children;
                surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
                Physics.SyncTransforms();
                surface.BuildNavMesh();
                yield return null;
            }
            // Each floor is drawn once unseen, alone, as it will be looked into.
            foreach (HomeArea floor in HomeAreas.Inside)
            {
                foreach (HomeArea other in HomeAreas.Inside) rooms[(int)other].SetShown(other == floor);
                Warm(rooms[(int)floor].transform.position, Mathf.Max(rooms[(int)floor].Size.x, rooms[(int)floor].Size.y));
                yield return null;
            }
        }

        private void CloseRoom()
        {
            foreach (HomeArea floor in HomeAreas.Inside)
            {
                views[(int)floor] = null;
                if (rooms[(int)floor] != null) Destroy(rooms[(int)floor].gameObject);
                rooms[(int)floor] = null;
            }
        }

        // One drawing nobody sees: every mesh and texture of the place goes to the graphics card and every shader
        // it uses is made ready now, behind the curtain, and not as a stutter when the curtain opens.
        private static void Warm(Vector3 middle, float span)
        {
            var holder = new GameObject("Hall Warm-up Camera");
            RenderTexture target = RenderTexture.GetTemporary(WarmPixels, WarmPixels, 16);
            try
            {
                var eye = holder.AddComponent<Camera>();
                eye.enabled = false;
                eye.targetTexture = target;
                eye.fieldOfView = 70f;
                eye.nearClipPlane = 0.3f;
                eye.farClipPlane = span * 6f;
                Vector3 from = middle + new Vector3(0f, span, -span);
                eye.transform.SetPositionAndRotation(from, Quaternion.LookRotation(middle - from, Vector3.up));
                eye.Render();
                eye.targetTexture = null;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(target);
                Destroy(holder);
            }
        }

        // Called from inside a trip (the trip back to town ends by closing the Hall), the trip itself goes on to
        // open its curtain; called from anywhere else, whatever trip was under way is over.
        private void Close(bool duringTrip = false)
        {
            bool was = lot != null;
            if (!duringTrip)
            {
                if (trip != null) StopCoroutine(trip);
                trip = null;
                Curtain = 0f;
            }
            arriving = false;
            if (authority != null) authority.LooksChanged -= OnLookChanged;
            authority = null;
            views[(int)HomeArea.Lawn] = null;
            facade = null;
            CloseRoom();
            if (lot != null) Destroy(lot.gameObject);
            lot = null;
            area = HomeArea.Lawn;
            if (was)
            {
                PrecipitationController.Indoors = false;
                if (mover != null && !duringTrip) mover.WorldClicksParked = false;
                Changed?.Invoke();
            }
        }

        private void SetArea(HomeArea next)
        {
            area = next;
            doorArmed = false;
            bool indoors = HomeAreas.IsInside(next);
            // The Lot stays where it is while its player is indoors, neither drawn nor lit; and of the floors
            // inside, only the one the player is on.
            if (lot.Outside != null)
                foreach (Renderer renderer in lot.Outside.GetComponentsInChildren<Renderer>(true)) renderer.forceRenderingOff = indoors;
            foreach (HomeArea which in HomeAreas.All)
            {
                if (views[(int)which] != null) views[(int)which].enabled = which == next;
                if (rooms[(int)which] != null) rooms[(int)which].SetShown(which == next);
            }
            PrecipitationController.Indoors = indoors;
            Changed?.Invoke();
        }

        private void Update()
        {
            if (!InHome || !HasAuthority || Travelling) return;
            Vector3 feet = Feet;
            HallRoom here = Room;
            // The door is on the lawns and on the ground floor; stairs are on every floor inside.
            float toDoor = float.MaxValue;
            if (here == null || area == HomeArea.Interior)
            {
                Vector3 door = here != null ? here.DoorPoint : lot.DoorPoint;
                toDoor = Vector2.Distance(new Vector2(feet.x, feet.z), new Vector2(door.x, door.z));
            }
            float toStair = here != null ? here.StairDistance(feet) : float.MaxValue;
            if (!doorArmed)
            {
                // Just come through: a door or a stair takes nobody back until they have stepped away from it.
                if (Mathf.Min(toDoor, toStair) > settings.doorRadius + DoorRearm) doorArmed = true;
                return;
            }
            if (toDoor <= settings.doorRadius)
            {
                if (here != null) GoOutside(); else GoInside();
            }
            else if (here != null && here.TryStairAt(feet, settings.doorRadius, out HomeArea leadsTo)) GoToFloor(leadsTo);
        }

        // ------------------------------------------------------------------ what the Hall wears

        private void OnLookChanged(HomeLook slot)
        {
            if (slot == HomeLook.Facade)
            {
                WearFacade();
                // Another building stands on other ground.
                BuildGround();
                if (HomeAreas.IsInside(area) && facade != null)
                    foreach (Renderer renderer in facade.GetComponentsInChildren<Renderer>(true)) renderer.forceRenderingOff = true;
            }
            else
            {
                // A Finish is the whole inside's: all three floors wear it.
                foreach (HomeArea floor in HomeAreas.Inside)
                    if (rooms[(int)floor] != null) rooms[(int)floor].Wear(slot, WornLook(slot));
                HallRoom here = Room;
                if (here != null)
                    foreach (HomeArea floor in HomeAreas.Inside)
                        if (rooms[(int)floor] != null) rooms[(int)floor].SetShown(floor == area);
            }
            Changed?.Invoke();
        }

        private HallLookDefinition WornLook(HomeLook slot) =>
            catalog != null && authority != null ? catalog.WornLook(Faction, slot, authority.Home.Look(slot)) : null;

        private void WearFacade()
        {
            if (facade != null)
            {
                // Gone at the end of the frame; switched off now, so the ground made next is not made round it.
                facade.SetActive(false);
                Destroy(facade);
            }
            HallLookDefinition look = WornLook(HomeLook.Facade);
            facade = look != null ? StandFacade(look, lot.FacadeAnchor) : null;
        }

        /// <summary>
        /// Stands a Facade on its pad: the building's floor on the ground and its middle on the pad's, moved by the
        /// look's own offset so that its door is at the Lot's. It keeps the vendor's colliders, or is given a box.
        /// </summary>
        public static GameObject StandFacade(HallLookDefinition look, Transform anchor)
        {
            if (look == null || look.prefab == null) return null;
            var holder = new GameObject("Hall Facade");
            holder.transform.SetParent(anchor, false);
            GameObject model = Instantiate(look.prefab, holder.transform, false);
            model.transform.localRotation = Quaternion.Euler(look.modelEuler);
            model.transform.localScale = look.prefab.transform.localScale * Mathf.Max(0.01f, look.modelScale);
            model.transform.localPosition = new Vector3(look.padOffset.x - look.boundsCenter.x,
                -(look.boundsCenter.y - look.boundsSize.y * 0.5f), look.padOffset.y - look.boundsCenter.z);
            if (model.GetComponentInChildren<Collider>() == null)
            {
                var box = holder.AddComponent<BoxCollider>();
                box.center = new Vector3(look.padOffset.x, look.boundsSize.y * 0.5f, look.padOffset.y);
                box.size = look.boundsSize;
            }
            return holder;
        }

        private void BuildGround()
        {
            if (lot == null || !lot.TryGetComponent(out NavMeshSurface surface)) return;
            Physics.SyncTransforms();
            surface.BuildNavMesh();
        }

        private bool Place(Vector3 position, Quaternion rotation, bool addOffset = true)
        {
            if (addOffset)
            {
                if (!NavMesh.SamplePosition(position, out NavMeshHit hit, NavSampleRadius, NavMesh.AllAreas)) return false;
                position = hit.position;
                if (agent != null) position += Vector3.up * agent.baseOffset;
            }
            if (mover != null) mover.ServerStopMoving();
            NetworkPlacement.Place(transform, position, rotation);
            return true;
        }

        private static void Tell(string text) => ClientPresentationRegistry.Messages?.ShowMessage(text);
    }
}
