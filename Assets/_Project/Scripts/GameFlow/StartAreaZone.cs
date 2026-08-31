using Unity.Netcode;
using UnityEngine;

namespace Domino.GameFlow
{
    public class StartAreaZone : DominoFallenZone
    {
        [Header("Running Player Constraint (Step 7.6)")]
        [Tooltip("PlayerのCapsule半径ぶん、実際に移動できる範囲をCollider境界より内側に狭めるマージン(m)")]
        [SerializeField] private float playerRadiusMargin = 0.5f;

        private BoxCollider _boxCollider;

        private void Awake()
        {
            _boxCollider = GetComponent<BoxCollider>();
        }

        // World-space position -> nearest point still inside this Zone's local-space bounds
        // (shrunk by playerRadiusMargin), fully honoring this Transform's Position/Rotation/
        // Scale so a rotated or non-uniformly-scaled Start Area still works correctly.
        public Vector3 ClampToLocalBounds(Vector3 worldPosition)
        {
            Vector3 scale = transform.lossyScale;
            Vector3 marginLocal = new Vector3(
                playerRadiusMargin / Mathf.Max(Mathf.Abs(scale.x), 0.0001f),
                0f,
                playerRadiusMargin / Mathf.Max(Mathf.Abs(scale.z), 0.0001f));

            Vector3 halfSize = _boxCollider.size * 0.5f - marginLocal;
            halfSize.x = Mathf.Max(halfSize.x, 0f);
            halfSize.z = Mathf.Max(halfSize.z, 0f);

            Vector3 local = transform.InverseTransformPoint(worldPosition);
            float clampedX = Mathf.Clamp(local.x, _boxCollider.center.x - halfSize.x, _boxCollider.center.x + halfSize.x);
            float clampedZ = Mathf.Clamp(local.z, _boxCollider.center.z - halfSize.z, _boxCollider.center.z + halfSize.z);

            return transform.TransformPoint(new Vector3(clampedX, local.y, clampedZ));
        }

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
