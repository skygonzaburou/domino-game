using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Domino.Player
{
    // Step 11.2: Player -> Domino contact assist. Separate from PlayerPushController (Player ->
    // Player, untouched) - this only ever nudges a Domino the Owner Player's own Collider
    // freshly touches, via a small Server-applied Impulse, so a light walking Contact reliably
    // starts a topple instead of the native Collision response sometimes doing effectively
    // nothing. Deliberately does not touch Domino Mass/Drag/Collider/PhysicMaterial or
    // Domino-to-Domino Physics in any way - the Impulse is a one-shot nudge on fresh Contact
    // only (OnCollisionEnter, not Stay), capped and cooldown-protected so it can never compound
    // into a Physics Explosion, and is skipped once a Domino is already tipping on its own
    // (e.g. mid Chain Reaction) so it never interferes with that.
    //
    // Same Owner-detects / Server-validates-and-applies Authority pattern as
    // PlayerPushController.RequestPushRpc, so Domino stays Host-authoritative regardless of
    // which connected Client's Player triggers the Contact (Section 16 - no Online Runtime Test
    // performed this Step, but the Architecture is unchanged from the existing Player-vs-Player
    // Push precedent that already works Online).
    [RequireComponent(typeof(PlayerMotor))]
    public class PlayerDominoContact : NetworkBehaviour
    {
        [Header("Player -> Domino Contact Assist (Step 11.2)")]
        [Tooltip("接触時にDominoへ与える水平Impulseの大きさ(Domino Mass=1のためほぼm/秒の速度変化に等しい)")]
        [SerializeField] private float dominoContactImpulse = 0.6f;
        [Tooltip("Impulseを与える高さ(Domino原点からの相対Y、m)。中心よりやや高い位置に与えることでTipping Torqueを発生させる")]
        [SerializeField] private float contactHeightOffset = 0.3f;
        [Tooltip("同じDominoへの連続Impulse間隔(秒)。OnCollisionEnterは接触開始時にしか発火しないため通常は連射自体起きないが、接触が瞬間的に切れて再接触した場合の連打防止用")]
        [SerializeField] private float contactCooldown = 0.5f;
        [Tooltip("Serverがこの距離(m)を超えるRequestを不正/遅延として無視する")]
        [SerializeField] private float maxValidationDistance = 2.5f;
        [Tooltip("この角度(度)以上既に傾いているDominoにはImpulseを与えない(Chain Reaction中のDominoへ干渉しないため)")]
        [SerializeField] private float alreadyTippingThreshold = 25f;

        private readonly Dictionary<ulong, float> _lastRequestSentTime = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, float> _lastImpulseAppliedTime = new Dictionary<ulong, float>();

        // Local (Owner-side) detection only, mirroring PlayerPushController exactly - fires on
        // this Player's own fresh Collision with any "Domino"-tagged Collider.
        private void OnCollisionEnter(Collision collision)
        {
            if (!IsOwner) return;
            if (!collision.gameObject.CompareTag("Domino")) return;

            NetworkObject dominoObj = collision.gameObject.GetComponentInParent<NetworkObject>();
            if (dominoObj == null) return;

            ulong dominoId = dominoObj.NetworkObjectId;
            if (_lastRequestSentTime.TryGetValue(dominoId, out float lastSent) && Time.time - lastSent < contactCooldown) return;
            _lastRequestSentTime[dominoId] = Time.time;

            RequestDominoContactImpulseRpc(new NetworkObjectReference(dominoObj));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestDominoContactImpulseRpc(NetworkObjectReference dominoRef, RpcParams rpcParams = default)
        {
            if (!dominoRef.TryGet(out NetworkObject dominoObj)) return;

            ulong dominoId = dominoObj.NetworkObjectId;
            if (_lastImpulseAppliedTime.TryGetValue(dominoId, out float lastApplied) && Time.time - lastApplied < contactCooldown) return;

            Rigidbody dominoRb = dominoObj.GetComponent<Rigidbody>();
            if (dominoRb == null) return;

            if (Vector3.Angle(dominoObj.transform.up, Vector3.up) >= alreadyTippingThreshold) return;

            // Direction re-derived from authoritative Server-side positions (never trusts a
            // Client-supplied direction), exactly like PlayerPushController.RequestPushRpc.
            Vector3 delta = dominoObj.transform.position - transform.position;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance > maxValidationDistance) return;

            Vector3 direction = distance > 0.0001f ? delta / distance : transform.forward;

            _lastImpulseAppliedTime[dominoId] = Time.time;

            Vector3 contactPoint = dominoObj.transform.position + Vector3.up * contactHeightOffset;
            dominoRb.AddForceAtPosition(direction * dominoContactImpulse, contactPoint, ForceMode.Impulse);
        }
    }
}
