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

        // Step 10A: tracks IsActivePhase(phase) && !Paused, separately from _lastObservedPhase
        // below - Pause can flip this without any GamePhase change at all (GameplayPauseManager
        // never touches GameFlowManager's Phase), so it needs its own edge detection.
        private bool _lastActiveState;

        public bool HasStarted => _timerStarted.Value;
        public bool HasFinalTime => _finalBuildSeconds.Value >= 0.0;
        public double FinalBuildSeconds => _finalBuildSeconds.Value;

        private void Awake()
        {
            Instance = this;
        }

        private static bool IsActivePhase(GamePhase phase) => phase == GamePhase.BUILDING || phase == GamePhase.READY;

        private static bool IsPaused() => GameplayPauseManager.Instance != null && GameplayPauseManager.Instance.IsPaused;

        // Local-only (no network traffic): every Client computes this itself each time the HUD
        // needs to render, from its own NetworkManager.ServerTime estimate.
        public double GetBuildSeconds()
        {
            if (HasFinalTime) return _finalBuildSeconds.Value;
            if (!_timerStarted.Value) return 0.0;
            if (NetworkManager == null) return _accumulatedBuildSeconds.Value;

            // Step 10A: Paused freezes BUILD TIME regardless of GamePhase - NetworkManager.
            // ServerTime.Time keeps advancing in real time even while Time.timeScale == 0 (it is
            // NGO's own network time estimate, not driven by the local Time class at all), so
            // this must be checked explicitly rather than relying on Time.timeScale to freeze it
            // for free the way Physics/Time.deltaTime-based timers elsewhere in the project do.
            bool active = !IsPaused() && GameFlowManager.Instance != null && IsActivePhase(GameFlowManager.Instance.CurrentPhase);
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

        // Step 10A: called synchronously (Server-only) by GameplayPauseManager the instant its
        // Pause state changes, instead of waiting for this to be picked up by Update()'s polling
        // below on some later tick. Time.timeScale == 0 does not slow Update() itself (only
        // FixedUpdate) so that gap is at most one frame in any normally-running build - but
        // GetBuildSeconds() is computed fresh on every call from the *current* Paused flag, so
        // even a one-frame gap between "Paused flips" and "the accumulator/segment-start actually
        // catches up" is a real (if brief) window where GetBuildSeconds() would read a stale
        // _activeSegmentStartServerTime against the current ServerTime.Time and report a
        // momentarily wrong value. Applying the transition here, synchronously, at the exact
        // moment Paused changes removes that window entirely.
        public void SyncPauseState(bool paused)
        {
            if (!IsServer) return;
            if (GameFlowManager.Instance == null) return;

            bool isActive = !paused && IsActivePhase(GameFlowManager.Instance.CurrentPhase);
            ApplyActiveTransition(isActive);
        }

        private void ApplyActiveTransition(bool isActive)
        {
            if (isActive == _lastActiveState) return;

            if (_timerStarted.Value)
            {
                if (_lastActiveState && !isActive)
                {
                    // Pausing, or leaving BUILDING/READY (e.g. -> RUNNING): fold the
                    // just-finished active segment into the accumulated total.
                    _accumulatedBuildSeconds.Value += NetworkManager.ServerTime.Time - _activeSegmentStartServerTime.Value;
                }
                else if (!_lastActiveState && isActive)
                {
                    // Resuming, or (re-)entering BUILDING/READY (e.g. FAILED -> BUILDING): start
                    // a fresh active segment on top of the accumulated total - never reset to
                    // zero here. On Resume this deliberately excludes the entire Paused duration,
                    // since the segment starts counting from now, not from before the Pause.
                    _activeSegmentStartServerTime.Value = NetworkManager.ServerTime.Time;
                }
            }

            _lastActiveState = isActive;
        }

        private void Update()
        {
            if (!IsServer) return;
            if (GameFlowManager.Instance == null) return;

            GamePhase phase = GameFlowManager.Instance.CurrentPhase;

            // Step 10A: Pause transitions are now applied synchronously by SyncPauseState above,
            // not detected here - this only needs to watch for ordinary GamePhase transitions
            // (e.g. BUILDING -> RUNNING) via the same ApplyActiveTransition, exactly as before.
            ApplyActiveTransition(!IsPaused() && IsActivePhase(phase));

            if (phase != _lastObservedPhase)
            {
                _lastObservedPhase = phase;

                if (phase == GamePhase.SUCCESS)
                {
                    // RUNNING already paused BUILD TIME above (or on a prior frame), so the
                    // accumulated value is already the correct frozen total.
                    _finalBuildSeconds.Value = _accumulatedBuildSeconds.Value;
                }
            }
        }
    }
}
