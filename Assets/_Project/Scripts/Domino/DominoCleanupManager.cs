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
    // active Challenge or awaiting a Reset (Failure or Retry).
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
        [Tooltip("DespawnAllRegistered()/DespawnFallenRegistered()実行時、1Frameあたり何個までDespawnするか(Transport Queueへの負荷分散用)")]
        [SerializeField] private int despawnBatchSize = 10;

        [Header("Meaningful Activity (Step 8.6)")]
        [Tooltip("前回Poll時からのTilt Angle変化がこの値(度)を超えていたら、Velocityが閾値未満でもChain Activityとみなす")]
        [SerializeField] private float meaningfulTiltDelta = 2f;
        [Tooltip("この高さ(World Y)を下回ったDominoは、Stageから逸脱した(Physics Explosion等で吹き飛んだ)ものとみなし即座にDespawnする。Failure Monitorの活動判定を永久に汚染するのを防ぐための安全網")]
        [SerializeField] private float outOfBoundsY = -10f;

        private class TrackedDomino
        {
            public NetworkObject NetworkObject;
            public Transform Transform;
            public Rigidbody Rigidbody;
            public float FallenSince = -1f;
            public float PreviousTiltAngle = -1f;
        }

        // Despawn-eligible: Player-placed via DominoSpawner. Subject to BUILDING auto-cleanup,
        // FAILED Fallen-only cleanup, and Retry full cleanup.
        private readonly List<TrackedDomino> _tracked = new List<TrackedDomino>();

        // Activity-detection only (Step 8.6): Stage-authored Dominoes placed directly in the
        // Scene (never go through DominoSpawner.Register(), so were previously invisible to
        // the Failure Monitor entirely). Populated once at startup, never Despawned by any
        // GameFlow-driven cleanup - Scene-authored Stage geometry has no respawn mechanism, so
        // it must stay untouched by BUILDING cleanup/FAILED cleanup/Retry alike, exactly as it
        // already implicitly was before this Step (only _tracked was ever eligible for Despawn).
        private readonly List<TrackedDomino> _sceneAuthored = new List<TrackedDomino>();
        private bool _sceneAuthoredScanned;

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

        // One-time (Server-only, first Update() tick) Scene-wide search for pre-existing
        // "Domino"-tagged NetworkObjects not spawned through DominoSpawner - i.e. everything
        // that exists in the Scene at startup, before any Player has placed anything. This is
        // the one deliberate exception to "never search the Scene" - it runs exactly once per
        // session, not per-frame, and only feeds the Activity list (see _sceneAuthored above).
        private void ScanSceneAuthoredDominoes()
        {
            var dominoObjects = GameObject.FindGameObjectsWithTag("Domino");
            for (int i = 0; i < dominoObjects.Length; i++)
            {
                NetworkObject no = dominoObjects[i].GetComponent<NetworkObject>();
                if (no == null) continue;

                _sceneAuthored.Add(new TrackedDomino
                {
                    NetworkObject = no,
                    Transform = no.transform,
                    Rigidbody = no.GetComponent<Rigidbody>()
                });
            }
        }

        // Used by GameFlowManager's Chain Stop Failure monitor (RUNNING only). Reuses this
        // Manager's existing Register-on-Spawn lists instead of a separate scene search each
        // call, and never writes any GameFlow state itself - it only answers "is anything
        // still meaningfully moving". Checks both Player-placed and Scene-authored Dominoes
        // (Step 8.6 - previously only Player-placed ones were visible here at all).
        //
        // "Meaningful" is deliberately not velocity-only (Step 8.6): a Domino mid-topple can
        // pass through a near-zero-velocity instant (settling into contact, a brief Physics
        // sleep, etc.) while still visibly, meaningfully in motion. A per-poll Tilt Angle
        // change is a second, independent signal that catches this - a Domino that is still
        // rotating over time is still "active" even if its instantaneous velocity sample
        // happens to read low. PreviousTiltAngle is updated as a side effect of this call,
        // which is safe because GameFlowManager's Failure Monitor is this method's only
        // caller, at a fixed, predictable cadence (failureCheckInterval).
        public bool HasMeaningfulActivity(float linearVelocityThreshold, float angularVelocityThreshold)
        {
            float linSq = linearVelocityThreshold * linearVelocityThreshold;
            float angSq = angularVelocityThreshold * angularVelocityThreshold;

            bool any = false;
            any |= CheckListActivity(_tracked, linSq, angSq);
            any |= CheckListActivity(_sceneAuthored, linSq, angSq);
            return any;
        }

        private bool CheckListActivity(List<TrackedDomino> list, float linSq, float angSq)
        {
            bool any = false;
            for (int i = 0; i < list.Count; i++)
            {
                TrackedDomino tracked = list[i];
                if (tracked.NetworkObject == null || !tracked.NetworkObject.IsSpawned) continue;
                if (tracked.Rigidbody == null) continue;

                float tilt = Vector3.Angle(tracked.Transform.up, Vector3.up);
                float previousTilt = tracked.PreviousTiltAngle;
                tracked.PreviousTiltAngle = tilt;

                if (tracked.Rigidbody.linearVelocity.sqrMagnitude > linSq) { any = true; continue; }
                if (tracked.Rigidbody.angularVelocity.sqrMagnitude > angSq) { any = true; continue; }
                if (previousTilt >= 0f && Mathf.Abs(tilt - previousTilt) > meaningfulTiltDelta) { any = true; }
            }
            return any;
        }

        // Server-only Challenge Reset API (Step 7.5). Despawns every currently-registered
        // Player-placed Domino - regardless of BUILDING-only Cleanup's own fallen/delay
        // judgement - spread across multiple frames so a large Stage doesn't Despawn hundreds
        // of NetworkObjects in one frame (see Step 5's UnityTransport Receive Queue warning).
        // Does not touch GameFlow state; the caller (GameFlowManager) is responsible for
        // Phase/progress. Used by Retry (SUCCESS -> BUILDING) - never by FAILED (see
        // DespawnFallenRegistered below).
        public void DespawnAllRegistered(Action onComplete)
        {
            StartCoroutine(DespawnRoutine(_tracked, allowStanding: true, onComplete));
        }

        // Server-only Failure Reset API (Step 8.6). Despawns only currently-Fallen (>=
        // fallenAngleThreshold, the same single definition used everywhere else in the
        // project - no separate threshold) Player-placed Dominoes, leaving Standing ones on
        // the Stage so the Player can re-place just the failed section. Same Batch Despawn
        // pattern as DespawnAllRegistered. Never touches Scene-authored Dominoes (see
        // _sceneAuthored above - they are never Despawn-eligible by any path).
        public void DespawnFallenRegistered(Action onComplete)
        {
            StartCoroutine(DespawnRoutine(_tracked, allowStanding: false, onComplete));
        }

        private IEnumerator DespawnRoutine(List<TrackedDomino> list, bool allowStanding, Action onComplete)
        {
            var toDespawn = new List<NetworkObject>(list.Count);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                TrackedDomino tracked = list[i];
                if (tracked.NetworkObject == null || !tracked.NetworkObject.IsSpawned)
                {
                    list.RemoveAt(i);
                    continue;
                }

                bool fallen = Vector3.Angle(tracked.Transform.up, Vector3.up) >= fallenAngleThreshold;
                if (!allowStanding && !fallen) continue;

                toDespawn.Add(tracked.NetworkObject);
                list.RemoveAt(i);
            }

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

            if (!_sceneAuthoredScanned)
            {
                ScanSceneAuthoredDominoes();
                _sceneAuthoredScanned = true;
            }

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

                // Out-of-bounds safety net (Step 8.6): a Domino that has fallen through/off
                // the Stage (observed in practice from a Physics "explosion" when two Dominoes
                // are Spawned overlapping) would otherwise sit in _tracked with a runaway,
                // never-settling Rigidbody forever, permanently corrupting HasMeaningfulActivity
                // regardless of Phase. Remove it immediately, independent of BUILDING/fallen
                // judgement below.
                if (tracked.Transform.position.y < outOfBoundsY)
                {
                    NetworkObject stray = tracked.NetworkObject;
                    _tracked.RemoveAt(i);
                    stray.Despawn(true);
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
