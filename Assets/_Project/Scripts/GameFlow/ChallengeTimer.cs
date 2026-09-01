using Unity.Netcode;
using UnityEngine;

namespace Domino.GameFlow
{
    // BUILD TIME timer (Step 8.5, replaces the Step 8B RUNNING-time Challenge Timer).
    // Deliberately separate from GameFlowManager: this component only cares about accumulating
    // active Build time and reacting to Phase, not Phase transition rules themselves - it
    // observes GameFlowManager.CurrentPhase from the outside (Server-only polling, edge-
    // detected) rather than GameFlowManager reaching into it for everyday Phase changes.
    // GameFlowManager only calls in directly for the two things that are NOT Phase-observable:
    // NotifyDominoPlaced() (first successful Network Spawn - PlacementController/DominoSpawner
    // don't raise a Phase change) and ResetForRetry() (a full session reset).
    public class ChallengeTimer : NetworkBehaviour
    {
        public static ChallengeTimer Instance { get; private set; }

        private const double NotSet = -1.0;

        // Whether this Build Session has begun (first Real Domino Network Spawn). Before this,
        // BUILD TIME is not running at all - not merely zero.
        private readonly NetworkVariable<bool> _timerStarted = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Sum of all completed "active" (BUILDING/READY) segments so far. Frozen while paused
        // (RUNNING/FAILED) and while there is a Final Build Time (SUCCESS).
        private readonly NetworkVariable<double> _accumulatedBuildSeconds = new NetworkVariable<double>(
            0.0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // ServerTime.Time at which the current active segment began. Only meaningful while
        // Phase is BUILDING/READY; not read otherwise.
        private readonly NetworkVariable<double> _activeSegmentStartServerTime = new NetworkVariable<double>(
            NotSet, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Set once, on SUCCESS, from the (already-paused-since-RUNNING-began) accumulated value.
        private readonly NetworkVariable<double> _finalBuildSeconds = new NetworkVariable<double>(
            NotSet, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private GamePhase _lastObservedPhase;

        public bool HasStarted => _timerStarted.Value;
        public bool HasFinalTime => _finalBuildSeconds.Value >= 0.0;
        public double FinalBuildSeconds => _finalBuildSeconds.Value;

        private void Awake()
        {
            Instance = this;
        }

        private static bool IsActivePhase(GamePhase phase) => phase == GamePhase.BUILDING || phase == GamePhase.READY;

        // Local-only (no network traffic): every Client computes this itself each time the HUD
        // needs to render, from its own NetworkManager.ServerTime estimate.
        public double GetBuildSeconds()
        {
            if (HasFinalTime) return _finalBuildSeconds.Value;
            if (!_timerStarted.Value) return 0.0;
            if (NetworkManager == null) return _accumulatedBuildSeconds.Value;

            bool active = GameFlowManager.Instance != null && IsActivePhase(GameFlowManager.Instance.CurrentPhase);
            if (!active) return _accumulatedBuildSeconds.Value;

            return _accumulatedBuildSeconds.Value + (NetworkManager.ServerTime.Time - _activeSegmentStartServerTime.Value);
        }

        // Called by DominoSpawner right after the first successful Real Domino Network Spawn of
        // this Build Session. A no-op if BUILD TIME has already started - only the very first
        // Domino of a session starts the clock.
        public void NotifyDominoPlaced()
        {
            if (!IsServer) return;
            if (_timerStarted.Value) return;

            _timerStarted.Value = true;
            _accumulatedBuildSeconds.Value = 0.0;
            _activeSegmentStartServerTime.Value = NetworkManager.ServerTime.Time;
        }

        // Called by GameFlowManager on a successful Retry: fully ends this Build Session so the
        // next First Domino starts an entirely new BUILD TIME from zero.
        public void ResetForRetry()
        {
            if (!IsServer) return;

            _timerStarted.Value = false;
            _accumulatedBuildSeconds.Value = 0.0;
            _activeSegmentStartServerTime.Value = NotSet;
            _finalBuildSeconds.Value = NotSet;
        }

        private void Update()
        {
            if (!IsServer) return;
            if (GameFlowManager.Instance == null) return;

            GamePhase phase = GameFlowManager.Instance.CurrentPhase;
            if (phase == _lastObservedPhase) return;

            GamePhase previous = _lastObservedPhase;
            _lastObservedPhase = phase;

            if (_timerStarted.Value)
            {
                bool wasActive = IsActivePhase(previous);
                bool isActive = IsActivePhase(phase);

                if (wasActive && !isActive)
                {
                    // Pausing (BUILDING/READY -> RUNNING): fold the just-finished active
                    // segment into the accumulated total.
                    _accumulatedBuildSeconds.Value += NetworkManager.ServerTime.Time - _activeSegmentStartServerTime.Value;
                }
                else if (!wasActive && isActive)
                {
                    // Resuming (FAILED -> BUILDING): start a fresh active segment on top of
                    // the accumulated total - never reset to zero here.
                    _activeSegmentStartServerTime.Value = NetworkManager.ServerTime.Time;
                }
            }

            if (phase == GamePhase.SUCCESS)
            {
                // RUNNING already paused BUILD TIME above (or on a prior frame), so the
                // accumulated value is already the correct frozen total.
                _finalBuildSeconds.Value = _accumulatedBuildSeconds.Value;
            }
        }
    }
}
