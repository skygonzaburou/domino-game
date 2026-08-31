using Unity.Netcode;
using UnityEngine;

namespace Domino.DominoSystem
{
    public class DominoSpawner : MonoBehaviour
    {
        public static DominoSpawner Instance { get; private set; }

        [SerializeField] private GameObject dominoPrefab;
        [SerializeField] private float maxPlacementDistance = 3f;

        private void Awake()
        {
            Instance = this;
        }

        public bool TryPlaceDomino(Vector3 position, Quaternion rotation, ulong senderClientId)
        {
            if (!NetworkManager.Singleton.IsServer)
            {
                return false;
            }

            if (!IsFinite(position) || !IsFinite(rotation))
            {
                Debug.LogWarning($"[DominoSpawner] Rejected placement from client {senderClientId}: non-finite position/rotation.");
                return false;
            }

            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(senderClientId, out var client) || client.PlayerObject == null)
            {
                Debug.LogWarning($"[DominoSpawner] Rejected placement from client {senderClientId}: not a connected player.");
                return false;
            }

            float distance = Vector3.Distance(client.PlayerObject.transform.position, position);
            if (distance > maxPlacementDistance)
            {
                Debug.LogWarning($"[DominoSpawner] Rejected placement from client {senderClientId}: distance {distance:F2}m exceeds max {maxPlacementDistance}m.");
                return false;
            }

            NetworkObject spawned = NetworkObject.InstantiateAndSpawn(
                dominoPrefab,
                NetworkManager.Singleton,
                ownerClientId: NetworkManager.ServerClientId,
                position: position,
                rotation: rotation);

            if (DominoCleanupManager.Instance != null)
            {
                DominoCleanupManager.Instance.Register(spawned);
            }

            return true;
        }

        private static bool IsFinite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsInfinity(v.x)
                && !float.IsNaN(v.y) && !float.IsInfinity(v.y)
                && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
        }

        private static bool IsFinite(Quaternion q)
        {
            return !float.IsNaN(q.x) && !float.IsInfinity(q.x)
                && !float.IsNaN(q.y) && !float.IsInfinity(q.y)
                && !float.IsNaN(q.z) && !float.IsInfinity(q.z)
                && !float.IsNaN(q.w) && !float.IsInfinity(q.w);
        }
    }
}
