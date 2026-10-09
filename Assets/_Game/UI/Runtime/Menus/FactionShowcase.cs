using System;
using System.Collections.Generic;
using KryticalNova.CharacterCreation;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// The champions of faction select: one doll a faction, each in its top Epic set with its class weapon and the
    /// Temper glow, standing in the frame of its faction's card. The chosen one draws its weapon, stands larger and
    /// is lit; the others carry theirs and stand back in the dark. For display only: nothing here is a character
    /// anyone owns. The screen's controller says which frames there are and which faction is chosen.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FactionShowcase : MonoBehaviour
    {
        /// <summary>Who stands for a faction.</summary>
        [Serializable]
        public struct Champion
        {
            public FactionId faction;
            [Tooltip("The lineage's name (Civilian, Skeleton, Elf).")]
            public string lineage;
            public BodyType body;
            [Tooltip("How it stands while it is not the chosen one: a stance of the expression library (Synty's idle pack), by its group and name. Empty: the player's own idle.")]
            public string stanceGroup, stance;
        }

        [SerializeField] private CharacterCreationDatabase database;
        [SerializeField] private GameObject modelPrefab;
        [SerializeField] private GearDictionary gear;
        [SerializeField] private ItemCatalog items;
        [SerializeField] private GearProgressionSettings progression;
        [Tooltip("The stances the champions stand in (the creator's Preview tab uses the same).")]
        [SerializeField] private CharacterExpressionLibrary expressions;
        [SerializeField] private Camera viewCamera;
        [Tooltip("The dolls stand on this object's plane, facing the camera.")]
        [SerializeField] private Transform stage;
        [Tooltip("Copied under each doll's feet. May be none.")]
        [SerializeField] private GameObject pedestal;
        [Tooltip("The wall behind the dolls: its glow takes the chosen faction's colour. May be none.")]
        [SerializeField] private Renderer backdrop;
        [SerializeField] private Champion[] champions = Array.Empty<Champion>();

        [Header("What they wear")]
        [Tooltip("The highest item level shown: each slot wears its best Epic piece at or under it.")]
        [SerializeField, Range(1, 200)] private int gearLevel = 112;
        [Tooltip("The Temper the weapon glows at.")]
        [SerializeField, Range(0, 200)] private int temper = 120;

        [Header("How they stand")]
        [SerializeField] private AnimationClip idleClip;
        [SerializeField] private AnimationClip drawnIdleClip;
        [Tooltip("Played once as a doll is chosen: it draws its weapon.")]
        [SerializeField] private AnimationClip drawClip;
        [Tooltip("How far into the draw the weapon comes into the hand, as a share of the clip.")]
        [SerializeField, Range(0f, 1f)] private float drawMoment = 0.35f;
        [Tooltip("The share of its frame's height a doll's own height takes, chosen and not.")]
        [SerializeField, Range(0.3f, 1.2f)] private float chosenFill = 0.92f, otherFill = 0.8f;
        [Tooltip("A doll's height in metres, for fitting it to its frame.")]
        [SerializeField, Min(0.5f)] private float dollHeight = 2.05f;
        [Tooltip("How far the outer dolls turn towards the middle, in degrees.")]
        [SerializeField, Range(0f, 45f)] private float turnIn = 14f;
        [SerializeField, Min(0.01f)] private float easeSeconds = 0.25f;

        [Header("Light")]
        [Tooltip("Each doll's own lamp from the front and its rim lamp from behind (in its faction's colour): chosen, and not.")]
        [SerializeField, Min(0f)] private float chosenLamp = 46f, otherLamp = 5f, chosenRim = 70f, otherRim = 10f;
        [SerializeField] private Color lampColour = new Color(1f, 0.95f, 0.88f);
        [Tooltip("The wall's glow in the chosen faction's colour: how strong the colour is, and how bright.")]
        [SerializeField, Range(0f, 1f)] private float backdropSaturation = 0.5f, backdropValue = 0.24f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");

        private sealed class Stand
        {
            public FactionDefinition faction;
            public CharacterDoll doll;
            public Transform root, sheath;
            public AnimationClip idle, idleEnter; // its own stance, and the way into it
            public GameObject idleProp;
            public Light lamp, rim;
            public RectTransform frame;
            public float chosen;      // 0 stands back, 1 is the chosen one (eased)
            public float drawAt = -1f; // when the weapon comes into the hand (unscaled time), or -1
        }

        private readonly List<Stand> stands = new List<Stand>();
        private readonly Vector3[] corners = new Vector3[4];
        private MaterialPropertyBlock block;
        private Color backdropGlow, backdropGlowTarget;
        private int selected = -1;

        /// <summary>
        /// Stands a champion for each of <paramref name="factions"/> in the frame of the same place in
        /// <paramref name="frames"/>. Asked again (the menus changed skin), the same dolls move to the new frames.
        /// </summary>
        public void Show(IReadOnlyList<FactionDefinition> factions, IReadOnlyList<RectTransform> frames)
        {
            for (int i = 0; i < factions.Count; i++)
            {
                Stand stand = i < stands.Count ? stands[i] : null;
                if (stand == null || stand.faction != factions[i])
                {
                    if (stand != null && stand.root != null) Destroy(stand.root.gameObject);
                    stand = Build(factions[i]);
                    if (i < stands.Count) stands[i] = stand; else stands.Add(stand);
                }
                stand.frame = i < frames.Count ? frames[i] : null;
            }
            for (int i = stands.Count - 1; i >= factions.Count; i--)
            {
                if (stands[i].root != null) Destroy(stands[i].root.gameObject);
                stands.RemoveAt(i);
            }
            int was = selected;
            selected = -1;
            Select(was);
        }

        /// <summary>The faction whose champion steps up. Out of range: nobody does.</summary>
        public void Select(int index)
        {
            if (index == selected) return;
            if (selected >= 0 && selected < stands.Count) Carry(stands[selected]);
            selected = index;
            if (index < 0 || index >= stands.Count) return;
            Stand stand = stands[index];
            Color accent = stand.faction != null ? stand.faction.accentColor : Color.white;
            Color.RGBToHSV(accent, out float hue, out float saturation, out _);
            backdropGlowTarget = Color.HSVToRGB(hue, Mathf.Min(saturation, backdropSaturation), backdropValue);
            if (stand.doll == null) return;
            // It draws its weapon (the weapon comes into the hand part of the way through) and stands ready.
            AnimationClip ready = drawnIdleClip != null ? drawnIdleClip : idleClip;
            if (drawClip != null && stand.doll.Weapon != null) stand.drawAt = Time.unscaledTime + drawClip.length * drawMoment;
            else Draw(stand);
            stand.doll.PlayIdle(ready, stand.doll.Weapon != null ? drawClip : null);
        }

        private Stand Build(FactionDefinition faction)
        {
            var stand = new Stand { faction = faction };
            var root = new GameObject("Champion_" + (faction != null ? faction.id.ToString() : "None"));
            stand.root = root.transform;
            stand.root.SetParent(stage != null ? stage : transform, false);
            if (faction == null || database == null) return stand;

            if (!TryGetChampion(faction.id, out Champion champion)) return stand;
            LineageDefinition lineage = FindLineage(database, faction.id, champion.lineage);
            if (lineage == null) return stand;

            stand.idle = idleClip;
            if (TryGetStance(expressions, champion.stanceGroup, champion.stance, out CharacterExpressionLibrary.Motion motion) && motion.Has(champion.body))
            {
                stand.idle = motion.For(champion.body);
                stand.idleEnter = motion.EnterFor(champion.body);
                stand.idleProp = motion.prop;
            }
            stand.doll = CharacterDoll.Create(modelPrefab, stand.root, stand.idle, keepWeapon: true);
            if (stand.doll == null) return stand;
            stand.doll.Apply(RecipeEdit.Default(lineage, champion.body), database);

            WeaponVisualMount mount = stand.doll.Weapon;
            if (mount != null) stand.sheath = mount.transform.parent;
            if (gear != null && items != null)
            {
                // Its top Epic set and its class weapon at the showcase's Temper, dressed as any doll is.
                var worn = new List<WornPiece>(WornSlots.Length + 1);
                foreach (EquipSlot slot in WornSlots)
                    worn.Add(new WornPiece(slot, BestPiece(gear, slot, faction.id, lineage, WeaponClassId.None, gearLevel), 0));
                worn.Add(new WornPiece(EquipSlot.Weapon,
                    BestPiece(gear, EquipSlot.Weapon, faction.id, lineage, WeaponClassRules.DefaultFor(faction.id), gearLevel), temper));
                DollOutfit.Wear(stand.doll, worn, items, progression);
            }

            if (pedestal != null)
            {
                GameObject foot = Instantiate(pedestal, stand.root);
                foot.name = "Pedestal";
                foot.SetActive(true);
            }
            // The doll faces the camera; its lamps ride with it.
            stand.lamp = Lamp(stand.root, "Lamp", new Vector3(0.5f, 2.9f, -2.6f), lampColour, otherLamp);
            stand.rim = Lamp(stand.root, "Rim", new Vector3(-0.9f, 2.5f, 2.3f), faction.accentColor, otherRim);
            foreach (Transform part in stand.root.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = gameObject.layer;
            return stand;
        }

        private static Light Lamp(Transform root, string name, Vector3 at, Color colour, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, 1.1f, 0f) - at);
            var light = go.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = colour;
            light.intensity = intensity;
            light.range = 9f;
            light.spotAngle = 46f;
            light.innerSpotAngle = 26f;
            light.shadows = LightShadows.None;
            return light;
        }

        // ------------------------------------------------------------------ what a champion wears

        /// <summary>The slots a champion's gear shows in (rings and amulets have no look; the weapon is chosen by class).</summary>
        public static readonly EquipSlot[] WornSlots = { EquipSlot.Armor, EquipSlot.Gloves, EquipSlot.Boots, EquipSlot.Cape };

        /// <summary>A stance of the library by its group and name.</summary>
        public static bool TryGetStance(CharacterExpressionLibrary library, string group, string name, out CharacterExpressionLibrary.Motion stance)
        {
            stance = default;
            if (library == null || string.IsNullOrEmpty(name)) return false;
            foreach (CharacterExpressionLibrary.Motion each in library.Idles)
            {
                if (!string.Equals(each.name, name, StringComparison.OrdinalIgnoreCase) || !string.Equals(each.group, group, StringComparison.OrdinalIgnoreCase)) continue;
                stance = each;
                return true;
            }
            return false;
        }

        public bool TryGetChampion(FactionId faction, out Champion champion)
        {
            foreach (Champion each in champions)
                if (each.faction == faction) { champion = each; return true; }
            champion = default;
            return false;
        }

        public static LineageDefinition FindLineage(CharacterCreationDatabase database, FactionId faction, string name)
        {
            var lineages = new List<LineageDefinition>();
            database.GetLineages(faction, lineages);
            foreach (LineageDefinition each in lineages)
                if (string.Equals(each.displayName, name, StringComparison.OrdinalIgnoreCase)) return each;
            return lineages.Count > 0 ? lineages[0] : null;
        }

        /// <summary>
        /// The id of the best Epic piece of a slot this lineage can wear at or under <paramref name="level"/>, or 0.
        /// A weapon is of <paramref name="weaponClass"/>; anything else is of the lineage's own pack where the piece
        /// names one.
        /// </summary>
        public static ushort BestPiece(GearDictionary gear, EquipSlot slot, FactionId faction, LineageDefinition lineage, WeaponClassId weaponClass, int level)
        {
            ushort best = 0;
            int bestLevel = -1;
            foreach (GearDictionary.Entry entry in gear.Entries)
            {
                if (entry.slot != slot || entry.rarity != ItemRarity.Epic || entry.itemLevel > level || entry.itemLevel <= bestLevel) continue;
                if (!FactionRules.Includes(entry.factions, faction)) continue;
                if (slot == EquipSlot.Weapon)
                {
                    if (entry.weaponClass != weaponClass) continue;
                }
                else
                {
                    if (lineage.body != BodyFit.None && (entry.bodies & lineage.body) == 0) continue;
                    if (!string.IsNullOrEmpty(lineage.ownPack) && !string.IsNullOrEmpty(entry.pack) && entry.pack != lineage.ownPack) continue;
                }
                best = entry.itemId;
                bestLevel = entry.itemLevel;
            }
            return best;
        }

        // ------------------------------------------------------------------ every frame

        private void Draw(Stand stand) => Hold(stand, stand.doll != null && stand.doll.Weapon != null ? stand.doll.Weapon.HandSocket : null);

        private void Carry(Stand stand)
        {
            stand.drawAt = -1f;
            Hold(stand, stand.sheath);
            if (stand.doll != null && stand.idle != null) stand.doll.PlayIdle(stand.idle, stand.idleEnter, stand.idleProp);
        }

        private static void Hold(Stand stand, Transform socket)
        {
            WeaponVisualMount mount = stand.doll != null ? stand.doll.Weapon : null;
            if (mount == null || socket == null || mount.transform.parent == socket) return;
            mount.transform.SetParent(socket, false);
            mount.transform.localPosition = Vector3.zero;
            mount.transform.localRotation = Quaternion.identity;
            mount.transform.localScale = Vector3.one;
        }

        private void LateUpdate()
        {
            if (viewCamera == null) return;
            float ease = 1f - Mathf.Exp(-Time.unscaledDeltaTime / easeSeconds);
            Plane floor = new Plane(-viewCamera.transform.forward, stage != null ? stage.position : transform.position);
            for (int i = 0; i < stands.Count; i++)
            {
                Stand stand = stands[i];
                if (stand.root == null) continue;
                if (stand.drawAt >= 0f && Time.unscaledTime >= stand.drawAt)
                {
                    stand.drawAt = -1f;
                    Draw(stand);
                }
                stand.chosen = Mathf.Lerp(stand.chosen, i == selected ? 1f : 0f, ease);
                if (stand.lamp != null) stand.lamp.intensity = Mathf.Lerp(otherLamp, chosenLamp, stand.chosen);
                if (stand.rim != null) stand.rim.intensity = Mathf.Lerp(otherRim, chosenRim, stand.chosen);

                bool placed = stand.frame != null && stand.frame.gameObject.activeInHierarchy;
                if (stand.root.gameObject.activeSelf != placed) stand.root.gameObject.SetActive(placed);
                if (!placed) continue;

                // The frame is on an overlay canvas: its corners are pixels of the screen. The doll's feet go on
                // the middle of its foot, at the stage's depth, and the doll is as tall as its share of the frame.
                stand.frame.GetWorldCorners(corners);
                Vector3 foot = (corners[0] + corners[3]) * 0.5f;
                float framePixels = corners[1].y - corners[0].y;
                Ray ray = viewCamera.ScreenPointToRay(foot);
                if (!floor.Raycast(ray, out float distance)) continue;
                Vector3 at = ray.GetPoint(distance);
                float depth = Vector3.Dot(at - viewCamera.transform.position, viewCamera.transform.forward);
                float metresPerPixel = 2f * depth * Mathf.Tan(viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, viewCamera.pixelHeight);
                float fill = Mathf.Lerp(otherFill, chosenFill, stand.chosen);
                stand.root.position = at;
                stand.root.localScale = Vector3.one * (framePixels * fill * metresPerPixel / dollHeight);
                float side = stands.Count > 1 ? i / (stands.Count - 1f) * 2f - 1f : 0f; // -1 left, 1 right
                // The model looks along its own forward: towards the camera, the outer ones a little towards the middle.
                stand.root.rotation = Quaternion.LookRotation(-viewCamera.transform.forward, viewCamera.transform.up) * Quaternion.Euler(0f, side * turnIn, 0f);
            }

            if (backdrop == null) return;
            backdropGlow = Color.Lerp(backdropGlow, backdropGlowTarget, ease);
            if (block == null) block = new MaterialPropertyBlock();
            backdrop.GetPropertyBlock(block);
            block.SetColor(GlowColorId, backdropGlow);
            backdrop.SetPropertyBlock(block);
        }
    }
}
