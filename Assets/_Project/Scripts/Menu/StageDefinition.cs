using UnityEngine;

namespace Domino.Menu
{
    [CreateAssetMenu(fileName = "StageDefinition", menuName = "Domino/Stage Definition")]
    public class StageDefinition : ScriptableObject
    {
        [SerializeField] private int stageId;
        [SerializeField] private string displayName;
        [SerializeField] private string gameplaySceneName;

        public int StageId => stageId;
        public string DisplayName => displayName;
        public string GameplaySceneName => gameplaySceneName;
    }
}
