using System;
using System.Collections.Generic;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;

namespace KryticalNova.CharacterCreation
{
    /// <summary>The three NPCs that change a character's look after it was made. Sent over the network: append only.</summary>
    public enum RestyleService : byte
    {
        /// <summary>Hair, brows, facial hair and hair colour.</summary>
        Stylist = 0,
        /// <summary>The face: head, eyes, ears, nose, teeth, skin tone, eye colour, marks.</summary>
        Reshaper = 1,
        /// <summary>The body: build, muscle, the body sliders (bust, butt, definition) and height.</summary>
        Coach = 2
    }

    /// <summary>The colour channels of a look, as recipes store them. Never renumber.</summary>
    public static class CharacterColorChannels
    {
        public const byte Hair = 1, Eyes = 2, Glow = 3, Bone = 4, Metal = 5;
        /// <summary>
        /// Heterochromia (owner, 24 Oct 2026): the right eye's own colour, on top of <see cref="Eyes"/>. A look has it
        /// only while the box is ticked; it is offered wherever Eyes is and never listed among a lineage's channels.
        /// </summary>
        public const byte EyeRight = 6;
    }

    /// <summary>
    /// What each of the three services may change about a look, free of networking so it can be tested. The server
    /// never takes a client's whole recipe: it takes only the fields the service owns out of it and keeps the rest
    /// of the saved look (<see cref="Merge"/>), so a Stylist can never be used to change a face or a build.
    /// Faction, lineage, body type and Discipline belong to no service: they are never changed.
    /// </summary>
    public static class RestyleRules
    {
        public const string ReasonNothingChanged = "Nothing was changed.";
        public const int BodyLimit = 100;
        public const int MarkLimit = 100;

        /// <summary>True when a service may change the part worn in a head region.</summary>
        public static bool Owns(RestyleService service, BodyRegion region)
        {
            switch (region)
            {
                case BodyRegion.Hair:
                case BodyRegion.EyebrowLeft:
                case BodyRegion.EyebrowRight:
                case BodyRegion.FacialHair:
                    return service == RestyleService.Stylist;
                case BodyRegion.Head:
                case BodyRegion.EyeLeft:
                case BodyRegion.EyeRight:
                case BodyRegion.EarLeft:
                case BodyRegion.EarRight:
                case BodyRegion.Nose:
                case BodyRegion.Teeth:
                case BodyRegion.Tongue:
                    return service == RestyleService.Reshaper;
                default:
                    return false;
            }
        }

        /// <summary>True when a service may change a colour channel. Hair is the Stylist's; the colours of the face and the frame are the Reshaper's.</summary>
        public static bool OwnsChannel(RestyleService service, byte channel) =>
            channel == CharacterColorChannels.Hair ? service == RestyleService.Stylist : service == RestyleService.Reshaper;

        /// <summary>Skin tone and the marks on the skin (scars, eyeliner, grime).</summary>
        public static bool OwnsSkin(RestyleService service) => service == RestyleService.Reshaper;

        /// <summary>The body: build, muscle, bust, butt, definition and height.</summary>
        public static bool OwnsBody(RestyleService service) => service == RestyleService.Coach;

        /// <summary>
        /// The look a service would leave: <paramref name="current"/> with only what the service owns taken from
        /// <paramref name="proposed"/>. Whether the result is a look the lineage allows is the validator's business.
        /// </summary>
        public static CharacterRecipe Merge(in CharacterRecipe current, in CharacterRecipe proposed, RestyleService service, SidekickPartCatalog catalog)
        {
            CharacterRecipe result = current.Clone();

            var parts = new List<ushort>(current.PartCount + 4);
            for (int i = 0; i < current.PartCount; i++)
                if (current.parts[i] != 0 && !Owns(service, RegionOf(catalog, current.parts[i]))) parts.Add(current.parts[i]);
            for (int i = 0; i < proposed.PartCount; i++)
                if (proposed.parts[i] != 0 && Owns(service, RegionOf(catalog, proposed.parts[i])) && !parts.Contains(proposed.parts[i]))
                    parts.Add(proposed.parts[i]);
            result.parts = parts.ToArray();

            var colors = new List<ColorOverride>(current.ColorCount + 2);
            for (int i = 0; i < current.ColorCount; i++)
                if (!OwnsChannel(service, current.colors[i].channel)) colors.Add(current.colors[i]);
            for (int i = 0; i < proposed.ColorCount; i++)
                if (OwnsChannel(service, proposed.colors[i].channel)) colors.Add(proposed.colors[i]);
            result.colors = colors.ToArray();
            if (OwnsChannel(service, CharacterColorChannels.Eyes)) result.eyeGlow = proposed.eyeGlow;

            if (OwnsSkin(service))
            {
                result.skinTone = proposed.skinTone;
                result.scars = (byte)Math.Min(MarkLimit, (int)proposed.scars);
                result.eyeliner = (byte)Math.Min(MarkLimit, (int)proposed.eyeliner);
                result.grime = (byte)Math.Min(MarkLimit, (int)proposed.grime);
            }
            if (OwnsBody(service))
            {
                // The body sliders as the creator sets them (22 Oct 2026): bust and butt on a feminine body only,
                // definition on a masculine one, height on every one. The body type is the saved one, never the
                // client's; a look saved before the sliders is brought up to date first, which keeps how it looks.
                result.Upgrade();
                result.CopyBodyFrom(proposed);
                result.build = (sbyte)Math.Max(-BodyLimit, Math.Min(BodyLimit, (int)proposed.build));
                result.muscle = (sbyte)Math.Max(-BodyLimit, Math.Min(BodyLimit, (int)proposed.muscle));
            }
            return result;
        }

        /// <summary>True when two looks are the same, whatever order their parts and colours are listed in.</summary>
        public static bool Same(in CharacterRecipe a, in CharacterRecipe b)
        {
            if (a.faction != b.faction || a.lineage != b.lineage || a.bodyType != b.bodyType || a.build != b.build || a.muscle != b.muscle
                || a.palette != b.palette || a.skinTone != b.skinTone || a.scars != b.scars || a.eyeliner != b.eyeliner || a.grime != b.grime
                || a.weaponClass != b.weaponClass || a.eyeGlow != b.eyeGlow || !a.ToShape().Equals(b.ToShape()) || a.Height != b.Height || a.PartCount != b.PartCount || a.ColorCount != b.ColorCount)
                return false;
            for (int i = 0; i < a.PartCount; i++)
                if (Array.IndexOf(b.parts, a.parts[i]) < 0) return false;
            for (int i = 0; i < a.ColorCount; i++)
                if (Array.IndexOf(b.colors, a.colors[i]) < 0) return false;
            return true;
        }

        private static BodyRegion RegionOf(SidekickPartCatalog catalog, ushort partId) =>
            catalog != null && catalog.TryGet(partId, out var entry) ? entry.region : BodyRegion.None;
    }
}
