using KryticalNova.CharacterCreation;
using KryticalNova.Networking.Entities;
using World = Unity.Entities.World;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// Client: the character the player chose on the select screen (<see cref="CharacterSession"/>) as the go-in-game request (M3a-2b):
    /// its id, its name and its look as <see cref="RecipeText"/>. Nothing to send (no character chosen) = the client waits. The account
    /// is never sent: the server takes the one it proved at the join.
    /// </summary>
    public sealed class CharacterSessionGoInGame : IKnGoInGameSource
    {
        public bool TryGet(World clientWorld, out GoInGamePayload payload)
        {
            payload = default;
            if (!CharacterSession.TryGetActive(out CharacterProfile profile) || profile == null || !profile.IsUsed) return false;
            payload = new GoInGamePayload
            {
                CharacterId = profile.id ?? string.Empty,
                Name = profile.name ?? string.Empty,
                RecipeText = RecipeText.Encode(profile.recipe),
            };
            return true;
        }
    }
}
