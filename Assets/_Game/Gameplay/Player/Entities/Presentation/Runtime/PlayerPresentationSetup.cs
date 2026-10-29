using Unity.Entities;
using World = Unity.Entities.World;

namespace KryticalNova.Player.Entities.Presentation
{
    /// <summary>
    /// The seam that wires the managed pieces of the player presentation into a client world: the body factory and, optionally, the
    /// click source. Like <see cref="PlayerEntitiesSetup"/>, no state of its own and no statics. Worlds without the matching system are left alone.
    /// </summary>
    public static class PlayerPresentationSetup
    {
        /// <param name="clientWorld">A client simulation world.</param>
        /// <param name="factory">Makes the body of each player ghost (required for bodies to appear).</param>
        /// <param name="clicks">Where the owner's clicks come from; null keeps whatever command source the gather already has.</param>
        /// <param name="registerLocalPlayer">False keeps this world's owner out of Core's LocalPlayer (tests with several client worlds).</param>
        public static void Install(World clientWorld, IPlayerBodyFactory factory, IClickSource clicks = null, bool registerLocalPlayer = true)
        {
            var spawn = clientWorld.GetExistingSystemManaged<PlayerBodySpawnSystem>();
            if (spawn != null)
            {
                spawn.Factory = factory;
                spawn.RegisterLocalPlayer = registerLocalPlayer;
            }
            if (clicks != null)
            {
                var gather = clientWorld.GetExistingSystemManaged<PlayerInputGatherSystem>();
                if (gather != null) gather.Source = new ClickCommandSource(clicks);
            }
        }
    }
}
