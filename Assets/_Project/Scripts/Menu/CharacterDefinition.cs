using UnityEngine;

namespace Domino.Menu
{
    // Step 12.1: mirrors StageDefinition exactly. visualKey names a child GameObject under
    // Player's Visual roots (see Player.PlayerCharacterVisual) - the only thing a new Character
    // needs to plug in is a new Visual child + a new Definition asset pointing at its name, no
    // C# change (Section 5/41: no per-Id hardcoded switch anywhere).
    [CreateAssetMenu(fileName = "CharacterDefinition", menuName = "Domino/Character Definition")]
    public class CharacterDefinition : ScriptableObject
    {
        [SerializeField] private int characterId;
        [SerializeField] private string displayName;
        [SerializeField] private string visualKey;

        public int CharacterId => characterId;
        public string DisplayName => displayName;
        public string VisualKey => visualKey;
    }
}
