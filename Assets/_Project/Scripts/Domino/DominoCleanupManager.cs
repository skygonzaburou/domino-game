using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Domino.GameFlow;

namespace Domino.DominoSystem
{
    // Host-only. Tracks Network-spawned Dominoes and despawns any that stay
    // fallen for too long, but only while BUILDING - never during READY/
    // RUNNING/SUCCESS, since a fallen Domino there is either part of the
    // active Challenge or awaiting a Restart implemented in a future Step.
    public class DominoCleanupManager : MonoBehaviour
    {
        public static DominoCleanupManager Instance { get; private set; }

        [Header("Fallen Detection")]
        [Tooltip("この角度(度)以上傾いたDominoを「倒れた」と判定する")]
        [SerializeField] private float fallenAngleThreshold = 60f;

        [Header("Cleanup Timing")]
        [Tooltip("倒れた状態がこの秒数続いたら削除する")]
        [SerializeField] private float cleanupDelay = 3f;
        [Tooltip("何秒おきに登録Dominoの状態を確認するか")]
        [SerializeField] private float checkInterval = 0.2f;

        private class TrackedDomino
        {
            public NetworkObject NetworkObject;
            public Transform Transform;
            public float FallenSince = -1f;
        }

        private readonly List<TrackedDomino> _tracked = new List<TrackedDomino>();
        private float _timeSinceLastCheck;

        private void Awake()
        {
            Instance = this;
        }

        public void Register(NetworkObject dominoNetworkObject)
        {
            if (dominoNetworkObject == null) return;
            _tracked.Add(new TrackedDomino
            {
                NetworkObject = dominoNetworkObject,
                Transform = dominoNetworkObject.transform
            });
        }

        private void Update()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

            _timeSinceLastCheck += Time.deltaTime;
            if (_timeSinceLastCheck < checkInterval) return;
            _timeSinceLastCheck = 0f;

            bool buildingPhase = GameFlowManager.Instance != null && GameFlowManager.Instance.CurrentPhase == GamePhase.BUILDING;

            for (int i = _tracked.Count - 1; i >= 0; i--)
            {
                TrackedDomino tracked = _tracked[i];
                if (tracked.NetworkObject == null || !tracked.NetworkObject.IsSpawned)
                {
                    _tracked.RemoveAt(i);
                    continue;
                }

                if (!buildingPhase)
                {
                    // Cancel any pending cleanup while not BUILDING (READY/RUNNING/
                    // SUCCESS) - a fallen Domino there must never be auto-removed.
                    tracked.FallenSince = -1f;
                    continue;
                }

                bool fallen = Vector3.Angle(tracked.Transform.up, Vector3.up) >= fallenAngleThreshold;

                if (!fallen)
                {
                    tracked.FallenSince = -1f;
                    continue;
                }

                if (tracked.FallenSince < 0f)
                {
                    tracked.FallenSince = Time.time;
                }
                else if (Time.time - tracked.FallenSince >= cleanupDelay)
                {
                    NetworkObject toDespawn = tracked.NetworkObject;
                    _tracked.RemoveAt(i);
                    toDespawn.Despawn(true);
                }
            }
        }
    }
}
