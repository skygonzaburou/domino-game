using System;
using System.Collections;
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

        [Header("Batch Despawn (Step 7.5)")]
        [Tooltip("DespawnAllRegistered()実行時、1Frameあたり何個までDespawnするか(Transport Queueへの負荷分散用)")]
        [SerializeField] private int despawnBatchSize = 10;

        private class TrackedDomino
        {
            public NetworkObject NetworkObject;
            public Transform Transform;
            public Rigidbody Rigidbody;
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
                Transform = dominoNetworkObject.transform,
                Rigidbody = dominoNetworkObject.GetComponent<Rigidbody>()
            });
        }

        // Used by GameFlowManager's Chain Stop Failure monitor (RUNNING only). Reuses this
        // Manager's existing Register-on-Spawn list instead of a separate scene search, and
        // never writes any GameFlow state itself - it only answers "is anything still moving".
        public bool AnyTrackedDominoActive(float linearVelocityThreshold, float angularVelocityThreshold)
        {
            float linSq = linearVelocityThreshold * linearVelocityThreshold;
            float angSq = angularVelocityThreshold * angularVelocityThreshold;

            for (int i = 0; i < _tracked.Count; i++)
            {
                TrackedDomino tracked = _tracked[i];
                if (tracked.NetworkObject == null || !tracked.NetworkObject.IsSpawned) continue;
                if (tracked.Rigidbody == null) continue;

                if (tracked.Rigidbody.linearVelocity.sqrMagnitude > linSq) return true;
                if (tracked.Rigidbody.angularVelocity.sqrMagnitude > angSq) return true;
            }

            return false;
        }

        // Server-only Challenge Reset API (Step 7.5). Despawns every currently-registered
        // Domino - regardless of BUILDING-only Cleanup's own fallen/delay judgement - spread
        // across multiple frames so a large Stage doesn't Despawn hundreds of NetworkObjects
        // in one frame (see Step 5's UnityTransport Receive Queue warning). Does not touch
        // GameFlow state; the caller (GameFlowManager) is responsible for Phase/progress.
        public void DespawnAllRegistered(Action onComplete)
        {
            StartCoroutine(DespawnAllRegisteredRoutine(onComplete));
        }

        private IEnumerator DespawnAllRegisteredRoutine(Action onComplete)
        {
            var toDespawn = new List<NetworkObject>(_tracked.Count);
            for (int i = 0; i < _tracked.Count; i++)
            {
                NetworkObject no = _tracked[i].NetworkObject;
                if (no != null && no.IsSpawned) toDespawn.Add(no);
            }
            _tracked.Clear();

            int count = 0;
            for (int i = 0; i < toDespawn.Count; i++)
            {
                NetworkObject no = toDespawn[i];
                if (no != null && no.IsSpawned) no.Despawn(true);

                count++;
                if (count % despawnBatchSize == 0) yield return null;
            }

            onComplete?.Invoke();
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
