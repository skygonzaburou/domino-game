using UnityEngine;

namespace Domino.GameFlow
{
    public class CheckpointZone : DominoFallenZone
    {
        [Header("Checkpoint")]
        [Tooltip("0始まりの通過順序。0 = 最初に通過するCheckpoint。Hierarchy順ではなくこの値が正です")]
        [SerializeField] private int checkpointIndex;

        public int CheckpointIndex => checkpointIndex;
    }
}
