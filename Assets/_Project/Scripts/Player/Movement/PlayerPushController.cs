using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Domino.Player
{
    // Player-vs-Player collision push (Step 8A). Owner Authority is preserved throughout: each
    // Player's Rigidbody is only ever non-kinematic on its own Owner's machine (NetworkRigidbody
    // makes every other copy Kinematic), so a Player can never be physically shoved by ordinary
    // Rigidbody collision response alone - a Kinematic body isn't moved by collision forces. This
    // component instead detects the collision locally on the attacker's own Owner client, asks
    // the Server to validate and compute the push (distance-clamped, direction re-derived from
    // authoritative positions), and the Server applies it only on the target's own Owner client
    // via PlayerMotor.ApplyPushRpc, where that Player's Rigidbody actually is Dynamic.
    [RequireComponent(typeof(PlayerMotor))]
    public class PlayerPushController : NetworkBehaviour
    {
        [Header("Player Push (Step 8A)")]
        [Tooltip("Pushで相手に与える水平速度の大きさ(m/秒)")]
        [SerializeField] private float pushForce = 3.5f;
        [Tooltip("同じ相手への連続Push間隔(秒)。OnCollisionEnterは接触開始時にしか発火しないため通常は連射自体起きないが、接触が瞬間的に切れて再接触した場合の連打防止用")]
        [SerializeField] private float pushCooldown = 0.4f;
        [Tooltip("Serverがこの距離(m)を超えるRequestを不正/遅延として無視する")]
        [SerializeField] private float maxValidationDistance = 2.5f;

        private readonly Dictionary<ulong, float> _lastRequestSentTime = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, float> _lastPushAppliedTime = new Dictionary<ulong, float>();

        // Local (attacker-side) detection only - the target's own copy of this same component
        // also runs OnCollisionEnter for the same physical contact, but IsOwner gates it off
        // there, so exactly one RequestPushRpc is sent per direction per fresh contact.
        private void OnCollisionEnter(Collision collision)
        {
            if (!IsOwner) return;
            if (!collision.gameObject.CompareTag("Player")) return;

            NetworkObject targetObj = collision.gameObject.GetComponentInParent<NetworkObject>();
            if (targetObj == null || targetObj == NetworkObject) return;

            ulong targetId = targetObj.NetworkObjectId;
            if (_lastRequestSentTime.TryGetValue(targetId, out float lastSent) && Time.time - lastSent < pushCooldown) return;
            _lastRequestSentTime[targetId] = Time.time;

            RequestPushRpc(new NetworkObjectReference(targetObj));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestPushRpc(NetworkObjectReference targetRef, RpcParams rpcParams = default)
        {
            if (!targetRef.TryGet(out NetworkObject targetObj)) return;
            if (targetObj == NetworkObject) return;

            ulong targetId = targetObj.NetworkObjectId;
            if (_lastPushAppliedTime.TryGetValue(targetId, out float lastApplied) && Time.time - lastApplied < pushCooldown) return;

            Vector3 delta = targetObj.transform.position - transform.position;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance > maxValidationDistance) return;

            Vector3 direction = distance > 0.0001f ? delta / distance : targetObj.transform.forward;

            PlayerMotor targetMotor = targetObj.GetComponent<PlayerMotor>();
            if (targetMotor == null) return;

            _lastPushAppliedTime[targetId] = Time.time;
            targetMotor.ApplyPushRpc(direction * pushForce);
        }
    }
}
