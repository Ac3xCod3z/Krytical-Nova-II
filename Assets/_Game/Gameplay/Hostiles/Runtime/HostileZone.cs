using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Economy;
using KryticalNova.Items;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Hostiles
{
    /// <summary>
    /// One hunting ground, where the game is decided (server, host or offline): it keeps its one species at full
    /// count, brings a dead Hostile back after its respawn time at a random spot away from players, puts its
    /// Hostiles to sleep while no player is near, and calls back any that is pulled too far. A slot's respawn
    /// time runs from the death, asleep or awake, so a zone wakes with whatever came due meanwhile.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HostileZone : MonoBehaviour
    {
        private const int SpawnAttempts = 8;
        private const float BruiserEdgeShare = 0.85f;

        [SerializeField] private HostileZoneDefinition definition;
        [SerializeField] private HostileSettings settings;
        [Tooltip("The prefabs of the zone's species and of its Bruiser, written by the zone builder.")]
        [SerializeField] private HostileNet hostilePrefab;
        [SerializeField] private HostileNet bruiserPrefab;

        private HostileNet[] slots;          // the last slot is the Bruiser's, when the zone has one
        private readonly RespawnQueue due = new RespawnQueue();
        private readonly Dictionary<HostileNet, int> slotOf = new Dictionary<HostileNet, int>();
        private float nextTick;
        private bool awake;
        private bool started;
        private bool wasListening;
        private bool? forcedAwake;
        private float respawnOverride = -1f;
        private bool localInside;
        private float nextNotice;
        private System.Action<HostileNet> onKilled; // one delegate for every Hostile, so a respawn allocates nothing

        /// <summary>Every Hostile Zone of the loaded map.</summary>
        public static IReadOnlyList<HostileZone> All => all;
        private static readonly List<HostileZone> all = new List<HostileZone>();

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => all.Clear();

        /// <summary>The Hostile Zone a point of the map lies in, or null. On every peer.</summary>
        public static HostileZone At(Vector3 point)
        {
            for (int i = 0; i < all.Count; i++)
            {
                HostileZone zone = all[i];
                if (zone.definition == null) continue;
                Vector3 offset = point - zone.transform.position;
                offset.y = 0f;
                if (offset.sqrMagnitude <= zone.definition.radius * zone.definition.radius) return zone;
            }
            return null;
        }

        public HostileZoneDefinition Definition => definition;
        public bool IsAwake => awake;
        public Vector3 Centre => transform.position;
        public float Radius => definition != null ? definition.radius : 0f;
        public int Capacity => slots != null ? slots.Length : 0;
        public bool HasBruiser => bruiserPrefab != null;

        public int Alive
        {
            get
            {
                int count = 0;
                if (slots != null)
                    foreach (HostileNet hostile in slots)
                        if (hostile != null && hostile.Entity.IsAlive) count++;
                return count;
            }
        }

        /// <summary>Seconds until the next slot comes due, or -1 when nothing is waiting.</summary>
        public float SecondsToNextRespawn => due.Count > 0 ? Mathf.Max(0f, due.PeekTime - Time.time) : -1f;

        private void OnEnable()
        {
            all.Add(this);
            onKilled ??= OnKilled;
        }

        private void OnDisable()
        {
            all.Remove(this);
            Clear();
            started = false;
        }

        private void Update()
        {
            if (definition == null || hostilePrefab == null || settings == null) return;
            NoticeLocalPlayer();
            if (!HostileSpawner.HasAuthority) { if (started) Forget(); return; }

            // A session that starts or ends changes what a Hostile is (on the network or not): begin again.
            NetworkManager manager = NetworkManager.Singleton;
            bool listening = manager != null && manager.IsListening;
            if (started && listening != wasListening) Clear();
            wasListening = listening;

            if (!started) Begin();
            if (Time.time < nextTick) return;
            nextTick = Time.time + settings.zoneTickSeconds;
            Tick();
        }

        private void Begin()
        {
            started = true;
            int count = definition.maxAlive + (bruiserPrefab != null ? 1 : 0);
            slots = new HostileNet[count];
            due.Clear();
            slotOf.Clear();
            for (int i = 0; i < count; i++) due.Push(Time.time, i);
            HostilePool.Prewarm(hostilePrefab, definition.maxAlive);
            if (bruiserPrefab != null) HostilePool.Prewarm(bruiserPrefab, 1);
        }

        private void Tick()
        {
            bool nowAwake = forcedAwake ?? AnyPlayerWithin(Radius + definition.wakeMargin);
            if (nowAwake != awake)
            {
                awake = nowAwake;
                foreach (HostileNet hostile in slots)
                    if (hostile != null) hostile.SetAsleep(!awake);
            }
            if (!awake) return;

            int budget = settings.spawnsPerTick;
            while (budget > 0 && due.Count > 0 && due.PeekTime <= Time.time)
            {
                int slot = due.Pop();
                if (!Fill(slot)) { due.Push(Time.time + 1f, slot); break; } // no room or no ground: try again shortly
                budget--;
            }
            CallBackStrays();
        }

        /// <summary>On every peer: the player of this machine is told what hunts here as they walk in.</summary>
        private void NoticeLocalPlayer()
        {
            if (Time.time < nextNotice) return;
            nextNotice = Time.time + settings.zoneTickSeconds;
            PlayerInventory local = PlayerInventory.Local;
            bool inside = local != null && At(local.transform.position) == this;
            if (inside == localInside) return;
            localInside = inside;
            if (!inside || definition.hostile == null) return;
            string behaviour = definition.hostile.behaviour == HostileBehaviour.AttacksOnSight ? "attacks on sight" : "fights back only";
            ClientPresentationRegistry.Messages?.ShowMessage($"{definition.displayName}  ·  Lv {definition.hostile.level} {definition.hostile.displayName}  ·  {behaviour}");
        }

        private bool IsBruiserSlot(int slot) => bruiserPrefab != null && slot == slots.Length - 1;

        private bool Fill(int slot)
        {
            if (HostileNet.Active.Count >= settings.maxActiveHostiles) return false;
            bool bruiser = IsBruiserSlot(slot);
            if (!TryFindSpot(bruiser, out Vector3 spot)) return false;
            HostileNet hostile = HostileSpawner.Spawn(bruiser ? bruiserPrefab : hostilePrefab, spot, Random.Range(0f, 360f));
            if (hostile == null) return false;
            hostile.Zone = this;
            slots[slot] = hostile;
            slotOf[hostile] = slot;
            hostile.Killed += onKilled;
            return true;
        }

        private void OnKilled(HostileNet hostile)
        {
            hostile.Killed -= onKilled;
            if (slots == null || !slotOf.TryGetValue(hostile, out int slot) || slots[slot] != hostile) return;
            slotOf.Remove(hostile);
            slots[slot] = null;
            hostile.Zone = null;
            due.Push(Time.time + RespawnSeconds(hostile, IsBruiserSlot(slot)), slot);
        }

        private float RespawnSeconds(HostileNet hostile, bool bruiser)
        {
            if (respawnOverride >= 0f) return respawnOverride;
            HostileStats stats = hostile.Stats;
            return bruiser ? Random.Range(stats.RespawnSeconds, stats.RespawnSecondsMax) : stats.RespawnSeconds;
        }

        /// <summary>A random spot in the zone, on the NavMesh, away from every player. A Bruiser stands near the edge.</summary>
        private bool TryFindSpot(bool bruiser, out Vector3 spot)
        {
            for (int attempt = 0; attempt < SpawnAttempts; attempt++)
            {
                Vector2 offset = bruiser
                    ? Random.insideUnitCircle.normalized * (Radius * BruiserEdgeShare)
                    : Random.insideUnitCircle * Radius;
                Vector3 point = Centre + new Vector3(offset.x, 0f, offset.y);
                if (!NavMesh.SamplePosition(point, out NavMeshHit hit, 6f, NavMesh.AllAreas)) continue;
                if (Flat(hit.position - Centre).sqrMagnitude > Radius * Radius) continue;
                if (PlayerWithin(hit.position, settings.spawnClearance)) continue;
                spot = hit.position;
                return true;
            }
            spot = default;
            return false;
        }

        /// <summary>
        /// A Hostile pulled past the leash gives up: it drops its target, is healed and walks home. So does one
        /// whose target has stepped onto neutral ground: no Hostile follows a player into town.
        /// </summary>
        private void CallBackStrays()
        {
            float limit = Radius + settings.leashMetres;
            foreach (HostileNet hostile in slots)
            {
                if (hostile == null || !hostile.Entity.IsAlive || hostile.Brain == null) continue;
                Transform chased = hostile.Brain.Target;
                if (chased == null) continue;
                if (Flat(hostile.transform.position - Centre).sqrMagnitude <= limit * limit && !TradeZone.CrossFactionAt(chased.position)) continue;
                hostile.Brain.Calm();
                hostile.Entity.ResetHealth();
                hostile.ReleaseTag();
            }
        }

        // ------------------------------------------------------------------ players

        private static readonly List<Transform> players = new List<Transform>();

        private static List<Transform> Players()
        {
            players.Clear();
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.IsListening)
            {
                foreach (NetworkClient client in manager.ConnectedClientsList)
                    if (client.PlayerObject != null) players.Add(client.PlayerObject.transform);
            }
            else if (PlayerInventory.Local != null) players.Add(PlayerInventory.Local.transform);
            return players;
        }

        private bool AnyPlayerWithin(float metres) => PlayerWithin(Centre, metres);

        private static bool PlayerWithin(Vector3 point, float metres)
        {
            foreach (Transform player in Players())
                if (Flat(player.position - point).sqrMagnitude <= metres * metres) return true;
            return false;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        // ------------------------------------------------------------------ tools (server or offline)

        /// <summary>Takes every Hostile of the zone out of the world. The zone begins again on its next update.</summary>
        public void Clear()
        {
            if (slots != null && HostileSpawner.HasAuthority)
                foreach (HostileNet hostile in slots)
                    if (hostile != null) { hostile.Killed -= onKilled; hostile.Zone = null; HostileSpawner.Remove(hostile); }
            Forget();
        }

        private void Forget()
        {
            slots = null;
            slotOf.Clear();
            due.Clear();
            started = false;
            awake = false;
        }

        /// <summary>Brings every empty slot back now, awake or not.</summary>
        public int FillNow()
        {
            if (!started || !HostileSpawner.HasAuthority) return 0;
            int filled = 0;
            var waiting = new List<int>();
            while (due.Count > 0) waiting.Add(due.Pop());
            foreach (int slot in waiting)
            {
                if (Fill(slot)) { filled++; if (!awake) slots[slot].SetAsleep(true); }
                else due.Push(Time.time + 1f, slot);
            }
            return filled;
        }

        /// <summary>Empties the zone and keeps it empty until <see cref="FillNow"/> or its timers bring it back.</summary>
        public void Empty(float seconds)
        {
            if (!started || !HostileSpawner.HasAuthority) return;
            for (int slot = 0; slot < slots.Length; slot++)
            {
                HostileNet hostile = slots[slot];
                if (hostile == null) continue;
                hostile.Killed -= onKilled;
                slotOf.Remove(hostile);
                slots[slot] = null;
                hostile.Zone = null;
                HostileSpawner.Remove(hostile);
                due.Push(Time.time + seconds, slot);
            }
        }

        /// <summary>Forces the zone awake (true), asleep (false), or lets players decide again (null).</summary>
        public void ForceAwake(bool? value)
        {
            forcedAwake = value;
            nextTick = 0f;
        }

        public bool? ForcedAwake => forcedAwake;

        /// <summary>Every respawn of this zone takes this long instead of the species' own time. Negative = the species' own.</summary>
        public void OverrideRespawn(float seconds) => respawnOverride = seconds;

        /// <summary>True while the zone's Bruiser is in the world.</summary>
        public bool BruiserAlive => bruiserPrefab != null && slots != null && slots[slots.Length - 1] != null;

        /// <summary>Seconds until the Bruiser is due (0 = as soon as the zone is awake), or -1 when it is alive or the zone has none.</summary>
        public float SecondsToBruiser
        {
            get
            {
                if (bruiserPrefab == null || slots == null || BruiserAlive) return -1f;
                return due.TryGetTime(slots.Length - 1, out float time) ? Mathf.Max(0f, time - Time.time) : -1f;
            }
        }

        /// <summary>Brings the Bruiser now if it is waiting.</summary>
        public bool SpawnBruiserNow()
        {
            if (bruiserPrefab == null || slots == null || !HostileSpawner.HasAuthority) return false;
            int slot = slots.Length - 1;
            if (slots[slot] != null || !due.Remove(slot)) return false;
            if (Fill(slot)) return true;
            due.Push(Time.time + 1f, slot);
            return false;
        }

        private void OnDrawGizmos()
        {
            if (definition == null) return;
            Gizmos.color = new Color(1f, 0.36f, 0.32f, 0.9f); // the zone's edge
            DrawRing(definition.radius);
            if (settings == null) return;
            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.6f); // the leash
            DrawRing(definition.radius + settings.leashMetres);
        }

        private void DrawRing(float radius)
        {
            const int Steps = 48;
            Vector3 previous = transform.position + new Vector3(radius, 0.3f, 0f);
            for (int i = 1; i <= Steps; i++)
            {
                float a = i * Mathf.PI * 2f / Steps;
                Vector3 next = transform.position + new Vector3(Mathf.Cos(a) * radius, 0.3f, Mathf.Sin(a) * radius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
    }
}
