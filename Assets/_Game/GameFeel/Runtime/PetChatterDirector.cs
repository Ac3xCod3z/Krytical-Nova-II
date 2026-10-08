using System.Collections.Generic;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;
using KryticalNova.Pets;
using KryticalNova.Progression;
using KryticalNova.UI;
using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// Has the local player's pet react to what its owner does: it watches for the moments (a death, a critical hit,
    /// a level gained, low HP, standing idle, a rare item picked up, and whatever is raised through
    /// <see cref="PetChatterMoments"/>), asks the pet's <see cref="PetChatterSet"/> for something to say that it did
    /// not say last time, and shows it in a bubble (<see cref="PetBubbleController"/>). How often, and what counts
    /// as low HP, idle and rare, is <see cref="PetChatterSettings"/>. Local only: nothing is sent, and only the
    /// owner sees it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PetChatterDirector : MonoBehaviour
    {
        [SerializeField] private PetChatterSettings settings;
        [Tooltip("Metres. A rare item on the ground this close to the player is watched: when it is picked up, the pet reacts.")]
        [SerializeField, Min(1f)] private float dropWatchDistance = 6f;
        [Tooltip("Metres. Moving less than this does not count as moving when the pet judges its owner idle.")]
        [SerializeField, Min(0.01f)] private float idleSlack = 0.3f;

        private const float DropConfirmSeconds = 1.5f; // how long after a watched item vanishes the Pack may take to show it

        public static PetChatterDirector Instance { get; private set; }

        private readonly PetChatterVoice voice = new PetChatterVoice();
        private readonly Dictionary<WorldDrop, (ushort item, int had)> watched = new Dictionary<WorldDrop, (ushort, int)>();
        private readonly List<WorldDrop> vanished = new List<WorldDrop>();
        private readonly List<(ushort item, int had, float until)> awaited = new List<(ushort, int, float)>();
        private PlayerInventory player;
        private PlayerCharacterProgression progression;
        private CombatEntity body;
        private PlayerPet playerPet;
        private PetFollower follower;
        private PetDefinition speaking;
        private PetChatterTrigger pending;
        private bool hasPending, lowArmed = true;
        private Vector3 idleAt;
        private float idleSince;

        private void OnEnable()
        {
            Instance = this;
            PetChatterMoments.Raised += Notify;
        }

        private void OnDisable()
        {
            PetChatterMoments.Raised -= Notify;
            Bind(null);
            if (Instance == this) Instance = null;
        }

        /// <summary>The local player has just had this moment. When several come in one frame the most important is kept.</summary>
        public void Notify(PetChatterTrigger trigger)
        {
            pending = hasPending ? PetChatterRules.MoreImportant(pending, trigger) : trigger;
            hasPending = true;
        }

        /// <summary>The local player did something: it is not standing idle.</summary>
        public void MarkActive() => idleSince = Time.unscaledTime;

        private void Update()
        {
            if (player != PlayerInventory.Local) Bind(PlayerInventory.Local);
            if (player == null || settings == null) { hasPending = false; return; }
            WatchHealth();
            WatchIdle();
            WatchDrops();
            if (!hasPending) return;
            hasPending = false;
            React(pending);
        }

        private void Bind(PlayerInventory local)
        {
            if (progression != null) progression.LevelGained -= OnLevelGained;
            player = local;
            progression = local != null ? local.GetComponent<PlayerCharacterProgression>() : null;
            body = local != null ? local.GetComponent<CombatEntity>() : null;
            playerPet = local != null ? local.GetComponent<PlayerPet>() : null;
            follower = local != null ? local.GetComponentInChildren<PetFollower>(true) : null;
            if (progression != null) progression.LevelGained += OnLevelGained;
            watched.Clear();
            awaited.Clear();
            hasPending = false;
            lowArmed = true;
            if (local != null) idleAt = local.transform.position;
            idleSince = Time.unscaledTime;
        }

        private void OnLevelGained(int levels) => Notify(PetChatterTrigger.LevelUp);

        // ---------------------------------------------------------------- the moments the director notices itself

        private void WatchHealth()
        {
            if (body == null || body.MaxHealth <= 0f) return;
            if (!body.IsAlive) return; // a death is its own moment; the warning is armed again once the HP is back up
            float share = body.CurrentHealth / body.MaxHealth;
            if (share >= settings.lowHealthRearmFraction) lowArmed = true;
            else if (lowArmed && share < settings.lowHealthFraction)
            {
                lowArmed = false;
                Notify(PetChatterTrigger.LowHealth);
            }
        }

        private void WatchIdle()
        {
            Vector3 at = player.transform.position;
            bool dead = body != null && !body.IsAlive;
            if (dead || (at - idleAt).sqrMagnitude > idleSlack * idleSlack)
            {
                idleAt = at;
                idleSince = Time.unscaledTime;
                return;
            }
            if (Time.unscaledTime - idleSince < settings.idleSeconds) return;
            idleSince = Time.unscaledTime; // and again after as long, if nothing happens
            Notify(PetChatterTrigger.LongIdle);
        }

        /// <summary>
        /// A rare item on the ground near the player is remembered with how many of it the Pack holds; when the item
        /// is gone from the ground and the Pack holds more, the player picked it up.
        /// </summary>
        private void WatchDrops()
        {
            float now = Time.unscaledTime;
            Vector3 at = player.transform.position;
            float reach = dropWatchDistance * dropWatchDistance;
            IReadOnlyList<WorldDrop> drops = WorldDrop.Active;
            for (int i = 0; i < drops.Count; i++)
            {
                WorldDrop drop = drops[i];
                if (drop == null || drop.Stack.IsEmpty || drop.Rarity < settings.rareDropFrom || watched.ContainsKey(drop)) continue;
                if ((drop.Position - at).sqrMagnitude > reach) continue;
                watched[drop] = (drop.Stack.itemId, player.CountItem(drop.Stack.itemId));
            }

            vanished.Clear();
            foreach (KeyValuePair<WorldDrop, (ushort item, int had)> each in watched)
            {
                WorldDrop drop = each.Key;
                bool gone = drop == null || !drop.IsSpawned || drop.Stack.IsEmpty;
                if (gone) awaited.Add((each.Value.item, each.Value.had, now + DropConfirmSeconds));
                if (gone || (drop.Position - at).sqrMagnitude > reach * 4f) vanished.Add(drop);
            }
            foreach (WorldDrop drop in vanished) watched.Remove(drop);

            for (int i = awaited.Count - 1; i >= 0; i--)
            {
                (ushort item, int had, float until) = awaited[i];
                if (player.CountItem(item) > had)
                {
                    awaited.RemoveAt(i);
                    Notify(PetChatterTrigger.RareDrop);
                }
                else if (now > until) awaited.RemoveAt(i);
            }
        }

        // ---------------------------------------------------------------- the reaction

        private void React(PetChatterTrigger trigger)
        {
            if (!HudPreferences.PetChatter) return;
            PetDefinition pet = playerPet != null ? playerPet.Pet : null;
            Transform petBody = follower != null ? follower.PetTransform : null;
            PetBubbleController bubbles = PetBubbleController.Instance;
            if (pet == null || pet.chatter == null || petBody == null || bubbles == null) return;
            if (pet != speaking)
            {
                // Another pet has come out: it does not remember what the last one said.
                voice.Forget();
                speaking = pet;
            }
            PetChatterSet set = pet.chatter;
            int variant = voice.Speak(trigger, set.Count(trigger), Time.unscaledTime, settings, Random.value);
            if (variant < 0) return;
            PetChatterSet.Moment moment = set.Find(trigger);
            Bounds? box = follower.TryGetBodyBox(out Bounds own) ? own : (Bounds?)null;
            if (set.voice == PetVoice.Text)
            {
                string line = moment.lines[variant];
                bubbles.Say(petBody, player.transform, line, null, settings.BubbleSeconds(line), box);
            }
            else bubbles.Say(petBody, player.transform, null, moment.emoji[variant], settings.BubbleSeconds(null), box);
        }
    }
}
