using Unity.Netcode;
using UnityEngine;

namespace Domino.GameFlow
{
    // Scene-placed NetworkObject (SampleScene, sibling of GameFlowManager) - Step 10A.
    //
    // Time.timeScale = 0 is deliberately used as the actual freeze mechanism (Step 10 spec,
    // Section 8) rather than disabling every individual script by hand: MonoBehaviour.Update()
    // still runs every frame at timeScale = 0 (so UI/Input System/NGO's own network tick all
    // keep working), but Time.deltaTime/Time.fixedDeltaTime/Time.time all collapse to 0, so
    // FixedUpdate()-driven Physics (Player/Domino Rigidbodies) and every Time.deltaTime/Time.time
    // based accumulator already in the project (GameFlowManager's Failure Monitor + Ready Poll,
    // DominoCleanupManager's Fallen timers) freeze automatically and resume exactly where they
    // left off - none of those scripts needed to change for Step 10. The one exception is
    // ChallengeTimer, which deliberately uses NetworkManager.ServerTime.Time (real network time,
    // immune to local Time.timeScale) for BUILD TIME - it explicitly observes IsPaused below.
    //
    // Confirmed via realtime Play Mode testing (Section 8's explicit requirement): RPCs sent to/
    // from the Server and NetworkVariable sync both keep working normally at Time.timeScale = 0.
    public class GameplayPauseManager : NetworkBehaviour
    {
        public static GameplayPauseManager Instance { get; private set; }

        private readonly NetworkVariable<bool> _isPaused = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public bool IsPaused => _isPaused.Value;

        private void Awake()
        {
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            _isPaused.OnValueChanged += HandlePausedChanged;
            ApplyLocal(_isPaused.Value);
        }

        public override void OnNetworkDespawn()
        {
            _isPaused.OnValueChanged -= HandlePausedChanged;
            ApplyLocal(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            // Last-resort safety net (Section 35): whatever else happens to this Object
            // (Scene unload, Network Shutdown, Editor Stop), never leave the Editor/Player
            // itself sitting at Time.timeScale == 0.
            Time.timeScale = 1f;
        }

        private void HandlePausedChanged(bool previous, bool current)
        {
            ApplyLocal(current);

            // Server-only, synchronous (not left to ChallengeTimer.Update()'s own polling) - see
            // ChallengeTimer.SyncPauseState for why the exact moment matters here.
            if (IsServer) ChallengeTimer.Instance?.SyncPauseState(current);
        }

        // Every Client (Host included) applies this identically and purely locally - this only
        // reacts to the already-synced NetworkVariable above, no network traffic of its own.
        private static void ApplyLocal(bool paused)
        {
            Time.timeScale = paused ? 0f : 1f;
        }

        private static bool IsPausablePhase(GamePhase phase) =>
            phase == GamePhase.BUILDING || phase == GamePhase.READY || phase == GamePhase.RUNNING;

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestPauseRpc(RpcParams rpcParams = default)
        {
            if (_isPaused.Value) return;
            if (GameFlowManager.Instance == null || !IsPausablePhase(GameFlowManager.Instance.CurrentPhase)) return;
            _isPaused.Value = true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestResumeRpc(RpcParams rpcParams = default)
        {
            if (!_isPaused.Value) return;
            _isPaused.Value = false;
        }

        // Server-only (Step 10). Called by GameFlowManager once a Restart reset sequence has
        // fully applied its new state, so the freshly-reset stage is never left frozen. A no-op
        // if not currently Paused (e.g. an ordinary SUCCESS -> Retry, which can never happen
        // while Paused - Section 3: Pause is unavailable during SUCCESS).
        public void ServerForceUnpause()
        {
            if (!IsServer) return;
            if (!_isPaused.Value) return;
            _isPaused.Value = false;
        }

        // Section 35: guarantee Time.timeScale == 1 before any Scene Transition / Network
        // Shutdown, independent of whether the Resume RPC round-trip has completed yet - a
        // Client that just confirmed "Return to Title" cannot afford to wait on a Server
        // round-trip before it is safe to leave. Local-only, does not touch the synced
        // NetworkVariable (the Server is still the one who eventually clears it, or the whole
        // Session/Object goes away with the Scene anyway).
        public static void ForceLocalUnpauseImmediate()
        {
            Time.timeScale = 1f;
        }
    }
}
