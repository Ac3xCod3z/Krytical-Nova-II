using KryticalNova.CharacterCreation;
using KryticalNova.Player;
using KryticalNova.Player.Entities.Bodies;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// Client: the look text of a player (<see cref="RecipeText.EncodeLook"/>) on its ClientView body, through
    /// <see cref="PlayerIdentity.ApplyView"/> (look, name, faction; no kit, no save). A text it cannot read leaves the body as it is.
    /// </summary>
    public sealed class IdentityLookApplier : ILookApplier
    {
        public int Applied { get; private set; }
        public int Unreadable { get; private set; }

        public void Apply(GameObject body, string lookText)
        {
            if (body == null) return;
            var identity = body.GetComponent<PlayerIdentity>();
            if (identity == null) return;
            if (!RecipeText.TryDecodeLook(lookText, out string name, out CharacterRecipe recipe))
            {
                Unreadable++;
                Debug.LogWarning("[KnClient] a player's look could not be read; the body keeps the one it has.", body);
                return;
            }
            identity.ApplyView(CharacterNetState.From(name, recipe));
            Applied++;
        }
    }
}
