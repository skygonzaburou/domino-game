using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Domino.GameFlow;

namespace Domino.Player
{
    // Deliberately separate from PlacementController: this is a single input->RPC
    // trigger unrelated to placement, and keeping it standalone makes it trivial to
    // swap the key for a UI button later without touching placement code.
    public class ChallengeInput : NetworkBehaviour
    {
        [SerializeField] private Key startChallengeKey = Key.F;

        public override void OnNetworkSpawn()
        {
            if (!IsOwner)
            {
                enabled = false;
            }
        }

        private void Update()
        {
            if (!IsOwner) return;
            if (Keyboard.current == null) return;

            if (Keyboard.current[startChallengeKey].wasPressedThisFrame && GameFlowManager.Instance != null)
            {
                GameFlowManager.Instance.RequestStartChallengeRpc();
            }
        }
    }
}
