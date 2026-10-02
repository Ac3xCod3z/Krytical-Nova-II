using System.Collections.Generic;
using KryticalNova.Items;
using KryticalNova.NPC;
using KryticalNova.Player;
using KryticalNova.World.Minimap;
using KryticalNova.World.Portals;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// HUD minimap. Performance by design: no camera and no RenderTexture. The zone is baked once into a texture
    /// (Krytical Nova/World/Bake Minimap) and each frame only three style transforms change: the map layer's
    /// translate/rotate (markers are its children, so they ride along for free) and the arrow's rotate.
    /// Marker positions refresh at 5 Hz from pooled elements, so nothing allocates per frame.
    /// Orientation: rotates with the camera, or fixed north-up (Settings → HudPreferences.MinimapRotates).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class MinimapController : MonoBehaviour
    {
        private static readonly float[] ZoomLevels = { 1.4f, 2.4f, 4f }; // UI pixels per metre
        private const float MarkerRefreshInterval = 0.2f;
        private const float EdgeInset = 9f; // portals far away stick to the rim instead of vanishing

        private VisualElement root, minimap, mask, map, north;
        private MinimapArrow arrow;
        private ZoneMinimapData data;
        private int zoomIndex = 1;
        private float nextMarkerRefresh;
        private bool initialized;

        private readonly List<VisualElement> npcMarkers = new List<VisualElement>();
        private readonly List<VisualElement> allyMarkers = new List<VisualElement>();
        private readonly List<VisualElement> portalMarkers = new List<VisualElement>();
        // Edge-clamped markers keep their counter-rotation in sync every frame (they don't move with the map).
        private float currentMapRotation;

        private float PixelsPerMetre => ZoomLevels[zoomIndex];
        private float MapPixels => data != null ? data.worldSize * PixelsPerMetre : 0f;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            minimap = root?.Q("minimap");
            if (minimap == null) return;
            mask = minimap.Q("minimap-mask");
            map = minimap.Q("minimap-map");
            north = minimap.Q("minimap-north");
            arrow = new MinimapArrow();
            minimap.Insert(minimap.IndexOf(minimap.Q(className: "minimap-ring")), arrow);

            minimap.Q<Button>("minimap-zoom-in").clicked += ZoomIn;
            minimap.Q<Button>("minimap-zoom-out").clicked += ZoomOut;
            minimap.RegisterCallback<WheelEvent>(OnWheel);
            ZoneMinimapAnchor.CurrentChanged += OnZoneChanged;
            initialized = true;
            OnZoneChanged();
        }

        private void OnDisable()
        {
            if (!initialized) return;
            initialized = false;
            ZoneMinimapAnchor.CurrentChanged -= OnZoneChanged;
            minimap.Q<Button>("minimap-zoom-in").clicked -= ZoomIn;
            minimap.Q<Button>("minimap-zoom-out").clicked -= ZoomOut;
            minimap.UnregisterCallback<WheelEvent>(OnWheel);
            arrow?.RemoveFromHierarchy();
        }

        private void OnZoneChanged()
        {
            data = ZoneMinimapAnchor.Current;
            bool show = data != null && data.map != null;
            minimap.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            map.style.backgroundImage = Background.FromTexture2D(data.map);
            ApplyZoom();
        }

        private void ZoomIn() { zoomIndex = Mathf.Min(zoomIndex + 1, ZoomLevels.Length - 1); ApplyZoom(); }
        private void ZoomOut() { zoomIndex = Mathf.Max(zoomIndex - 1, 0); ApplyZoom(); }

        private void OnWheel(WheelEvent evt)
        {
            if (evt.delta.y < 0f) ZoomIn(); else if (evt.delta.y > 0f) ZoomOut();
            evt.StopPropagation();
        }

        private void ApplyZoom()
        {
            if (data == null) return;
            map.style.width = MapPixels;
            map.style.height = MapPixels;
            nextMarkerRefresh = 0f; // marker map-pixel positions depend on zoom
        }

        // ------------------------------------------------------------------ per frame

        private void LateUpdate()
        {
            if (!initialized || data == null || minimap.resolvedStyle.display == DisplayStyle.None) return;
            Transform player = LocalPlayer();
            if (player == null) return;

            float size = mask.layout.width;
            if (float.IsNaN(size) || size <= 0f) return;
            float half = size * 0.5f;

            Vector2 playerPx = MapPixel(player.position);
            float cameraYaw = CameraYaw();
            bool rotates = HudPreferences.MinimapRotates;
            currentMapRotation = rotates ? -cameraYaw : 0f;

            // Rotate the map around the player's point, then slide that point to the mask centre.
            map.style.transformOrigin = new TransformOrigin(playerPx.x, playerPx.y, 0f);
            map.style.translate = new Translate(half - playerPx.x, half - playerPx.y, 0f);
            map.style.rotate = new Rotate(currentMapRotation);

            float playerYaw = player.eulerAngles.y;
            arrow.style.rotate = new Rotate(rotates ? playerYaw - cameraYaw : playerYaw);

            PlaceNorth(currentMapRotation);

            if (Time.unscaledTime >= nextMarkerRefresh)
            {
                nextMarkerRefresh = Time.unscaledTime + MarkerRefreshInterval;
                RefreshMarkers(player, playerPx, half);
            }
            // Keep icons upright while the map spins under them.
            foreach (var m in portalMarkers) if (m.style.display != DisplayStyle.None) m.style.rotate = new Rotate(-currentMapRotation);
        }

        private Vector2 MapPixel(Vector3 world)
        {
            Vector2 uv = data.WorldToUv(world);
            float s = MapPixels;
            return new Vector2(uv.x * s, (1f - uv.y) * s);
        }

        private static float CameraYaw()
        {
            var cam = Camera.main;
            if (cam == null) return 0f;
            Vector3 f = cam.transform.forward;
            f.y = 0f;
            return f.sqrMagnitude < 1e-4f ? cam.transform.eulerAngles.y : Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }

        private static Transform LocalPlayer()
        {
            var inv = PlayerInventory.Local;
            return inv != null && inv.isActiveAndEnabled ? inv.transform : null;
        }

        private void PlaceNorth(float mapRotation)
        {
            float r = minimap.layout.width * 0.5f - 8f;
            float a = mapRotation * Mathf.Deg2Rad; // north is "up" on the map, so it turns with the map
            float cx = minimap.layout.width * 0.5f + Mathf.Sin(a) * r;
            float cy = minimap.layout.height * 0.5f - Mathf.Cos(a) * r;
            north.style.left = cx - 10f;
            north.style.top = cy - 10f;
        }

        // ------------------------------------------------------------------ markers (5 Hz, pooled)

        private void RefreshMarkers(Transform player, Vector2 playerPx, float maskHalf)
        {
            int npcCount = 0;
            foreach (var npc in NpcInteractable.Active)
                if (npc != null) Place(Pooled(npcMarkers, npcCount++, "npc"), MapPixel(npc.Position), 9f);
            Hide(npcMarkers, npcCount);

            int allyCount = 0;
            foreach (var p in PointAndClickPlayer.Active)
                if (p != null && p.transform != player) Place(Pooled(allyMarkers, allyCount++, "ally"), MapPixel(p.transform.position), 9f);
            Hide(allyMarkers, allyCount);

            int portalCount = 0;
            foreach (var portal in ZonePortal.Active)
            {
                if (portal == null) continue;
                Vector2 px = MapPixel(portal.transform.position);
                // Clamp to the rim so the way onward is always visible.
                Vector2 offset = px - playerPx;
                float maxDist = maskHalf - EdgeInset;
                if (offset.magnitude > maxDist) px = playerPx + offset.normalized * maxDist;
                Place(Pooled(portalMarkers, portalCount++, "portal"), px, 22f);
            }
            Hide(portalMarkers, portalCount);
        }

        private VisualElement Pooled(List<VisualElement> pool, int index, string kind)
        {
            if (index < pool.Count) return pool[index];
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList("minimap-marker");
            e.AddToClassList(kind);
            map.Add(e);
            pool.Add(e);
            return e;
        }

        private static void Place(VisualElement e, Vector2 px, float size)
        {
            e.style.display = DisplayStyle.Flex;
            e.style.left = px.x - size * 0.5f;
            e.style.top = px.y - size * 0.5f;
        }

        private static void Hide(List<VisualElement> pool, int fromIndex)
        {
            for (int i = fromIndex; i < pool.Count; i++) pool[i].style.display = DisplayStyle.None;
        }
    }
}
