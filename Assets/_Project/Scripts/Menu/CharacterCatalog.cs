using System.Collections.Generic;
using UnityEngine;

namespace Domino.Menu
{
    // Step 12.1: mirrors StageCatalog exactly.
    [CreateAssetMenu(fileName = "CharacterCatalog", menuName = "Domino/Character Catalog")]
    public class CharacterCatalog : ScriptableObject
    {
        [SerializeField] private List<CharacterDefinition> characters = new List<CharacterDefinition>();

        public IReadOnlyList<CharacterDefinition> Characters => characters;

        public CharacterDefinition FindById(int characterId)
        {
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i] != null && characters[i].CharacterId == characterId) return characters[i];
            }
            return null;
        }
    }
}
