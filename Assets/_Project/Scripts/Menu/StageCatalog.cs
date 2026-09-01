using System.Collections.Generic;
using UnityEngine;

namespace Domino.Menu
{
    [CreateAssetMenu(fileName = "StageCatalog", menuName = "Domino/Stage Catalog")]
    public class StageCatalog : ScriptableObject
    {
        [SerializeField] private List<StageDefinition> stages = new List<StageDefinition>();

        public IReadOnlyList<StageDefinition> Stages => stages;

        public StageDefinition FindById(int stageId)
        {
            for (int i = 0; i < stages.Count; i++)
            {
                if (stages[i] != null && stages[i].StageId == stageId) return stages[i];
            }
            return null;
        }
    }
}
