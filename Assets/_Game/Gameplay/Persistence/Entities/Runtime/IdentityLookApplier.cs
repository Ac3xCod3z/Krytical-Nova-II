using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Items;
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
            if (!RecipeText.TryDecodeLook(lookText, out string name, out CharacterRecipe recipe, out string gear))
            {
                Unreadable++;
                Debug.LogWarning("[KnClient] a player's look could not be read; the body keeps the one it has.", body);
                return;
            }
            identity.ApplyView(CharacterNetState.From(name, recipe));
            // what they wear (M8 8.1.2b); the owner's own body wears its mirror (the Pack's worn cells) instead
            if (gear != null && !BodyRoleTag.IsLocalClientView(body.transform) && body.TryGetComponent(out PlayerEquipment equipment)
                && GearText.TryDecode(gear, out EquipmentLoadout loadout, out EquipmentLooks looks, out EquipmentTemper temper))
                equipment.ApplyObserved(loadout, looks, temper);
            // the own view wears its worn pieces from the Pack mirror; its Weaves, Morph and toggles come from here (M8 8.3.9)
            else if (gear != null && BodyRoleTag.IsLocalClientView(body.transform) && body.TryGetComponent(out PlayerEquipment own)
                && GearText.TryDecode(gear, out _, out EquipmentLooks ownLooks, out _))
                own.MirrorLooks(ownLooks);
            Applied++;
        }
    }
}
