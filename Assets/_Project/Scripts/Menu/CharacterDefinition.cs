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

        // Step 13.2: Character Select Card表示専用のUI Data。Networkには一切使用しない
        // (Networkで同期されるのは既存のCharacterIdのみ - Section 3/4)。未設定(None)の場合は
        // CharacterSelectController側のPlaceholder Spriteが代わりに使われるため、このFieldを
        // 設定し忘れても見た目が壊れることはない(Section 13)。
        [Tooltip("Character Select Cardに表示するPortrait画像(UI表示専用、Network同期しない)。未設定ならCharacterSelectController側のPlaceholderが使われる")]
        [SerializeField] private Sprite portrait;

        public int CharacterId => characterId;
        public string DisplayName => displayName;
        public string VisualKey => visualKey;
        public Material VisualMaterial => visualMaterial;
        public Sprite Portrait => portrait;
    }
}
