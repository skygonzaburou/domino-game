using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using Domino.GameFlow;

namespace Domino.Player
{
    // Step 12: Visual-only Animation driver for GreenBoy. Only ever READS existing Gameplay
    // State (PlayerMotor's Move Input/Grounded/Jumped, GameFlowManager.CurrentPhase) and writes
    // Animator parameters - never writes back into Gameplay Logic (Section 19). Runs only on the
    // Owner (mirrors PlayerMotor.ReadInput's own IsOwner gating); NetworkAnimator (AuthorityMode
    // = Owner, on GreenBoy's Model) replicates the resulting Animator parameter/state to every
    // other Client automatically, so no NetworkVariable/RPC is added here (Section 20/21).
    [RequireComponent(typeof(PlayerMotor))]
    public class PlayerAnimationController : NetworkBehaviour
    {
        [SerializeField] private Animator animator;

        [Header("Idle3 -> Idle8 (Section 10)")]
        [Tooltip("この秒数以上静止し続けるとIdle8へ遷移する")]
        [SerializeField] private float idleLongThreshold = 3f;

        [Header("Slow Walk Playback Speed (Section 11)")]
        [Tooltip("Ctrl Slow Walk中、Walking Clipの再生速度に掛けるMultiplier。Gameplay Movement Speed自体は変更しない")]
        [SerializeField] private float slowWalkAnimatorSpeed = 0.55f;
        [SerializeField] private float normalAnimatorSpeed = 1f;

        private static readonly int SpeedParam = UnityEngine.Animator.StringToHash("Speed");
        private static readonly int IsRunningParam = UnityEngine.Animator.StringToHash("IsRunning");
        private static readonly int IsSlowWalkParam = UnityEngine.Animator.StringToHash("IsSlowWalk");
        private static readonly int IsIdleLongParam = UnityEngine.Animator.StringToHash("IsIdleLong");
        private static readonly int GroundedParam = UnityEngine.Animator.StringToHash("Grounded");
        private static readonly int IsVictoryParam = UnityEngine.Animator.StringToHash("IsVictory");
        private const string JumpTriggerParam = "JumpTrigger";

        private PlayerMotor _motor;
        private NetworkAnimator _networkAnimator;
        private float _idleTimer;

        private void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
            _networkAnimator = animator != null ? animator.GetComponent<NetworkAnimator>() : null;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner) return;
            _motor.Jumped += HandleJumped;
        }

        public override void OnNetworkDespawn()
        {
            if (!IsOwner) return;
            _motor.Jumped -= HandleJumped;
        }

        private void Update()
        {
            if (!IsOwner || animator == null) return;

            bool moving = _motor.MoveAxis.sqrMagnitude > 0.0001f;
            // Section 11: Ctrl(Slow Walk)takes priority over Shift(Run)when both held - mirrors
            // PlayerMotor.ApplyMovement's own speedMultiplier priority exactly.
            bool isSlowWalk = moving && _motor.IsSlowWalkHeld;
            bool isRunning = moving && !_motor.IsSlowWalkHeld && _motor.IsDashHeld;

            if (moving)
            {
                _idleTimer = 0f;
            }
            else
            {
                _idleTimer += Time.deltaTime;
            }
            bool isIdleLong = !moving && _idleTimer >= idleLongThreshold;

            bool isVictory = GameFlowManager.Instance != null && GameFlowManager.Instance.CurrentPhase == GamePhase.SUCCESS;

            animator.SetFloat(SpeedParam, moving ? 1f : 0f);
            animator.SetBool(IsRunningParam, isRunning);
            animator.SetBool(IsSlowWalkParam, isSlowWalk);
            animator.SetBool(IsIdleLongParam, isIdleLong);
            animator.SetBool(GroundedParam, _motor.IsGrounded);
            animator.SetBool(IsVictoryParam, isVictory);

            animator.speed = isSlowWalk ? slowWalkAnimatorSpeed : normalAnimatorSpeed;
        }

        // Section 13/14: PlayerMotor.Jumped fires exactly once per real Jump (already gated on
        // Grounded at the moment of the actual Space press - see PlayerMotor.ReadInput), so no
        // extra condition is needed here. Routed through NetworkAnimator.SetTrigger (not
        // Animator.SetTrigger directly) so the one-shot Trigger reliably networks to other
        // Clients, per NGO's NetworkAnimator usage contract.
        private void HandleJumped()
        {
            if (_networkAnimator != null)
            {
                _networkAnimator.SetTrigger(JumpTriggerParam);
            }
            else
            {
                animator.SetTrigger(JumpTriggerParam);
            }
        }
    }
}
