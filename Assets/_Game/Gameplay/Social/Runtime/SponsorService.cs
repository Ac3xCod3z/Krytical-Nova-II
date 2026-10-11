using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Persistence;
using Unity.Collections;
using UnityEngine;

namespace KryticalNova.Social
{
    /// <summary>
    /// Every Sponsor link on the server. Links are a world document (<see cref="ServerSave"/>), so they outlive
    /// both characters logging out. Runs only where the game is decided; players reach it through
    /// <see cref="PlayerSponsor"/>. A request needs both online (it is answered on screen); ending a link does not.
    /// </summary>
    public static class SponsorService
    {
        public const string WorldName = "sponsors";

        private static readonly Dictionary<string, PlayerSponsor> OnlinePlayers = new Dictionary<string, PlayerSponsor>(StringComparer.Ordinal);

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => OnlinePlayers.Clear();

        private static SponsorsWorld World => ServerSave.World<SponsorsWorld>(WorldName);
        private static void Save() => ServerSave.SaveWorld(WorldName);

        public static PlayerSponsor Online(string characterId) =>
            !string.IsNullOrEmpty(characterId) && OnlinePlayers.TryGetValue(characterId, out PlayerSponsor player) ? player : null;

        /// <summary>The link in which this character is the Recruit, or null.</summary>
        public static SponsorLink SponsorOf(string recruitId)
        {
            if (string.IsNullOrEmpty(recruitId)) return null;
            foreach (SponsorLink link in World.links)
                if (link.recruitId == recruitId) return link;
            return null;
        }

        /// <summary>The links in which this character is the Sponsor.</summary>
        public static void RecruitsOf(string sponsorId, List<SponsorLink> into)
        {
            into.Clear();
            if (string.IsNullOrEmpty(sponsorId)) return;
            foreach (SponsorLink link in World.links)
                if (link.sponsorId == sponsorId) into.Add(link);
        }

        public static int RecruitCount(string sponsorId)
        {
            int count = 0;
            if (string.IsNullOrEmpty(sponsorId)) return 0;
            foreach (SponsorLink link in World.links)
                if (link.sponsorId == sponsorId) count++;
            return count;
        }

        // ------------------------------------------------------------------ coming and going

        /// <summary>Server: a character came into the world. Its links take its name and level as they are now.</summary>
        public static void ServerArrived(PlayerSponsor player)
        {
            string id = player.CharacterId;
            if (string.IsNullOrEmpty(id)) return;
            OnlinePlayers[id] = player;
            bool changed = false;
            foreach (SponsorLink link in World.links)
            {
                if (link.sponsorId == id) { changed |= link.sponsorName != player.SponsorName || link.sponsorLevel != player.Level; link.sponsorName = player.SponsorName; link.sponsorLevel = player.Level; }
                if (link.recruitId == id) { changed |= link.recruitName != player.SponsorName || link.recruitLevel != player.Level; link.recruitName = player.SponsorName; link.recruitLevel = player.Level; }
            }
            if (changed) Save();
            PushViews(id);
        }

        /// <summary>Server: a character left the world.</summary>
        public static void ServerLeft(PlayerSponsor player)
        {
            string id = player.CharacterId;
            if (string.IsNullOrEmpty(id) || !OnlinePlayers.TryGetValue(id, out PlayerSponsor known) || known != player) return;
            OnlinePlayers.Remove(id);
            PushViews(id);
        }

        // ------------------------------------------------------------------ making a link

        /// <summary>
        /// Server: one player asks another to be its Sponsor or its Recruit; which is decided by their levels.
        /// The other is asked on screen. Returns why not (told to the asker), or null when the request went out.
        /// </summary>
        public static string ServerRequest(PlayerSponsor from, PlayerSponsor to, SponsorSettings settings)
        {
            if (from == null || to == null || settings == null) return "Sponsoring isn't available.";
            if (from == to) return SponsorRules.ReasonSelf;
            if (string.IsNullOrEmpty(from.CharacterId) || string.IsNullOrEmpty(to.CharacterId)) return "Only saved characters can be sponsored.";
            string gate = SocialGate.Refusal(from, to);
            if (gate != null) return gate;
            if (!SponsorRules.TryRoles(from.Level, to.Level, settings.sponsorMinLevel, out bool fromSponsors))
                return from.Level >= settings.sponsorMinLevel
                    ? $"A Recruit must be more than {settings.levelGap} levels below the Sponsor."
                    : $"One of you must be level {settings.sponsorMinLevel} to be the Sponsor.";

            PlayerSponsor sponsor = fromSponsors ? from : to, recruit = fromSponsors ? to : from;
            string blocked = BlockedReason(sponsor, recruit, settings);
            if (blocked != null)
            {
                // The rules speak of "that character": said the right way round for whoever asked.
                if (!fromSponsors && blocked == SponsorRules.ReasonHasSponsor) blocked = SponsorRules.ReasonYouHaveSponsor;
                return blocked;
            }
            if (to.HasPendingRequest) return $"{to.SponsorName} is answering another request.";
            to.ServerOfferRequest(from, fromSponsors, settings.requestSeconds);
            return null;
        }

        private static string BlockedReason(PlayerSponsor sponsor, PlayerSponsor recruit, SponsorSettings settings) =>
            SponsorRules.LinkBlockedReason(sponsor.Level, recruit.Level, sponsor.Faction, recruit.Faction,
                RecruitCount(sponsor.CharacterId), SponsorOf(sponsor.CharacterId) != null, SponsorOf(recruit.CharacterId) != null,
                RecruitCount(recruit.CharacterId) > 0, settings.sponsorMinLevel, settings.levelGap, settings.maxRecruits);

        /// <summary>Server: the asked player said yes. Everything is checked again: time has passed.</summary>
        public static string ServerAccept(PlayerSponsor from, PlayerSponsor to, bool fromSponsors, SponsorSettings settings)
        {
            if (from == null || to == null || settings == null) return "That request is no longer open.";
            PlayerSponsor sponsor = fromSponsors ? from : to, recruit = fromSponsors ? to : from;
            string blocked = BlockedReason(sponsor, recruit, settings);
            if (blocked != null) return blocked;

            World.links.Add(new SponsorLink
            {
                sponsorId = sponsor.CharacterId, sponsorName = sponsor.SponsorName, sponsorLevel = sponsor.Level,
                recruitId = recruit.CharacterId, recruitName = recruit.SponsorName, recruitLevel = recruit.Level,
                sinceUtc = DateTime.UtcNow.Ticks,
            });
            Save();
            sponsor.Tell($"{recruit.SponsorName} is now your Recruit.");
            recruit.Tell($"{sponsor.SponsorName} is now your Sponsor. Hunt near them for +{Mathf.RoundToInt(settings.experienceBonus * 100f)}% kill XP.");
            sponsor.ServerPushView();
            recruit.ServerPushView();
            return null;
        }

        // ------------------------------------------------------------------ ending a link

        /// <summary>Server: a player ends its link with the character of this id (its Sponsor, or one of its Recruits).</summary>
        public static string ServerEnd(PlayerSponsor player, string otherId)
        {
            string id = player.CharacterId;
            SponsorLink link = Find(id, otherId);
            if (link == null) return "There is no such link.";
            bool playerSponsors = link.sponsorId == id;
            string otherName = playerSponsors ? link.recruitName : link.sponsorName;
            Remove(link);
            player.Tell(playerSponsors ? $"{otherName} is no longer your Recruit." : $"{otherName} is no longer your Sponsor.");
            PlayerSponsor other = Online(otherId);
            if (other != null) other.Tell(playerSponsors ? $"{player.SponsorName} is no longer your Sponsor." : $"{player.SponsorName} is no longer your Recruit.");
            return null;
        }

        /// <summary>Server: the Recruit has caught up with the Sponsor. The link ends, and both are told why.</summary>
        public static void ServerGraduate(SponsorLink link, int levelGap)
        {
            if (link == null) return;
            Remove(link);
            PlayerSponsor sponsor = Online(link.sponsorId), recruit = Online(link.recruitId);
            if (recruit != null) recruit.Tell($"You are within {levelGap} levels of {link.sponsorName}: you no longer need a Sponsor.");
            if (sponsor != null) sponsor.Tell($"{link.recruitName} has caught up to within {levelGap} levels of you and is no longer your Recruit.");
        }

        private static SponsorLink Find(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return null;
            foreach (SponsorLink link in World.links)
                if ((link.sponsorId == a && link.recruitId == b) || (link.sponsorId == b && link.recruitId == a)) return link;
            return null;
        }

        private static void Remove(SponsorLink link)
        {
            World.links.Remove(link);
            Save();
            Online(link.sponsorId)?.ServerPushView();
            Online(link.recruitId)?.ServerPushView();
        }

        /// <summary>GM: links two online players, the higher as Sponsor, whatever the rules say. Returns what was done.</summary>
        public static string GmLink(PlayerSponsor a, PlayerSponsor b)
        {
            if (a == null || b == null || a == b) return "Two different players are needed.";
            if (string.IsNullOrEmpty(a.CharacterId) || string.IsNullOrEmpty(b.CharacterId)) return "Both need a saved character.";
            PlayerSponsor sponsor = a.Level >= b.Level ? a : b, recruit = sponsor == a ? b : a;
            SponsorLink old = SponsorOf(recruit.CharacterId);
            if (old != null) Remove(old);
            World.links.Add(new SponsorLink
            {
                sponsorId = sponsor.CharacterId, sponsorName = sponsor.SponsorName, sponsorLevel = sponsor.Level,
                recruitId = recruit.CharacterId, recruitName = recruit.SponsorName, recruitLevel = recruit.Level,
                sinceUtc = DateTime.UtcNow.Ticks,
            });
            Save();
            sponsor.ServerPushView();
            recruit.ServerPushView();
            return $"{sponsor.SponsorName} (level {sponsor.Level}) sponsors {recruit.SponsorName} (level {recruit.Level}).";
        }

        /// <summary>GM: ends every link of a player. Returns how many.</summary>
        public static int GmEndAll(PlayerSponsor player)
        {
            string id = player != null ? player.CharacterId : null;
            if (string.IsNullOrEmpty(id)) return 0;
            var mine = World.links.FindAll(link => link.sponsorId == id || link.recruitId == id);
            foreach (SponsorLink link in mine) Remove(link);
            return mine.Count;
        }

        // ------------------------------------------------------------------ what a player is shown

        private static readonly List<SponsorLinkView> viewScratch = new List<SponsorLinkView>(4);

        /// <summary>Server: a player's own links, as its window shows them.</summary>
        public static SponsorLinkView[] ServerView(PlayerSponsor player, float rangeMetres)
        {
            viewScratch.Clear();
            string id = player.CharacterId;
            if (!string.IsNullOrEmpty(id))
                foreach (SponsorLink link in World.links)
                {
                    bool otherSponsors = link.recruitId == id;
                    if (!otherSponsors && link.sponsorId != id) continue;
                    string otherId = otherSponsors ? link.sponsorId : link.recruitId;
                    PlayerSponsor other = Online(otherId);
                    var view = new SponsorLinkView
                    {
                        isSponsor = otherSponsors,
                        level = other != null ? other.Level : otherSponsors ? link.sponsorLevel : link.recruitLevel,
                        online = other != null,
                        near = other != null && player.IsNear(other, rangeMetres),
                    };
                    view.characterId.CopyFromTruncated(otherId ?? string.Empty);
                    view.name.CopyFromTruncated((otherSponsors ? link.sponsorName : link.recruitName) ?? string.Empty);
                    viewScratch.Add(view);
                }
            return viewScratch.ToArray();
        }

        // Everyone linked to a character hears when it comes or goes.
        private static void PushViews(string id)
        {
            Online(id)?.ServerPushView();
            foreach (SponsorLink link in World.links)
            {
                if (link.sponsorId == id) Online(link.recruitId)?.ServerPushView();
                else if (link.recruitId == id) Online(link.sponsorId)?.ServerPushView();
            }
        }
    }
}
