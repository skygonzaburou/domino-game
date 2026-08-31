using Unity.Netcode;
using UnityEngine;

namespace Domino.GameFlow
{
    public class StartAreaZone : DominoFallenZone
    {
        protected override void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                NotifyPlayer(other, entered: true);
                return;
            }
            base.OnTriggerEnter(other);
        }

        protected override void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                NotifyPlayer(other, entered: false);
                return;
            }
            base.OnTriggerExit(other);
        }

        private void NotifyPlayer(Collider other, bool entered)
        {
            var playerObj = other.GetComponentInParent<NetworkObject>();
            if (playerObj == null || GameFlowManager.Instance == null) return;

            if (entered) GameFlowManager.Instance.NotifyPlayerEnteredStart(playerObj.OwnerClientId);
            else GameFlowManager.Instance.NotifyPlayerExitedStart(playerObj.OwnerClientId);
        }
    }
}
