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
            // Step 10A: Pause blocks all Gameplay Input (Section 12) - an RPC send is not
            // blocked by Time.timeScale, so this must be checked explicitly.
            if (GameplayPauseManager.Instance != null && GameplayPauseManager.Instance.IsPaused) return;

            if (Keyboard.current[startChallengeKey].wasPressedThisFrame && GameFlowManager.Instance != null)
            {
                // Step 14.3: the same key now also cancels a RUNNING Challenge back to
                // BUILDING - which RPC to send is decided from this Client's own local read of
                // CurrentPhase (a NetworkVariable, already synced to everyone); the Server
                // re-validates the actual Phase independently in both RPCs regardless.
                GamePhase phase = GameFlowManager.Instance.CurrentPhase;
                if (phase == GamePhase.READY)
                {
                    GameFlowManager.Instance.RequestStartChallengeRpc();
                }
                else if (phase == GamePhase.RUNNING)
                {
                    GameFlowManager.Instance.RequestCancelChallengeRpc();
                }
            }
        }
    }
}
