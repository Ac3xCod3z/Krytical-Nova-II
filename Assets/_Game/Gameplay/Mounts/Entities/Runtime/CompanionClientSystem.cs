using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Player.Entities.Presentation;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace KryticalNova.Mounts.Entities
{
    /// <summary>
    /// Client (day list item 19): every Mount Companion ghost is drawn by a <see cref="CompanionPresenter"/> on every view (the owner's own
    /// screen too: it is interpolated for everyone); the own view's Mount window gets its companion boxes from
    /// <see cref="MountCompanionBookRpc"/>. Presentation only.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CompanionClientSystem : SystemBase
    {
        EntityQuery m_Books, m_Ghosts;
        readonly Dictionary<Entity, CompanionPresenter> m_Shown = new Dictionary<Entity, CompanionPresenter>();
        readonly List<Entity> m_Gone = new List<Entity>();

        /// <summary>Presenters made (tests).</summary>
        public int Made { get; private set; }
        public int ShownCount => m_Shown.Count;
        public bool TryGetShown(Entity ghost, out CompanionPresenter presenter) => m_Shown.TryGetValue(ghost, out presenter);

        protected override void OnCreate()
        {
            m_Books = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<MountCompanionBookRpc>());
            m_Ghosts = GetEntityQuery(ComponentType.ReadOnly<CompanionView>(), ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<GhostInstance>());
        }

        protected override void OnDestroy()
        {
            foreach (CompanionPresenter p in m_Shown.Values) if (p != null) Object.Destroy(p.gameObject);
            m_Shown.Clear();
        }

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            if (!m_Books.IsEmpty)
            {
                using var books = m_Books.ToComponentDataArray<MountCompanionBookRpc>(Allocator.Temp);
                em.DestroyEntity(m_Books);
                IBodyMountCompanion own = null;
                foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
                    if (ReferenceEquals(entry.World, World) && entry.IsLocal && entry.Body != null && entry.Body.TryGetComponent(out own)) break;
                foreach (MountCompanionBookRpc b in books)
                    own?.MirrorCompanion(b.Out, b.Stance, b.Hp1, b.Hp2, b.Hp3, b.Hp4, b.Rest1, b.Rest2, b.Rest3, b.Rest4);
            }

            using var ghosts = m_Ghosts.ToEntityArray(Allocator.Temp);
            using var views = m_Ghosts.ToComponentDataArray<CompanionView>(Allocator.Temp);
            using var places = m_Ghosts.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < ghosts.Length; i++)
            {
                CompanionView view = views[i];
                if (!m_Shown.TryGetValue(ghosts[i], out CompanionPresenter p) || p == null)
                {
                    if (view.State != (byte)CompanionState.Out || !TryOwner(em, view.OwnerGhostId, out PlayerMounts mounts, out bool ownerIsLocal) || mounts.Catalog == null) continue;
                    MountDefinition mount = mounts.Catalog.Get(view.Box);
                    if (mount == null) continue;
                    p = CompanionPresenter.Make(mount, mounts.Settings, view.Stage, mounts.gameObject.layer, places[i].Position, places[i].Rotation, view, mounts.transform, ownerIsLocal);
                    m_Shown[ghosts[i]] = p;
                    Made++;
                }
                p.Drive(places[i].Position, places[i].Rotation, view);
            }

            m_Gone.Clear();
            foreach (KeyValuePair<Entity, CompanionPresenter> kv in m_Shown)
                if (!em.Exists(kv.Key) || !em.HasComponent<CompanionView>(kv.Key)) m_Gone.Add(kv.Key);
            foreach (Entity ghost in m_Gone)
            {
                if (m_Shown[ghost] != null) m_Shown[ghost].Release();
                m_Shown.Remove(ghost);
            }
        }

        bool TryOwner(EntityManager em, int ownerGhostId, out PlayerMounts mounts, out bool isLocal)
        {
            mounts = null;
            isLocal = false;
            if (ownerGhostId == 0) return false;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !em.Exists(entry.Ghost) || !em.HasComponent<GhostInstance>(entry.Ghost)) continue;
                if (em.GetComponentData<GhostInstance>(entry.Ghost).ghostId != ownerGhostId) continue;
                isLocal = entry.IsLocal;
                return entry.Body.TryGetComponent(out mounts);
            }
            return false;
        }
    }
}
