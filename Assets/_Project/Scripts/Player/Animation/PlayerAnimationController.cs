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
    // = Owner) replicates the resulting Animator parameter/state to every other Client
    // automatically, so no NetworkVariable/RPC is added here (Section 20/21).
    //
    // Step 12.1.2: NetworkAnimator lives on the Player Root (sibling of this component), NOT on
    // GreenBoy's Model, even though its Animator reference still points at GreenBoy's Animator
    // there. Root Cause this fixes: NGO's NetworkObject.InvokeBehaviourNetworkSpawn() skips
    // OnNetworkSpawn() for any child NetworkBehaviour whose GameObject is not
    // activeInHierarchy at the exact moment Spawn runs (logged as a Warning, not an Error - easy
    // to miss). PlayerCharacterVisual's own OnNetworkSpawn() applies its NetworkVariable's
    // *default* value (Character 1/Capsule) immediately, before GameplayPlayerSpawner has a
    // chance to call ServerSetCharacterId() with the real resolved Id - so for any Character
    // 2-20 selection, GreenBoyVisual (and the NetworkAnimator that used to live under it) was
    // toggled Inactive during that very same Spawn pass, permanently skipping NetworkAnimator's
    // own OnNetworkSpawn() and leaving its internal state handler null for the rest of that
    // NetworkObject's lifetime (surfacing as a NullReferenceException from inside NetworkAnimator
    // itself the first time SetTrigger() was called, and Continuous Animator Parameter sync never
    // starting for Remote observers at all). Since the Player Root is always active regardless of
    // which Character Visual is currently shown, keeping NetworkAnimator there guarantees its own
    // OnNetworkSpawn() always runs.
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

            // Step 12.1.2: NetworkAnimator now lives on the Player Root (see field/class comment
            // above) precisely so it is always active in hierarchy at Spawn time - it is no
            // longer a sibling of animator, so it must be looked up on this GameObject instead.
            _networkAnimator = GetComponent<NetworkAnimator>();
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
            // Step 12.1: Character 1 (Capsule) has no Animator to drive at all - GreenBoyVisual
            // (and its Animator) is simply Inactive in that case (PlayerCharacterVisual). This
            // check is character-agnostic (it only ever asks "is the Animator I already have a
            // reference to currently active"), so it needs no knowledge of which Character is
            // selected and keeps working unmodified for any future Character this Animator
            // reference might point at.
            if (!IsOwner || animator == null || !animator.gameObject.activeInHierarchy) return;

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
