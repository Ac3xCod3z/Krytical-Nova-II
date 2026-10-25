using System;
using System.Collections.Generic;
using KryticalNova.Rendering;
using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// Owns a Sidekick character's visible body: fixed parts (head, face, hair...) plus one set of parts per visual
    /// equip slot (Armor, Gloves, Boots, Cape), falling back to base-body defaults when a slot is empty. Changes are
    /// batched and rebuilt once in LateUpdate into the single existing SkinnedMeshRenderer, so a character stays one
    /// skinned draw no matter what it wears.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterAppearance : MonoBehaviour
    {
        private static readonly EquipSlot[] VisualSlots = { EquipSlot.Armor, EquipSlot.Gloves, EquipSlot.Boots, EquipSlot.Cape };
        private static readonly EquipSlot[] ColorSlots = { EquipSlot.Cape, EquipSlot.Boots, EquipSlot.Gloves, EquipSlot.Armor };

        /// <summary>
        /// The visual slots in the order their set colours are laid down. The armor comes last: metal, straps and
        /// lights are shared by every piece, and the armor is the piece that should decide them.
        /// </summary>
        public static IReadOnlyList<EquipSlot> ColorOrder => ColorSlots;

        [Tooltip("The character's single skinned renderer. Its original mesh supplies the reference bones/bindposes.")]
        [SerializeField] private SkinnedMeshRenderer targetRenderer;
        [Tooltip("Always worn: head, hair, face and any recipe part that no equip slot owns.")]
        [SerializeField] private List<SidekickPartRef> fixedParts = new List<SidekickPartRef>();
        [Tooltip("Shown for a slot's regions while that slot is empty (Sidekick SK_HUMN_BASE body).")]
        [SerializeField] private List<SidekickPartRef> defaultParts = new List<SidekickPartRef>();
        [SerializeField] private BodyShapeWeights bodyShape = new BodyShapeWeights { buff = 90f, heavy = 10f };
        [Tooltip("Resting face, baked into the merged mesh. Shape = Sidekick facial blendshape name after the last '.', " +
                 "weight 0-100. Tune live with Sidekick's FacialController on a test head, then copy the values here.")]
        [SerializeField] private List<FaceShapeWeight> idleFace = new List<FaceShapeWeight>
        {
            new FaceShapeWeight("mouthSmileLeft", 22f),
            new FaceShapeWeight("mouthSmileRight", 22f),
            new FaceShapeWeight("eyeSquintLeft", 12f),
            new FaceShapeWeight("eyeSquintRight", 12f),
            new FaceShapeWeight("cheekSquintLeft", 10f),
            new FaceShapeWeight("cheekSquintRight", 10f),
        };
        [Tooltip("Adds a FaceBlinker; the eyelid blink shapes are kept live on the merged mesh.")]
        [SerializeField] private bool blink = true;
        [Tooltip("The halo round a worn part's lights (a robot head's lens): reach in metres, brightness at the outline, " +
                 "and how deep and fast it breathes. The colour is the lights' own. Bloom is off in this game, so the halo is the glow.")]
        [SerializeField] private float lightGlowWidth = 0.03f;
        [SerializeField] private float lightGlowIntensity = 1.8f;
        [SerializeField, Range(0f, 1f)] private float lightGlowPulse = 0.3f;
        [SerializeField] private float lightGlowPulseSpeed = 0.6f;
        [Tooltip("Glowing eyes: how strongly the eyes' second material shines, times the eye-glow map. Kept slight " +
                 "(owner, 24 Oct 2026: the normal eye colour with an emissive channel, no halo).")]
        [SerializeField] private float eyeGlowIntensity = 0.29f; // owner, 24 Oct 2026: "glows just a little bit more" (+15 %)
        [Tooltip("Bone order + bindposes of the original baked mesh, captured by the setup tool. Keeps merges exact and lets clones (the preview doll) merge without reading the live mesh.")]
        [SerializeField] private string[] referenceBoneNames = System.Array.Empty<string>();
        [SerializeField] private Matrix4x4[] referenceBindposes = System.Array.Empty<Matrix4x4>();
        [Tooltip("Debug: render one SkinnedMeshRenderer per part instead of merging (more draw calls).")]
        [SerializeField] private bool mergeParts = true;
        [Tooltip("The species' chest wrap, worn on a feminine body whenever the resolved torso needs one. Set by ApplyBase.")]
        [SerializeField] private SidekickPartRef wrapPart;
        [Tooltip("The bare body and head under everything worn: gear, hair and beards without body keys of their own take theirs from it. Set by ApplyBase.")]
        [SerializeField] private List<SidekickPartRef> bareBody = new List<SidekickPartRef>();

        private readonly Dictionary<EquipSlot, IReadOnlyList<SidekickPartRef>> slotParts = new Dictionary<EquipSlot, IReadOnlyList<SidekickPartRef>>();
        private readonly Dictionary<EquipSlot, ItemDefinition> worn = new Dictionary<EquipSlot, ItemDefinition>();
        private readonly Dictionary<EquipSlot, Color32[]> dyes = new Dictionary<EquipSlot, Color32[]>();
        private readonly SortedDictionary<BodyRegion, SidekickPartRef> resolved = new SortedDictionary<BodyRegion, SidekickPartRef>();
        private readonly List<SidekickPartRef> partList = new List<SidekickPartRef>(48);
        private readonly List<Transform> boneBuffer = new List<Transform>(160);
        private readonly List<string> boneNames = new List<string>(160);
        private readonly List<SkinnedMeshRenderer> debugRenderers = new List<SkinnedMeshRenderer>();
        private readonly List<SkinnedMeshRenderer> lightRenderers = new List<SkinnedMeshRenderer>();
        private GlowHalo lightHalo; // made on first use: a halo draws its beat from Random, which a component's constructor may not call
        private SkinnedMeshMerger merger;
        private EyeGlow eyeGlow;
        // Parts that move by real cloth (GearClothLook, a cloak): left out of the merge and drawn by this, with Unity's Cloth.
        private GearCloth cloth;
        private CharacterLod lod;
        private readonly List<SidekickPartRef> mergeList = new List<SidekickPartRef>(48);
        private readonly List<(SidekickPartRef part, GearClothLook look)> clothParts = new List<(SidekickPartRef, GearClothLook)>();
        private bool eyesGlow;
        private Texture eyeColourMap, eyeEmissionMap;
        private Mesh ownedMesh;
        private CharacterAppearance sharedSource;
        private bool dirty;
        private bool paintsStale;
        private bool helmetShown = true;
        // The paints the shown mesh draws in (submesh i + 1 = paint i) and a part of each, to find its piece and dye.
        private readonly List<GearPaintLook> shownPaints = new List<GearPaintLook>();
        private readonly List<SidekickPartRef> shownPaintParts = new List<SidekickPartRef>();
        private static readonly List<Material> materialBuffer = new List<Material>(8);

        /// <summary>Raised after every rebuild, so mirrors (the equipment preview doll) can re-share the mesh.</summary>
        public event Action Rebuilt;
        /// <summary>Raised when the piece in a visual slot changes: the colours a piece brings change with it.</summary>
        public event Action WornChanged;

        /// <summary>The piece shown in a visual slot, or null.</summary>
        public ItemDefinition GetWorn(EquipSlot slot) => worn.TryGetValue(slot, out var item) ? item : null;
        /// <summary>The colour grid a visual slot's piece is dyed in, or null (the piece's own colours).</summary>
        public Color32[] GetDye(EquipSlot slot) => dyes.TryGetValue(slot, out var cells) ? cells : null;

        public bool IsConfigured => fixedParts.Count > 0 || defaultParts.Count > 0;
        public SkinnedMeshRenderer Renderer => targetRenderer;

        private List<string> liveFaceShapes;
        public Mesh CurrentMesh => ownedMesh;
        public IReadOnlyList<string> CurrentBoneNames => boneNames;
        public IReadOnlyList<SidekickPartRef> CurrentParts => partList;

        private void Awake()
        {
            if (targetRenderer == null) targetRenderer = GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (targetRenderer == null) return;
            if (referenceBoneNames.Length > 0)
            {
                merger = new SkinnedMeshMerger(transform, null, null);
                var bones = new Transform[referenceBoneNames.Length];
                for (int i = 0; i < bones.Length; i++) bones[i] = merger.FindBone(referenceBoneNames[i]);
                merger.SetReference(bones, referenceBindposes);
            }
            else if (targetRenderer.sharedMesh != null && targetRenderer.sharedMesh.isReadable)
                merger = new SkinnedMeshMerger(transform, targetRenderer.bones, targetRenderer.sharedMesh.bindposes);
            else return;
            // Keep the baked mesh until the first LateUpdate, by which time the equipment binder has applied the
            // loadout, so spawning costs exactly one merge.
            dirty = IsConfigured;
            EnsureBlinker();
        }

        // FaceBlinker normally lives on the prefab (tunable in the Inspector); add one only if it's missing.
        private void EnsureBlinker()
        {
            if (!blink || !IsConfigured || targetRenderer == null) return;
            if (!TryGetComponent<FaceBlinker>(out var blinker)) blinker = gameObject.AddComponent<FaceBlinker>();
            if (!blinker.HasTarget) blinker.Bind(targetRenderer);
        }

        private void OnDestroy()
        {
            if (sharedSource != null) sharedSource.Rebuilt -= OnSourceRebuilt;
            if (ownedMesh != null) Destroy(ownedMesh);
            lightHalo?.Clear();
            eyeGlow?.Dispose();
            cloth?.Dispose();
        }

        /// <summary>
        /// Replace the character's own look at runtime (character creation, a replicated recipe): the always-worn
        /// parts, the parts shown while a slot is empty, the body shape and the chest wrap. Equipped gear is kept.
        /// <paramref name="bare"/> is the bare body and head that worn parts take missing body keys from.
        /// </summary>
        public void ApplyBase(IReadOnlyList<SidekickPartRef> fixedList, IReadOnlyList<SidekickPartRef> defaults,
            BodyShapeWeights shape, SidekickPartRef wrap = default, IReadOnlyList<SidekickPartRef> bare = null)
        {
            bareBody.Clear();
            if (bare != null) bareBody.AddRange(bare);
            fixedParts.Clear();
            if (fixedList != null) fixedParts.AddRange(fixedList);
            defaultParts.Clear();
            if (defaults != null) defaultParts.AddRange(defaults);
            bodyShape = shape;
            wrapPart = wrap;
            StopSharing();
            EnsureBlinker();
            dirty = true;
        }

        /// <summary>
        /// Show <paramref name="item"/>'s parts for <paramref name="slot"/>; null shows the default body.
        /// <paramref name="dye"/> is the colour grid of the colourway the piece is dyed in (a theme's cells), or
        /// null for its own colours.
        /// </summary>
        public void SetSlot(EquipSlot slot, ItemDefinition item, Color32[] dye = null)
        {
            if (Array.IndexOf(VisualSlots, slot) < 0) return;
            if (item == null) dye = null;
            if (GetWorn(slot) != item || !ReferenceEquals(GetDye(slot), dye))
            {
                if (item == null) worn.Remove(slot); else worn[slot] = item;
                if (dye == null) dyes.Remove(slot); else dyes[slot] = dye;
                paintsStale = true; // a painted piece takes its dye in its paint, not the colour map
                WornChanged?.Invoke();
            }
            IReadOnlyList<SidekickPartRef> parts = item != null ? item.GetAppearanceParts() : null;
            if (parts != null && parts.Count == 0) parts = null;
            if (slotParts.TryGetValue(slot, out var existing) && ReferenceEquals(existing, parts)) return;
            if (parts == null) slotParts.Remove(slot); else slotParts[slot] = parts;
            StopSharing();
            dirty = true;
        }

        /// <summary>
        /// Draw the helmet a worn piece carries in the head attachment, or not (the Equipment page's toggle; helmets are
        /// vanity, not a slot). While one is drawn, the hair shows only what hangs below its rim.
        /// </summary>
        public void SetHelmetShown(bool shown)
        {
            if (helmetShown == shown) return;
            helmetShown = shown;
            StopSharing();
            dirty = true;
        }

        /// <summary>
        /// Wear what another appearance shows (pieces and dyes) as this one's own, merged here: where a try-on
        /// starts from.
        /// </summary>
        public void WearLike(CharacterAppearance source)
        {
            if (source == null || source == this) return;
            helmetShown = source.helmetShown;
            foreach (var slot in VisualSlots) SetSlot(slot, source.GetWorn(slot), source.GetDye(slot));
        }

        /// <summary>Apply every visual slot from a loadout in one rebuild.</summary>
        public void ApplyLoadout(EquipmentLoadout loadout, IItemLookup lookup)
        {
            foreach (var slot in VisualSlots) SetSlot(slot, lookup?.Get(loadout[slot]));
        }

        /// <summary>
        /// Mirror another appearance's merged mesh instead of merging again (same rig, bones matched by name).
        /// Stays in sync until this appearance gets its own <see cref="SetSlot"/> (e.g. a try-on).
        /// </summary>
        public void ShareFrom(CharacterAppearance source)
        {
            if (source == null || source == this) return;
            if (sharedSource != source)
            {
                StopSharing();
                sharedSource = source;
                source.Rebuilt += OnSourceRebuilt;
            }
            slotParts.Clear();
            foreach (var kv in source.slotParts) slotParts[kv.Key] = kv.Value;
            worn.Clear();
            foreach (var kv in source.worn) worn[kv.Key] = kv.Value;
            dyes.Clear();
            foreach (var kv in source.dyes) dyes[kv.Key] = kv.Value;
            WornChanged?.Invoke();
            // The source's own look can change after this clone was made (a recipe applied at runtime): a later
            // try-on must merge from the current look, not the one copied at Instantiate.
            fixedParts.Clear();
            fixedParts.AddRange(source.fixedParts);
            defaultParts.Clear();
            defaultParts.AddRange(source.defaultParts);
            bodyShape = source.bodyShape;
            wrapPart = source.wrapPart;
            helmetShown = source.helmetShown;
            bareBody.Clear();
            bareBody.AddRange(source.bareBody);
            dirty = false;
            OnSourceRebuilt();
        }

        /// <summary>
        /// Keeps more face shapes live on the merged mesh, on top of the blink shapes, so expressions can be blended
        /// at runtime. Display characters use this; players in the world keep the cheaper baked face. The mesh is
        /// rebuilt on the next update.
        /// </summary>
        public void SetLiveFaceShapes(IReadOnlyList<string> shapes)
        {
            var all = new List<string>();
            if (blink) all.AddRange(FaceBlinker.LiveShapes);
            if (shapes != null)
                foreach (string shape in shapes)
                    if (!all.Contains(shape)) all.Add(shape);
            liveFaceShapes = all;
            dirty = IsConfigured;
        }

        /// <summary>
        /// Glowing eyes (the creator's row): the eyes drawn once more in a second material, the character's colour map
        /// <paramref name="colourMap"/> with <paramref name="emissionMap"/> (black but for the eyes) as its emission.
        /// Kept through every rebuild.
        /// </summary>
        public void SetEyeGlow(bool on, Texture colourMap, Texture emissionMap)
        {
            on &= colourMap != null && emissionMap != null;
            if (eyesGlow == on && (!on || (eyeColourMap == colourMap && eyeEmissionMap == emissionMap))) { if (on) RebuildEyeGlow(sharedSource != null ? sharedSource.CurrentParts : partList); return; }
            eyesGlow = on;
            eyeColourMap = colourMap;
            eyeEmissionMap = emissionMap;
            RebuildEyeGlow(sharedSource != null ? sharedSource.CurrentParts : partList);
        }

        private void RebuildEyeGlow(IReadOnlyList<SidekickPartRef> parts)
        {
            if (!eyesGlow || merger == null || targetRenderer == null) { eyeGlow?.Clear(); return; }
            eyeGlow ??= new EyeGlow();
            eyeGlow.Show(parts, eyeColourMap, eyeEmissionMap, eyeGlowIntensity, targetRenderer.transform.parent,
                targetRenderer.gameObject.layer, targetRenderer.rootBone, targetRenderer.localBounds, bodyShape, merger.FindBone);
        }

        public void RebuildNow()
        {
            dirty = false;
            if (merger == null || !IsConfigured) return;
            ResolveParts();
            if (mergeParts) RebuildMerged(); else RebuildSeparate();
            RebuildLights(partList);
            RebuildEyeGlow(partList);
            RebuildCloth(partList);
            BindDangleBones();
            Rebuilt?.Invoke();
        }

        private void LateUpdate()
        {
            if (dirty) RebuildNow();
            else if (paintsStale) ApplyPaints(targetRenderer);
            // a far-away character's cloak hangs as skinned: its cloth stops (the same tier the dangle bones rest at)
            if (cloth != null && cloth.Renderers.Count > 0) cloth.SetSimulated(lod == null || lod.Tier < 2);
        }

        /// <summary>The parts to merge, and the cloth parts kept out of the merge (<see cref="GearCloth"/>).</summary>
        private void SplitCloth(IReadOnlyList<SidekickPartRef> parts)
        {
            mergeList.Clear();
            clothParts.Clear();
            foreach (var p in parts)
            {
                if (GearCloth.TryGetLook(p, out var look)) clothParts.Add((p, look));
                else mergeList.Add(p);
            }
        }

        private void RebuildCloth(IReadOnlyList<SidekickPartRef> parts)
        {
            if (!mergeParts || merger == null || targetRenderer == null) return;
            SplitCloth(parts);
            if (clothParts.Count == 0) { cloth?.Clear(); return; }
            if (lod == null) lod = GetComponentInParent<CharacterLod>();
            cloth ??= new GearCloth();
            cloth.Show(clothParts, targetRenderer.transform.parent, targetRenderer.gameObject.layer, targetRenderer.rootBone,
                targetRenderer.localBounds, bodyShape, merger.FindBone, ClothMaterial);
        }

        /// <summary>A cloth part's material: its paint in its piece's dye, or the character's own colour-mapped material.</summary>
        private Material ClothMaterial(SidekickPartRef part)
        {
            if (GearPaint.TryGetLook(part, out var paint))
            {
                var item = PieceWearing(part, out var slot);
                GearPaint.TryFindColourway(item, GetDye(slot), out var colourway);
                return GearPaint.MaterialFor(paint, colourway);
            }
            return targetRenderer != null ? targetRenderer.sharedMaterial : null;
        }

        /// <summary>
        /// Painted parts' submeshes take their paint, in the dye of the piece they belong to; slot 0 (the body and every
        /// colour-mapped part) keeps the material it has, the character's own (CharacterLook).
        /// </summary>
        private void ApplyPaints(SkinnedMeshRenderer smr)
        {
            paintsStale = false;
            if (smr == null) return;
            materialBuffer.Clear();
            smr.GetSharedMaterials(materialBuffer);
            var body = materialBuffer.Count > 0 ? materialBuffer[0] : null;
            materialBuffer.Clear();
            materialBuffer.Add(body);
            for (int i = 0; i < shownPaints.Count; i++)
            {
                var item = PieceWearing(shownPaintParts[i], out var slot);
                GearPaint.TryFindColourway(item, GetDye(slot), out var colourway);
                materialBuffer.Add(GearPaint.MaterialFor(shownPaints[i], colourway));
            }
            smr.sharedMaterials = materialBuffer.ToArray();
            if (cloth != null)
                foreach (var r in cloth.Renderers)
                {
                    if (r == null) continue;
                    foreach (var (part, _) in clothParts)
                        if (part.mesh == r.sharedMesh)
                        {
                            var m = ClothMaterial(part);
                            var mats = new Material[r.sharedMesh.subMeshCount];
                            for (int i = 0; i < mats.Length; i++) mats[i] = m;
                            r.sharedMaterials = mats;
                        }
                }
        }

        private ItemDefinition PieceWearing(SidekickPartRef part, out EquipSlot slot)
        {
            foreach (var kv in slotParts)
                foreach (var p in kv.Value)
                    if (p.mesh == part.mesh) { slot = kv.Key; return GetWorn(slot); }
            slot = default;
            return null;
        }

        private void ResolveParts()
        {
            resolved.Clear();
            foreach (var p in fixedParts) if (p.IsValid) resolved[p.region] = p;
            foreach (var slot in VisualSlots)
            {
                var regions = EquipSlots.RegionsFor(slot);
                bool worn = slotParts.TryGetValue(slot, out var parts);
                if (worn)
                    foreach (var p in parts) if (p.IsValid) resolved[p.region] = p;
                foreach (var p in defaultParts)
                {
                    if (!p.IsValid || Array.IndexOf(regions, p.region) < 0) continue;
                    // Empty slot: the character's own parts. Worn item: only the body itself falls back, so a set
                    // with fewer leg variants than torsos never leaves a limb missing; attachments stay off.
                    if (!worn || (EquipSlots.IsBodyRegion(p.region) && !resolved.ContainsKey(p.region))) resolved[p.region] = p;
                }
            }
            // A helmet worn with a piece (head attachment): hidden by the toggle, or shown with the hair tucked under it.
            if (resolved.TryGetValue(BodyRegion.AttachmentHead, out var helmet) && !IsFixed(helmet))
            {
                if (!helmetShown) resolved.Remove(BodyRegion.AttachmentHead);
                else
                {
                    if (resolved.TryGetValue(BodyRegion.Hair, out var hair))
                    {
                        if (PartLibrary.TryLoadUnderHelmet(hair, out var tucked)) resolved[BodyRegion.Hair] = tucked;
                        else resolved.Remove(BodyRegion.Hair); // all of this hair sits under the helmet
                    }
                    // A robot set's helmet is the robot's head itself (owner, 24 Oct 2026, Scrap Sentinel): our robot
                    // head pieces (SK_KRYT_*_RO01) stand in for the bare head, which would only poke through.
                    if (IsRobotHeadPiece(helmet)) resolved.Remove(BodyRegion.Head);
                }
            }
            // A bare-chested torso needs the chest wrap on a feminine body (same rule as Sidekick's own tool).
            if (wrapPart.IsValid && bodyShape.masculineFeminine > 50f
                && resolved.TryGetValue(BodyRegion.Torso, out var torso) && torso.usesWrap)
                resolved[BodyRegion.Wrap] = wrapPart;
            partList.Clear();
            partList.AddRange(resolved.Values); // region order => deterministic bone order for sharing
        }

        private static bool IsRobotHeadPiece(SidekickPartRef part)
        {
            string n = part.mesh != null ? part.mesh.name : null;
            return n != null && n.StartsWith("SK_KRYT_", StringComparison.Ordinal) && n.EndsWith("_RO01", StringComparison.Ordinal);
        }

        private bool IsFixed(SidekickPartRef part)
        {
            foreach (var p in fixedParts) if (p.mesh == part.mesh) return true;
            return false;
        }

        private void RebuildMerged()
        {
            ClearDebugRenderers();
            SplitCloth(partList);
            var mesh = merger.Merge(mergeList, bodyShape, boneBuffer, name + "_Merged", idleFace,
                liveFaceShapes != null ? liveFaceShapes : blink ? (IReadOnlyList<string>)FaceBlinker.LiveShapes : null, bareBody);
            if (mesh == null) return;
            var old = ownedMesh;
            ownedMesh = mesh;
            boneNames.Clear();
            foreach (var b in boneBuffer) boneNames.Add(b.name);
            ApplyToRenderer(targetRenderer, mesh, boneBuffer.ToArray());
            shownPaints.Clear();
            shownPaints.AddRange(merger.Paints);
            shownPaintParts.Clear();
            shownPaintParts.AddRange(merger.PaintParts);
            ApplyPaints(targetRenderer);
            if (old != null) Destroy(old);
        }

        private void ApplyToRenderer(SkinnedMeshRenderer smr, Mesh mesh, Transform[] bones)
        {
            smr.enabled = true;
            smr.sharedMesh = null; // force the skinning buffers to rebind to the new bone count
            smr.bones = bones;
            smr.sharedMesh = mesh;
            for (int i = 0; i < mesh.blendShapeCount; i++) smr.SetBlendShapeWeight(i, 0f);
        }

        private void OnSourceRebuilt()
        {
            if (sharedSource == null || sharedSource.CurrentMesh == null || merger == null) return;
            merger.EnsureBones(sharedSource.CurrentParts);
            var names = sharedSource.CurrentBoneNames;
            var bones = new Transform[names.Count];
            for (int i = 0; i < names.Count; i++) bones[i] = merger.FindBone(names[i]) ?? transform;
            ClearDebugRenderers();
            if (ownedMesh != null) { Destroy(ownedMesh); ownedMesh = null; }
            boneNames.Clear();
            boneNames.AddRange(names);
            ApplyToRenderer(targetRenderer, sharedSource.CurrentMesh, bones);
            shownPaints.Clear();
            shownPaints.AddRange(sharedSource.shownPaints);
            shownPaintParts.Clear();
            shownPaintParts.AddRange(sharedSource.shownPaintParts);
            ApplyPaints(targetRenderer);
            RebuildLights(sharedSource.CurrentParts);
            RebuildEyeGlow(sharedSource.CurrentParts);
            RebuildCloth(sharedSource.CurrentParts);
            BindDangleBones();
            Rebuilt?.Invoke();
        }

        // Capes, sashes, long hair and beards swing on their dangle bones (presentation only, never on a server).
        private void BindDangleBones()
        {
            if (mergeParts || debugRenderers.Count == 0) DangleBones.Bind(gameObject, new[] { targetRenderer });
            else DangleBones.Bind(gameObject, debugRenderers);
        }

        private void StopSharing()
        {
            if (sharedSource == null) return;
            sharedSource.Rebuilt -= OnSourceRebuilt;
            sharedSource = null;
        }

        // ---- Debug path: one renderer per part (keeps blendshapes live instead of baking them) ----

        private void RebuildSeparate()
        {
            ClearDebugRenderers();
            targetRenderer.enabled = false;
            foreach (var part in partList)
            {
                var go = new GameObject(part.mesh.name);
                go.layer = targetRenderer.gameObject.layer;
                go.transform.SetParent(targetRenderer.transform.parent, false);
                var smr = go.AddComponent<SkinnedMeshRenderer>();
                var bones = new Transform[part.boneNames.Length];
                for (int i = 0; i < bones.Length; i++) bones[i] = merger.FindBone(part.boneNames[i]) ?? transform;
                smr.bones = bones;
                smr.rootBone = targetRenderer.rootBone;
                smr.sharedMesh = part.mesh;
                var mats = new Material[part.mesh.subMeshCount];
                for (int i = 0; i < mats.Length; i++) mats[i] = targetRenderer.sharedMaterial;
                smr.sharedMaterials = mats;
                smr.localBounds = targetRenderer.localBounds;
                for (int i = 0; i < part.mesh.blendShapeCount; i++)
                {
                    string shapeName = part.mesh.GetBlendShapeName(i);
                    smr.SetBlendShapeWeight(i, bodyShape.WeightFor(shapeName)
                        + FaceShapeWeight.WeightFor(idleFace, FaceShapeWeight.Suffix(shapeName)));
                }
                debugRenderers.Add(smr);
            }
        }

        // ---- Lights: the glowing bits of worn parts (lenses), drawn beside the merged body while their part shows ----

        private void RebuildLights(IReadOnlyList<SidekickPartRef> parts)
        {
            ClearLights();
            if (parts == null || merger == null) return;
            foreach (var part in parts)
            {
                if (!PartLibrary.TryLoadLights(part, out var lights, out var material)) continue;
                var go = new GameObject(lights.mesh.name);
                go.layer = targetRenderer.gameObject.layer;
                go.transform.SetParent(targetRenderer.transform.parent, false);
                var smr = go.AddComponent<SkinnedMeshRenderer>();
                var bones = new Transform[lights.boneNames.Length];
                for (int i = 0; i < bones.Length; i++) bones[i] = merger.FindBone(lights.boneNames[i]) ?? transform;
                smr.bones = bones;
                smr.rootBone = targetRenderer.rootBone;
                smr.sharedMesh = lights.mesh;
                smr.sharedMaterial = material;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                smr.localBounds = targetRenderer.localBounds;
                for (int i = 0; i < lights.mesh.blendShapeCount; i++)
                    smr.SetBlendShapeWeight(i, bodyShape.WeightFor(lights.mesh.GetBlendShapeName(i)));
                lightRenderers.Add(smr);
                // Bloom is off in this game, so the lights glow through the screen-space halo, in their own colour.
                if (lightHalo == null) lightHalo = new GlowHalo();
                lightHalo.Set(new GlowHaloLook
                {
                    color = material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) : material.color,
                    intensity = lightGlowIntensity, width = lightGlowWidth,
                    pulse = lightGlowPulse, pulseSpeed = lightGlowPulseSpeed
                });
                lightHalo.Add(go);
            }
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private void ClearLights()
        {
            lightHalo?.Clear();
            foreach (var r in lightRenderers) if (r != null) Destroy(r.gameObject);
            lightRenderers.Clear();
        }

        private void ClearDebugRenderers()
        {
            foreach (var r in debugRenderers) if (r != null) Destroy(r.gameObject);
            debugRenderers.Clear();
        }

#if UNITY_EDITOR
        /// <summary>Editor setup tool entry point.</summary>
        public void EditorConfigure(SkinnedMeshRenderer renderer, List<SidekickPartRef> fixedList, List<SidekickPartRef> defaults, BodyShapeWeights shape)
        {
            targetRenderer = renderer;
            referenceBoneNames = System.Array.ConvertAll(renderer.bones, b => b != null ? b.name : string.Empty);
            referenceBindposes = renderer.sharedMesh.bindposes;
            fixedParts = fixedList;
            defaultParts = defaults;
            bodyShape = shape;
        }
#endif
    }
}
