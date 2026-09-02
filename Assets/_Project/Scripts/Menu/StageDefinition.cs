using UnityEngine;

namespace Domino.Menu
{
    [CreateAssetMenu(fileName = "StageDefinition", menuName = "Domino/Stage Definition")]
    public class StageDefinition : ScriptableObject
    {
        [SerializeField] private int stageId;
        [SerializeField] private string displayName;
        [SerializeField] private string gameplaySceneName;

        // Step 13.3: Stage Select Card表示専用のUI Data。CharacterDefinition.Portrait(Step 13.2)
        // と同じ設計思想 - GameplaySceneNameとは完全に独立していて、Gameplay Scene参照には一切
        // 使用しない。未設定(None)ならStageSelectController側のPlaceholder Spriteが代わりに使われる。
        [Tooltip("Stage Select Cardに表示するThumbnail画像(UI表示専用、GameplaySceneNameとは無関係)。未設定ならStageSelectController側のPlaceholderが使われる")]
        [SerializeField] private Sprite thumbnail;

        public int StageId => stageId;
        public string DisplayName => displayName;
        public string GameplaySceneName => gameplaySceneName;
        public Sprite Thumbnail => thumbnail;
    }
}
