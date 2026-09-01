using Unity.Netcode;
using UnityEngine;

namespace Domino.GameFlow
{
    // Challenge Time Attack timer (Step 8B). Deliberately separate from GameFlowManager: this
    // component only cares about "when did RUNNING start" and "what was the final time", not
    // Phase transition rules themselves - it observes GameFlowManager.CurrentPhase from the
    // outside (Server-only polling, edge-detected) rather than GameFlowManager reaching into it,
    // so GameFlowManager needs no changes at all to support this.
    public class ChallengeTimer : NetworkBehaviour
    {
        public static ChallengeTimer Instance { get; private set; }

        private const double NotSet = -1.0;

        // Synced once per Run (on RUNNING start) and once per Success (on SUCCESS) - never
        // per-frame. Clients compute the live Elapsed Time locally from RunStartServerTime and
        // their own continuously-updated NetworkManager.ServerTime estimate; no ticking
        // NetworkVariable is ever sent.
        private readonly NetworkVariable<double> _runStartServerTime = new NetworkVariable<double>(
            NotSet, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<double> _finalElapsedSeconds = new NetworkVariable<double>(
            NotSet, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private GamePhase _lastObservedPhase;

        public bool HasFinalTime => _finalElapsedSeconds.Value >= 0.0;
        public double FinalElapsedSeconds => _finalElapsedSeconds.Value;
        public bool IsRunning => _runStartServerTime.Value >= 0.0 && !HasFinalTime;

        private void Awake()
        {
            Instance = this;
        }

        // Local-only (no network traffic): every Client computes this itself each time the HUD
        // needs to render, from its own NetworkManager.ServerTime estimate - the same pattern
        // NGO uses for client-side interpolation elsewhere.
        public double GetElapsedSeconds()
        {
            if (HasFinalTime) return _finalElapsedSeconds.Value;
            if (_runStartServerTime.Value < 0.0 || NetworkManager == null) return 0.0;
            return NetworkManager.ServerTime.Time - _runStartServerTime.Value;
        }

        private void Update()
        {
            if (!IsServer) return;
            if (GameFlowManager.Instance == null) return;

            GamePhase phase = GameFlowManager.Instance.CurrentPhase;
            if (phase == _lastObservedPhase) return;

            GamePhase previous = _lastObservedPhase;
            _lastObservedPhase = phase;

            if (phase == GamePhase.RUNNING)
            {
                _runStartServerTime.Value = NetworkManager.ServerTime.Time;
                _finalElapsedSeconds.Value = NotSet;
            }
            else if (phase == GamePhase.SUCCESS && previous == GamePhase.RUNNING)
            {
                _finalElapsedSeconds.Value = NetworkManager.ServerTime.Time - _runStartServerTime.Value;
            }
            else if (phase == GamePhase.BUILDING)
            {
                // Covers both the FAILED->BUILDING reset and the (currently unused) SUCCESS->
                // BUILDING path: next Challenge always starts from a clean, un-set Timer.
                _runStartServerTime.Value = NotSet;
                _finalElapsedSeconds.Value = NotSet;
            }
        }
    }
}
