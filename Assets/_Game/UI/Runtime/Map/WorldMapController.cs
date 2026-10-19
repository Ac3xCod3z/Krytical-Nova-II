using System.Collections.Generic;
using KryticalNova.Items;
using KryticalNova.NPC;
using KryticalNova.Player;
using KryticalNova.World;
using KryticalNova.World.Minimap;
using KryticalNova.World.Portals;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using KryticalNova.Core;

namespace KryticalNova.UI
{
    /// <summary>
    /// World map window (WorldMap.uxml, instanced in the HUD document). Shows the zone's baked top-down map north-up
    /// with the local player's triangle at its true position (same world → UV mapping as the minimap), plus points
    /// of interest, NPCs, portals and other players. Scroll zooms about the cursor, drag pans, right-click places the
    /// player's own marker (<see cref="MapWaypoint"/>), which the minimap shows too. Hovering a place, NPC, portal
    /// or the marker names it; double-clicking it leads the player there (<see cref="MapTravel"/>) and draws the
    /// route. The window is a
    /// <c>.hud-movable</c>, so its place and scale come from the HUD layout editor like every other panel.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class WorldMapController : MonoBehaviour
    {
        private const float MinZoom = 1f;
        private const float MaxZoom = 4f;
        private const float ZoomPerWheelStep = 1.25f;
        private const float InitRetryInterval = 1f;
        private const float PlayerIconSize = 26f;
        private const int LeftButton = 0;
        private const int RightButton = 1;
        private const float WaypointIconSize = 26f;
        private const float RouteLineWidth = 3f;
        private const float HoverPadding = 5f;   // pixels beyond an icon's edge that still count as over it

        private VisualElement root, window, viewport, content, playerIcon;
        private Label zoneLabel, coordsLabel;
        private Button openButton, closeButton, centerButton;
        private ZoneMinimapData data;
        private float nextInitAttempt;
        private bool initialized;

        private float zoom = MinZoom;
        private Vector2 pan;            // content top-left inside the viewport, in viewport pixels
        private bool followPlayer = true;
        private int panPointerId = -1;
        private Vector2 panLast;
        private MapHoverLabel hoverLabel;
        private MapRouteLine routeLine;
        private readonly List<MapTarget> targets = new List<MapTarget>();

        private readonly List<VisualElement> poiMarkers = new List<VisualElement>();
        private readonly List<VisualElement> npcMarkers = new List<VisualElement>();
        private readonly List<VisualElement> allyMarkers = new List<VisualElement>();
        private readonly List<VisualElement> portalMarkers = new List<VisualElement>();
        private readonly List<VisualElement> waypointMarkers = new List<VisualElement>();

        private bool IsOpen => window != null && window.resolvedStyle.display != DisplayStyle.None;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            TryInitialize();
        }

        /// <summary>The HUD document can build its tree after this component enables; retry until it exists.</summary>
        private void TryInitialize()
        {
            nextInitAttempt = Time.unscaledTime + InitRetryInterval;
            root = GetComponent<UIDocument>().rootVisualElement;
            window = root?.Q("world-map");
            if (window == null) return;
            viewport = window.Q("world-map-viewport");
            content = window.Q("world-map-content");
            zoneLabel = window.Q<Label>("world-map-zone");
            coordsLabel = window.Q<Label>("world-map-coords");
            openButton = root.Q<Button>("open-map");
            closeButton = window.Q<Button>("close-world-map");
            centerButton = window.Q<Button>("world-map-center");

            playerIcon = new VisualElement { pickingMode = PickingMode.Ignore };
            playerIcon.AddToClassList("world-map-player");
            playerIcon.Add(Layer("world-map-player-cone"));
            playerIcon.Add(Layer("world-map-player-icon"));
            content.Add(playerIcon);
            routeLine = new MapRouteLine(RouteLineWidth);
            content.Insert(0, routeLine); // under the markers
            hoverLabel = new MapHoverLabel();
            root.Add(hoverLabel);

            if (openButton != null) openButton.clicked += Toggle;
            closeButton.clicked += Close;
            centerButton.clicked += CenterOnPlayer;
            viewport.RegisterCallback<WheelEvent>(OnWheel);
            viewport.RegisterCallback<PointerDownEvent>(OnPointerDown);
            viewport.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            viewport.RegisterCallback<PointerUpEvent>(OnPointerUp);
            viewport.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            viewport.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
            ZoneMinimapAnchor.CurrentChanged += OnZoneChanged;

            initialized = true;
            OnZoneChanged();
        }

        private void OnDisable()
        {
            if (!initialized) return;
            initialized = false;
            ZoneMinimapAnchor.CurrentChanged -= OnZoneChanged;
            if (openButton != null) openButton.clicked -= Toggle;
            closeButton.clicked -= Close;
            centerButton.clicked -= CenterOnPlayer;
            viewport.UnregisterCallback<WheelEvent>(OnWheel);
            viewport.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            viewport.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            viewport.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            viewport.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            viewport.UnregisterCallback<PointerLeaveEvent>(OnPointerLeave);
            playerIcon?.RemoveFromHierarchy();
            routeLine?.RemoveFromHierarchy();
            hoverLabel?.RemoveFromHierarchy();
        }

        private static VisualElement Layer(string className)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList(className);
            return e;
        }

        private void OnZoneChanged()
        {
            data = ZoneMinimapAnchor.Current;
            bool hasMap = data != null && data.map != null;
            content.style.backgroundImage = hasMap ? Background.FromTexture2D(data.map) : StyleKeyword.None;
            zoneLabel.text = SceneManager.GetActiveScene().name.Replace('_', ' ').ToUpperInvariant();
            if (!hasMap) Close();
        }

        // ------------------------------------------------------------------ open / close

        private void Toggle() => SetOpen(!IsOpen);
        private void Close() => SetOpen(false);

        /// <summary>Opens the map (the minimap does this on a double-click).</summary>
        public void Open() => SetOpen(true);

        private void SetOpen(bool open)
        {
            if (!initialized || open == IsOpen) return;
            if (open && (data == null || data.map == null)) return; // this zone has no baked map
            window.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            window.EnableInClassList("visible", open);
            if (!open) { ReleasePan(); hoverLabel?.Hide(); return; }
            window.BringToFront();
            CenterOnPlayer();
            Transform player = LocalPlayer();
            if (player != null && player.TryGetComponent(out PointAndClickPlayer movement)) movement.ReleaseCameraControl();
        }

        private void Update()
        {
            if (!initialized)
            {
                if (Application.isPlaying && Time.unscaledTime >= nextInitAttempt) TryInitialize();
                return;
            }
            var keyboard = Keyboard.current;
            if (keyboard == null || IsTyping()) return;
            if (KeyBindings.Pressed(KeyBindings.Map)) Toggle();
            else if (keyboard.escapeKey.wasPressedThisFrame && IsOpen) Close();
        }

        private bool IsTyping()
        {
            var focused = root.panel?.focusController?.focusedElement as VisualElement;
            for (VisualElement e = focused; e != null; e = e.parent)
                if (e is TextField) return true;
            return false;
        }

        // ------------------------------------------------------------------ zoom & pan

        private void CenterOnPlayer()
        {
            followPlayer = true;
        }

        private void OnWheel(WheelEvent evt)
        {
            evt.StopPropagation();
            float size = ViewportSize();
            if (size <= 0f || Mathf.Approximately(evt.delta.y, 0f)) return;
            float next = Mathf.Clamp(evt.delta.y < 0f ? zoom * ZoomPerWheelStep : zoom / ZoomPerWheelStep, MinZoom, MaxZoom);
            if (Mathf.Approximately(next, zoom)) return;

            // Keep the map point under the cursor fixed while the content grows or shrinks.
            Vector2 cursor = viewport.WorldToLocal(evt.mousePosition);
            Vector2 anchor = (cursor - pan) / (size * zoom);
            zoom = next;
            pan = cursor - anchor * (size * zoom);
            followPlayer = false;
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button == RightButton)
            {
                evt.StopPropagation();
                ToggleWaypoint(viewport.WorldToLocal(evt.position));
                return;
            }
            if (evt.button != LeftButton) return;
            evt.StopPropagation();
            // A double-click on something named leads the player there instead of starting a drag.
            if (evt.clickCount >= 2 && TryPick(viewport.WorldToLocal(evt.position), out MapTarget target))
            {
                ReleasePan();
                MapTravel.Start(target);
                return;
            }
            panPointerId = evt.pointerId;
            panLast = viewport.WorldToLocal(evt.position);
            viewport.CapturePointer(evt.pointerId);
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != panPointerId)
            {
                // Not dragging: name whatever is under the pointer.
                if (panPointerId < 0 && TryPick(viewport.WorldToLocal(evt.position), out MapTarget hovered)) hoverLabel.Show(hovered.Name, evt.position);
                else hoverLabel.Hide();
                return;
            }
            hoverLabel.Hide();
            Vector2 local = viewport.WorldToLocal(evt.position);
            Vector2 delta = local - panLast;
            panLast = local;
            if (delta.sqrMagnitude <= 0f) return;
            pan += delta;
            followPlayer = false;
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId != panPointerId) return;
            evt.StopPropagation();
            ReleasePan();
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent evt) => panPointerId = -1;

        private void OnPointerLeave(PointerLeaveEvent evt) => hoverLabel.Hide();

        /// <summary>The named thing under a point of the viewport: the nearest whose icon the point is on.</summary>
        private bool TryPick(Vector2 cursor, out MapTarget picked)
        {
            picked = default;
            float contentSize = ViewportSize() * zoom;
            if (data == null || contentSize <= 0f) return false;

            Transform player = LocalPlayer();
            MapTargets.Collect(targets, player != null ? player.position : Vector3.zero, places: true);
            float best = float.MaxValue;
            foreach (MapTarget target in targets)
            {
                float reach = IconSize(target.Kind) * 0.5f + HoverPadding;
                float sqr = (MapPixel(target.Position, contentSize) + pan - cursor).sqrMagnitude;
                if (sqr > reach * reach || sqr >= best) continue;
                best = sqr;
                picked = target;
            }
            return best < float.MaxValue;
        }

        // Icon sizes as RefreshMarkers places them.
        private static float IconSize(MapTarget.TargetKind kind) =>
            kind == MapTarget.TargetKind.Npc ? 10f : kind == MapTarget.TargetKind.Portal ? 24f
            : kind == MapTarget.TargetKind.Place ? 14f : WaypointIconSize;

        private void ReleasePan()
        {
            if (panPointerId >= 0 && viewport.HasPointerCapture(panPointerId)) viewport.ReleasePointer(panPointerId);
            panPointerId = -1;
        }

        /// <summary>Right-click: drops the player's marker under the cursor, or removes it when the click lands on it.</summary>
        private void ToggleWaypoint(Vector2 cursor)
        {
            float contentSize = ViewportSize() * zoom;
            if (data == null || contentSize <= 0f) return;

            if (MapWaypoint.TryGet(out Vector3 existing)
                && (MapPixel(existing, contentSize) + pan - cursor).sqrMagnitude <= WaypointIconSize * WaypointIconSize * 0.25f)
            {
                MapWaypoint.Clear();
                return;
            }

            Vector2 px = cursor - pan;
            Vector2 uv = new Vector2(px.x / contentSize, 1f - px.y / contentSize);
            if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f) return;
            Transform player = LocalPlayer();
            MapWaypoint.Set(data.UvToWorld(uv, player != null ? player.position.y : 0f));
        }

        private float ViewportSize()
        {
            float size = viewport.layout.width;
            return float.IsNaN(size) ? 0f : size;
        }

        // ------------------------------------------------------------------ per frame

        private void LateUpdate()
        {
            if (!initialized || data == null || !IsOpen) return;
            float size = ViewportSize();
            if (size <= 0f) return;
            float contentSize = size * zoom;

            Transform player = LocalPlayer();
            Vector2 playerPx = player != null ? MapPixel(player.position, contentSize) : new Vector2(contentSize, contentSize) * 0.5f;

            if (followPlayer) pan = new Vector2(size, size) * 0.5f - playerPx;
            // The map always covers the viewport: at 1x it fits exactly, zoomed in it can slide but never show a gap.
            pan.x = Mathf.Clamp(pan.x, size - contentSize, 0f);
            pan.y = Mathf.Clamp(pan.y, size - contentSize, 0f);

            content.style.width = contentSize;
            content.style.height = contentSize;
            content.style.translate = new Translate(pan.x, pan.y, 0f);

            playerIcon.style.display = player != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (player != null)
            {
                playerIcon.style.left = playerPx.x - PlayerIconSize * 0.5f;
                playerIcon.style.top = playerPx.y - PlayerIconSize * 0.5f;
                playerIcon.style.rotate = new Rotate(FacingYaw(player)); // map is north-up, so yaw maps straight to screen rotation
                coordsLabel.text = $"X {player.position.x:0}  ·  Z {player.position.z:0}";
            }

            RefreshMarkers(player, contentSize);

            routeLine.Begin();
            foreach (Vector3 corner in MapTravel.Route()) routeLine.Add(MapPixel(corner, contentSize));
            routeLine.Apply();
        }

        /// <summary>World position → pixel inside the content element (origin top-left, +Z up).</summary>
        private Vector2 MapPixel(Vector3 world, float contentSize)
        {
            Vector2 uv = data.WorldToUv(world);
            return new Vector2(uv.x * contentSize, (1f - uv.y) * contentSize);
        }

        private static Transform LocalPlayer()
        {
            var inv = PlayerInventory.Local;
            if (inv != null && inv.isActiveAndEnabled) return inv.transform;
            // Before the networked player spawns (or offline), fall back to the locally controlled avatar.
            foreach (var p in PointAndClickPlayer.Active)
                if (p != null && p.isActiveAndEnabled && (!p.IsSpawned || p.IsOwner)) return p.transform;
            return null;
        }

        private static float FacingYaw(Transform player) =>
            player.TryGetComponent(out PointAndClickPlayer pc) ? pc.FacingYaw : player.eulerAngles.y;

        // ------------------------------------------------------------------ markers (pooled)

        private void RefreshMarkers(Transform player, float contentSize)
        {
            int poiCount = 0;
            foreach (var poi in PointOfInterest.Active)
            {
                if (poi == null) continue;
                var marker = Pooled(poiMarkers, poiCount++, "poi", withLabel: true);
                marker.Q<Label>().text = poi.DisplayName.ToUpperInvariant();
                Place(marker, MapPixel(poi.transform.position, contentSize), 14f);
            }
            Hide(poiMarkers, poiCount);

            int npcCount = 0;
            foreach (var npc in NpcInteractable.Active)
                if (npc != null) Place(Pooled(npcMarkers, npcCount++, "npc"), MapPixel(npc.Position, contentSize), 10f);
            Hide(npcMarkers, npcCount);

            int allyCount = 0;
            foreach (var p in PointAndClickPlayer.Active)
            {
                if (p == null || p.transform == player) continue;
                var marker = Pooled(allyMarkers, allyCount++, "ally");
                Place(marker, MapPixel(p.transform.position, contentSize), 16f);
                marker.style.rotate = new Rotate(p.FacingYaw);
            }
            Hide(allyMarkers, allyCount);

            int portalCount = 0;
            foreach (var portal in ZonePortal.Active)
                if (portal != null) Place(Pooled(portalMarkers, portalCount++, "portal"), MapPixel(portal.transform.position, contentSize), 24f);
            Hide(portalMarkers, portalCount);

            int waypointCount = 0;
            if (MapWaypoint.TryGet(out Vector3 waypoint))
                Place(Pooled(waypointMarkers, waypointCount++, "waypoint"), MapPixel(waypoint, contentSize), WaypointIconSize);
            Hide(waypointMarkers, waypointCount);

            playerIcon.BringToFront();
        }

        private VisualElement Pooled(List<VisualElement> pool, int index, string kind, bool withLabel = false)
        {
            if (index < pool.Count) return pool[index];
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList("world-map-marker");
            e.AddToClassList(kind);
            if (withLabel)
            {
                var label = new Label { pickingMode = PickingMode.Ignore };
                label.AddToClassList("world-map-poi-label");
                e.Add(label);
            }
            content.Add(e);
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
