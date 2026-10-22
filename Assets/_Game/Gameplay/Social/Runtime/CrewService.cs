using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.NPC;
using KryticalNova.Persistence;
using Unity.Collections;
using UnityEngine;
using KryticalNova.Wallet;

namespace KryticalNova.Social
{
    /// <summary>
    /// Every Crew on the server. Crews are a world document (<see cref="ServerSave"/>), so they outlive sessions and
    /// keep their members while those are offline. Runs only where the game is decided; players reach it through
    /// <see cref="PlayerCrew"/>. A member's Crew drives their Crew chat channel and the tag everyone sees.
    /// </summary>
    public static class CrewService
    {
        public const string WorldName = "crews";
        // A client sees itself a little ahead of where the server has it.
        private const float RegistryPadding = 2.5f;

        private static readonly Dictionary<string, PlayerCrew> OnlinePlayers = new Dictionary<string, PlayerCrew>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Crew> ByMember = new Dictionary<string, Crew>(StringComparer.Ordinal);
        private static CrewsWorld indexed;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnlinePlayers.Clear();
            ByMember.Clear();
            indexed = null;
        }

        private static CrewsWorld World
        {
            get
            {
                CrewsWorld world = ServerSave.World<CrewsWorld>(WorldName);
                if (ReferenceEquals(world, indexed)) return world;
                indexed = world;
                ByMember.Clear();
                foreach (Crew crew in world.crews)
                    foreach (CrewMember member in crew.members)
                        if (!string.IsNullOrEmpty(member.characterId)) ByMember[member.characterId] = crew;
                return world;
            }
        }

        private static long Now => DateTime.UtcNow.Ticks;
        private static void Save() => ServerSave.SaveWorld(WorldName);

        /// <summary>The Crew a character is in, or null.</summary>
        public static Crew Of(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return null;
            _ = World;
            return ByMember.TryGetValue(characterId, out Crew crew) ? crew : null;
        }

        public static Crew FindByName(string name)
        {
            foreach (Crew crew in World.crews)
                if (string.Equals(crew.name, name, StringComparison.OrdinalIgnoreCase)) return crew;
            return null;
        }

        public static IReadOnlyList<Crew> All => World.crews;

        private static PlayerCrew Online(string characterId) =>
            !string.IsNullOrEmpty(characterId) && OnlinePlayers.TryGetValue(characterId, out PlayerCrew player) ? player : null;

        private static void Describe(CrewMember member, PlayerCrew player)
        {
            member.characterId = player.CharacterId;
            member.name = player.MemberName;
            member.level = player.Level;
            member.weaponClass = player.WeaponClass;
            member.lastSeenUtc = Now;
        }

        private static int Cap(Crew crew, CrewSettings settings) =>
            CrewRules.MemberCap(settings != null ? settings.memberCapByLevel : null, crew.level);

        // ------------------------------------------------------------------ coming and going

        public static void ServerArrived(PlayerCrew player)
        {
            string id = player.CharacterId;
            if (string.IsNullOrEmpty(id)) return;
            OnlinePlayers[id] = player;
            Crew crew = Of(id);
            if (crew == null) { player.ServerShow(null, null); return; }
            CrewMember me = CrewRules.Find(crew, id);
            Describe(me, player);
            Save();
            Publish(crew);
            foreach (CrewMember member in crew.members)
            {
                PlayerCrew other = Online(member.characterId);
                if (other != null && other != player) other.Tell($"{me.name} of the Crew is online.");
            }
            if (!string.IsNullOrEmpty(crew.announcement)) player.Tell($"Crew announcement: {crew.announcement}");
        }

        public static void ServerLeft(PlayerCrew player)
        {
            string id = player.CharacterId;
            if (string.IsNullOrEmpty(id) || !OnlinePlayers.TryGetValue(id, out PlayerCrew known) || known != player) return;
            OnlinePlayers.Remove(id);
            Crew crew = Of(id);
            if (crew == null) return;
            Describe(CrewRules.Find(crew, id), player);
            Save();
            Publish(crew);
        }

        // ------------------------------------------------------------------ founding

        public static void ServerFound(PlayerCrew player, string name, int symbol, int colour = 0, bool gm = false, int tagColour = 0, int tagBackground = 0)
        {
            CrewSettings settings = player.Settings;
            string id = player.CharacterId;
            if (string.IsNullOrEmpty(id)) { player.Notify("Only a saved character can found a Crew."); return; }
            if (settings == null) { player.Notify("Crews are not set up on this server."); return; }
            if (!gm && !NpcInteractable.IsOffered(NpcOffer.CrewRegistry, player.transform.position, RegistryPadding))
            { player.Notify("Crews are registered with a Vaultkeeper."); return; }

            IWallet wallet = player.Wallet;
            long cost = gm ? 0 : settings.foundingCost;
            string blocked = CrewRules.FoundBlockedReason(Of(id) != null, gm ? int.MaxValue : player.Level, settings.founderLevel, wallet != null ? wallet.Creds : 0, cost);
            if (blocked != null) { player.Notify(blocked); return; }
            var taken = new List<string>(World.crews.Count);
            foreach (Crew other in World.crews) taken.Add(other.name);
            blocked = CrewRules.NameBlockedReason(name, settings.nameMinLength, settings.nameMaxLength, taken, out string clean);
            if (blocked != null) { player.Notify(blocked); return; }
            if (cost > 0 && (wallet == null || !wallet.ServerSpend(cost, CredsSink.CrewFounding))) { player.Notify($"Founding a Crew costs {WalletRules.FormatCreds(cost)}."); return; }

            var founder = new CrewMember();
            Describe(founder, player);
            Crew crew = CrewRules.Found(Guid.NewGuid().ToString("N"), clean, (int)player.Faction, Mathf.Clamp(symbol, 0, Mathf.Max(0, settings.symbolCount - 1)), founder, Now);
            crew.symbolColour = ColourIndex(settings, colour);
            // The tag's own colours (owner, 22 Oct 2026); a choice there is no colour for is the plain look (0).
            crew.tagColour = settings.HasTagInk(tagColour) ? tagColour : 0;
            crew.tagBackground = settings.tagBackgrounds != null && tagBackground >= 1 && tagBackground <= settings.tagBackgrounds.Length ? tagBackground : 0;
            World.crews.Add(crew);
            ByMember[id] = crew;
            // The Creds paid and the new Crew: one change (online phase 4, milestone 3). The Creds are in the wallet's tally.
            LedgerNote.Keep(player, LedgerKinds.CrewFound, "founded " + crew.name);
            player.ServerSaveWith(WorldName);
            Publish(crew);
            player.Tell(cost > 0
                ? $"You founded the Crew {crew.name} for {WalletRules.FormatCreds(cost)}. Invite players from the player menu; the Crew window is [G]."
                : $"You founded the Crew {crew.name}. Invite players from the player menu; the Crew window is [G].");
        }

        // ------------------------------------------------------------------ joining

        public static void ServerInvite(PlayerCrew from, PlayerCrew target)
        {
            if (target == null || target == from) { from.Notify("Invite another player."); return; }
            Crew crew = Of(from.CharacterId);
            if (crew == null) { from.Notify(CrewRules.ReasonNotInCrew); return; }
            if (string.IsNullOrEmpty(target.CharacterId)) { from.Notify($"{target.MemberName} has no saved character to join with."); return; }
            CrewMember me = CrewRules.Find(crew, from.CharacterId);
            string blocked = CrewRules.InviteBlockedReason(crew, me.Rank, Cap(crew, from.Settings), (int)target.Faction, Of(target.CharacterId) != null, target.MemberName)
                             ?? SocialGate.Refusal(from, target);
            if (blocked != null) { from.Notify(blocked); return; }
            if (!target.ServerOfferInvite(crew, from.MemberName)) { from.Notify($"{target.MemberName} is answering another Crew invitation."); return; }
            from.Tell($"You invited {target.MemberName} to the Crew.");
            target.Tell($"{from.MemberName} invites you to the Crew {crew.name}. Accept with /caccept, refuse with /cdecline.");
        }

        public static void ServerInviteByName(PlayerCrew from, string name)
        {
            name = ChatRouting.Clean(name);
            PlayerCrew found = null;
            int matches = 0;
            foreach (PlayerCrew player in OnlinePlayers.Values)
            {
                if (player == null || player == from || !string.Equals(player.MemberName, name, StringComparison.OrdinalIgnoreCase)) continue;
                found = player;
                matches++;
            }
            if (matches == 0) from.Notify($"No player named '{name}' is online.");
            else if (matches > 1) from.Notify("Several players share that name. Use the player menu on the one you mean (Shift+click them).");
            else ServerInvite(from, found);
        }

        /// <summary>The invited player answered. <paramref name="crewId"/> is the Crew they were invited to.</summary>
        public static void ServerAnswerInvite(PlayerCrew player, string crewId, string inviterName, bool accept)
        {
            Crew crew = null;
            foreach (Crew candidate in World.crews)
                if (candidate.id == crewId) { crew = candidate; break; }
            if (crew == null) { player.Notify("That Crew no longer exists."); return; }
            if (!accept)
            {
                player.Tell($"You declined the invitation to {crew.name}.");
                TellCrew(crew, $"{player.MemberName} declined the Crew invitation.", officersOnly: true);
                return;
            }
            if (Of(player.CharacterId) != null) { player.Notify(CrewRules.ReasonAlreadyInCrew); return; }
            if ((int)player.Faction != crew.faction) { player.Notify("That Crew is of another faction: a Crew only takes members of its own."); return; }
            if (crew.members.Count >= Cap(crew, player.Settings)) { player.Notify($"{crew.name} is full."); return; }

            var member = new CrewMember();
            Describe(member, player);
            CrewRules.Join(crew, member, Now);
            ByMember[member.characterId] = crew;
            Save();
            TellCrew(crew, $"{member.name} joined the Crew.");
            Publish(crew);
            if (!string.IsNullOrEmpty(crew.announcement)) player.Tell($"Crew announcement: {crew.announcement}");
        }

        // ------------------------------------------------------------------ leaving

        public static void ServerLeave(PlayerCrew player)
        {
            if (!TryMine(player, out Crew crew, out CrewMember me)) return;
            string blocked = CrewRules.LeaveBlockedReason(me.Rank, crew.members.Count);
            if (blocked != null) { player.Notify(blocked); return; }
            Detach(crew, me.characterId);
            player.Tell($"You left the Crew {crew.name}.");
            if (crew.members.Count == 0) Close(crew);
            else TellCrew(crew, $"{me.name} left the Crew.");
            Save();
            Publish(crew);
        }

        public static void ServerExpel(PlayerCrew actor, string memberId)
        {
            if (!TryMine(actor, out Crew crew, out CrewMember me)) return;
            CrewMember target = CrewRules.Find(crew, memberId);
            if (target == null || target == me) { actor.Notify(CrewRules.ReasonNoSuchMember); return; }
            if (!CrewRules.CanExpel(me.Rank, target.Rank))
            { actor.Notify(me.Rank == CrewRank.Officer ? "An Officer can only expel Members." : CrewRules.ReasonOfficerOnly); return; }
            PlayerCrew expelled = Online(target.characterId);
            Detach(crew, target.characterId);
            if (expelled != null) expelled.Tell($"You were expelled from the Crew {crew.name}.");
            TellCrew(crew, $"{target.name} was expelled from the Crew by {me.name}.");
            Save();
            Publish(crew);
        }

        public static void ServerDisband(PlayerCrew actor)
        {
            if (!TryMine(actor, out Crew crew, out CrewMember me)) return;
            if (me.Rank != CrewRank.Leader) { actor.Notify(CrewRules.ReasonLeaderOnly); return; }
            TellCrew(crew, $"The Crew {crew.name} was disbanded by {me.name}.");
            foreach (CrewMember member in crew.members.ToArray()) Detach(crew, member.characterId);
            Close(crew);
            Save();
        }

        /// <summary>Takes a member out of a Crew and clears what their player shows of it.</summary>
        private static void Detach(Crew crew, string characterId)
        {
            CrewRules.Remove(crew, characterId);
            ByMember.Remove(characterId);
            PlayerCrew player = Online(characterId);
            if (player != null) player.ServerShow(null, null);
        }

        private static void Close(Crew crew) => World.crews.Remove(crew);

        // ------------------------------------------------------------------ running a Crew

        public static void ServerSetOfficer(PlayerCrew actor, string memberId, bool officer)
        {
            if (!TryMine(actor, out Crew crew, out CrewMember me)) return;
            CrewMember target = CrewRules.Find(crew, memberId);
            int maxOfficers = actor.Settings != null ? actor.Settings.maxOfficers : 3;
            string blocked = CrewRules.OfficerBlockedReason(crew, me.Rank, target, officer, maxOfficers);
            if (blocked != null) { actor.Notify(blocked); return; }
            target.rank = (int)(officer ? CrewRank.Officer : CrewRank.Member);
            Save();
            TellCrew(crew, officer ? $"{target.name} is now an Officer of the Crew." : $"{target.name} is no longer an Officer.");
            Publish(crew);
        }

        public static void ServerHandOver(PlayerCrew actor, string memberId)
        {
            if (!TryMine(actor, out Crew crew, out CrewMember me)) return;
            int maxOfficers = actor.Settings != null ? actor.Settings.maxOfficers : 3;
            if (!CrewRules.TryHandOver(crew, me.characterId, memberId, maxOfficers, out string reason)) { actor.Notify(reason); return; }
            Save();
            TellCrew(crew, $"{CrewRules.Find(crew, memberId).name} now leads the Crew.");
            Publish(crew);
        }

        public static void ServerSetTitle(PlayerCrew actor, string memberId, string title)
        {
            if (!TryMine(actor, out Crew crew, out CrewMember me)) return;
            if (me.Rank != CrewRank.Leader) { actor.Notify(CrewRules.ReasonLeaderOnly); return; }
            CrewMember target = CrewRules.Find(crew, memberId);
            if (target == null) { actor.Notify(CrewRules.ReasonNoSuchMember); return; }
            string clean = CrewRules.CleanTitle(title, actor.Settings != null ? actor.Settings.titleMaxLength : 12);
            if (target.title == clean) return;
            target.title = clean;
            Save();
            PlayerCrew theirs = Online(target.characterId);
            if (theirs != null && theirs != actor) theirs.Tell(clean.Length > 0 ? $"Your Crew Title is now '{clean}'." : "Your Crew Title was taken away.");
            Publish(crew);
        }

        public static void ServerSetAnnouncement(PlayerCrew actor, string text)
        {
            if (!TryMine(actor, out Crew crew, out CrewMember me)) return;
            if (!CrewRules.CanAnnounce(me.Rank)) { actor.Notify(CrewRules.ReasonOfficerOnly); return; }
            string clean = CrewRules.CleanAnnouncement(text, actor.Settings != null ? actor.Settings.announcementMaxLength : 160);
            if (crew.announcement == clean) return;
            crew.announcement = clean;
            Save();
            // An announcement reaches every member, wherever they are.
            if (clean.Length > 0) TellCrew(crew, $"Crew announcement from {me.name}: {clean}");
            Publish(crew);
        }

        public static void ServerSetSymbol(PlayerCrew actor, int symbol, int colour)
        {
            if (!TryMine(actor, out Crew crew, out CrewMember me)) return;
            if (me.Rank != CrewRank.Leader) { actor.Notify(CrewRules.ReasonLeaderOnly); return; }
            int count = actor.Settings != null ? actor.Settings.symbolCount : 12;
            crew.symbol = Mathf.Clamp(symbol, 0, Mathf.Max(0, count - 1));
            crew.symbolColour = ColourIndex(actor.Settings, colour);
            Save();
            Publish(crew);
        }

        // A colour there is no swatch for is the first one.
        private static int ColourIndex(CrewSettings settings, int colour) =>
            settings != null && settings.symbolColours != null && colour >= 0 && colour < settings.symbolColours.Length ? colour : 0;

        // ------------------------------------------------------------------ GM

        /// <summary>
        /// GM: puts a player in the Crew called <paramref name="name"/>, founding it when there is none. No fee, level
        /// or NPC, but the faction rule holds for a GM too: a Crew never has members of two factions.
        /// </summary>
        public static string GmJoin(PlayerCrew player, string name)
        {
            if (string.IsNullOrEmpty(player.CharacterId)) return "That player has no saved character.";
            if (Of(player.CharacterId) != null) return $"{player.MemberName} is already in a Crew: gm.guild none first.";
            Crew crew = FindByName(name);
            if (crew == null)
            {
                ServerFound(player, name, 0, gm: true);
                crew = Of(player.CharacterId);
                return crew != null ? $"{player.MemberName} founded the Crew {crew.name}." : "That Crew could not be founded (see the notice on screen).";
            }
            if ((int)player.Faction != crew.faction) return CrewRules.OtherFaction(player.MemberName);
            if (crew.members.Count >= Cap(crew, player.Settings)) return $"{crew.name} is full.";
            var member = new CrewMember();
            Describe(member, player);
            CrewRules.Join(crew, member, Now);
            ByMember[member.characterId] = crew;
            Save();
            Publish(crew);
            return $"{player.MemberName} joined the Crew {crew.name}.";
        }

        /// <summary>GM: takes a player out of their Crew whatever their rank; the Crew closes when it empties and the next in line leads otherwise.</summary>
        public static string GmLeave(PlayerCrew player)
        {
            Crew crew = Of(player.CharacterId);
            if (crew == null) return $"{player.MemberName} is in no Crew.";
            bool led = CrewRules.Find(crew, player.CharacterId).Rank == CrewRank.Leader;
            Detach(crew, player.CharacterId);
            if (crew.members.Count == 0) Close(crew);
            else if (led)
            {
                crew.members.Sort((a, b) => b.rank != a.rank ? b.rank.CompareTo(a.rank) : a.joinedUtc.CompareTo(b.joinedUtc));
                crew.members[0].rank = (int)CrewRank.Leader;
            }
            Save();
            Publish(crew);
            return $"{player.MemberName} left the Crew {crew.name}.";
        }

        // ------------------------------------------------------------------ telling the members

        private static bool TryMine(PlayerCrew player, out Crew crew, out CrewMember me)
        {
            crew = Of(player.CharacterId);
            me = CrewRules.Find(crew, player.CharacterId);
            if (me != null) return true;
            player.Notify(CrewRules.ReasonNotInCrew);
            return false;
        }

        private static void TellCrew(Crew crew, string text, bool officersOnly = false)
        {
            foreach (CrewMember member in crew.members)
            {
                if (officersOnly && member.Rank == CrewRank.Member) continue;
                PlayerCrew player = Online(member.characterId);
                if (player != null) player.Tell(text);
            }
        }

        /// <summary>Sends every online member the Crew as it stands, and sets what everyone else sees of them.</summary>
        private static void Publish(Crew crew)
        {
            if (crew.members.Count == 0) return;
            var roster = new CrewMemberView[crew.members.Count];
            for (int i = 0; i < roster.Length; i++)
            {
                CrewMember member = crew.members[i];
                bool online = Online(member.characterId) != null;
                var line = new CrewMemberView
                {
                    rank = member.Rank,
                    level = (ushort)Mathf.Clamp(member.level, 1, ushort.MaxValue),
                    weaponClass = (byte)member.weaponClass,
                    online = online,
                    lastSeenUtc = member.lastSeenUtc,
                };
                line.id.CopyFromTruncated(member.characterId ?? string.Empty);
                line.name.CopyFromTruncated(member.name ?? string.Empty);
                line.title.CopyFromTruncated(member.title ?? string.Empty);
                roster[i] = line;
            }
            foreach (CrewMember member in crew.members)
            {
                PlayerCrew player = Online(member.characterId);
                if (player != null) player.ServerShow(crew, roster);
            }
        }
    }
}
