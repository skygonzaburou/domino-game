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

        [Tooltip("未設定(None)ならPlayerCharacterVisual側のDefault Material(GreenBoy_Material)がそのまま使われる。GreenBoyVisualの色違いなど、共有ModelのMaterialだけを差し替えたい場合に設定する")]
        [SerializeField] private Material visualMaterial;

        public int CharacterId => characterId;
        public string DisplayName => displayName;
        public string VisualKey => visualKey;
        public Material VisualMaterial => visualMaterial;
    }
}
