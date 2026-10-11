using System.Collections.Generic;
using KryticalNova.Items;
using KryticalNova.Progression;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>One skill or item that takes its icon from the icon library.</summary>
    public sealed class IconTarget
    {
        public IconTargetKind kind;
        /// <summary>What the library knows it by: the skill's id, Run / Jump, or the item asset's name.</summary>
        public string id;
        public string name;
        /// <summary>What it is listed under: its class, or the folder its asset is in.</summary>
        public string group;
        public int order;
        public Object asset;
        public IconRequest request;

        public Sprite Icon
        {
            get
            {
                switch (asset)
                {
                    case SkillDefinitionSO skill: return skill.icon;
                    case MovementSkillData movement: return movement.icon;
                    case ItemDefinition item: return item.icon;
                    default: return null;
                }
            }
            set
            {
                switch (asset)
                {
                    case SkillDefinitionSO skill: skill.icon = value; break;
                    case MovementSkillData movement: movement.icon = value; break;
                    case ItemDefinition item: item.icon = value; break;
                    default: return;
                }
                EditorUtility.SetDirty(asset);
            }
        }
    }

    /// <summary>
    /// Finds everything in the project that takes its icon from the icon library: every skill, and every item that
    /// has no model to render an icon from (materials, consumables, permits, jewellery). Gear and pets are not
    /// listed: their icons are pictures of their own models (<see cref="ItemIconCapture"/>).
    /// </summary>
    public static class IconTargets
    {
        public const string SkillRoot = "Assets/_Game/Gameplay/Progression/Data/Assets/Skills";
        public const string ItemRoot = "Assets/_Game/Gameplay/Items/Data/Assets";

        public static List<IconTarget> Skills()
        {
            var found = new List<IconTarget>();
            foreach (string guid in AssetDatabase.FindAssets("t:SkillDefinitionSO", new[] { SkillRoot }))
            {
                var skill = AssetDatabase.LoadAssetAtPath<SkillDefinitionSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (skill == null) continue;
                found.Add(new IconTarget
                {
                    kind = IconTargetKind.Skill, id = skill.skillId, name = skill.displayName, asset = skill,
                    group = skill.IsClassSkill ? WeaponClasses.DisplayName(skill.weaponClass) : "Shared",
                    order = (skill.IsClassSkill ? (int)skill.weaponClass * 100 : 10000) + (int)skill.classSlot,
                    request = IconLibrary.RequestFor(skill),
                });
            }
            foreach (string guid in AssetDatabase.FindAssets("t:MovementSkillData", new[] { SkillRoot }))
            {
                var skill = AssetDatabase.LoadAssetAtPath<MovementSkillData>(AssetDatabase.GUIDToAssetPath(guid));
                if (skill == null) continue;
                found.Add(new IconTarget
                {
                    kind = IconTargetKind.MovementSkill, id = skill.skillId.ToString(), name = skill.displayName, asset = skill,
                    group = "Movement", order = 20000 + (int)skill.skillId, request = IconLibrary.RequestFor(skill),
                });
            }
            found.Sort((a, b) => a.order != b.order ? a.order.CompareTo(b.order) : string.CompareOrdinal(a.id, b.id));
            return found;
        }

        public static List<IconTarget> Items()
        {
            var found = new List<IconTarget>();
            foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { ItemRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (item == null || !TakesLibraryIcon(item)) continue;
                string folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path));
                found.Add(new IconTarget
                {
                    kind = IconTargetKind.Item, id = item.name, name = item.displayName, asset = item, group = folder,
                    order = item.itemId, request = IconLibrary.RequestForItem(item.displayName, item.description),
                });
            }
            found.Sort((a, b) =>
            {
                int byGroup = string.CompareOrdinal(a.group, b.group);
                return byGroup != 0 ? byGroup : a.order.CompareTo(b.order);
            });
            return found;
        }

        /// <summary>
        /// True for an item with nothing to render: no weapon model, no pet, nothing worn. Those take a pack icon
        /// from the library; everything else is pictured from its own model. A tier of the gear ladder is never
        /// listed: it wears its look's icon (<see cref="GearStatBaker"/>), so the jewellery looks listed here hand
        /// their icons on to every ring and amulet of the ladder.
        /// </summary>
        public static bool TakesLibraryIcon(ItemDefinition item)
        {
            if (item == null || item.IsLadderGear || item.weaponPrefab != null || item.petPrefab != null) return false;
            // It shows another item's icon: the library knows that one, not this.
            if (item.sharesIconOf != null) return false;
            if (item.appearancePartIds != null && item.appearancePartIds.Length > 0) return false;
            return item.appearanceParts == null || item.appearanceParts.Count == 0;
        }
    }
}
