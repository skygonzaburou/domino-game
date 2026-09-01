using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Domino.CameraSystem;
using Domino.GameFlow;

namespace Domino.Player
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(PlayerStats))]
    public class PlayerMotor : NetworkBehaviour
    {
        [SerializeField] private float groundCheckDistance = 0.25f;
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private Transform cameraTarget;

        [Header("Collision Push (Step 8A)")]
        [Tooltip("Push velocity(m/秒)がこの値を超えないようクランプする。連続Pushで際限なく加速しないための上限")]
        [SerializeField] private float maxPushSpeed = 6f;
        [Tooltip("Push velocityの減衰速度(m/秒^2)。この値が大きいほど早く元の動きに戻る")]
        [SerializeField] private float pushVelocityDecay = 8f;

        private Rigidbody _rigidbody;
        private PlayerStats _stats;
        private Vector2 _moveAxis;
        private bool _dashHeld;
        private bool _jumpRequested;
        private bool _isGrounded;
        private bool _movementEnabled = true;

        // External horizontal velocity from Player-vs-Player collision push (Step 8A). Additive
        // on top of movement-driven targetVelocity rather than overwriting it, and decays every
        // FixedUpdate - this is what lets a push compose with normal WASD movement instead of
        // being instantly discarded by ApplyMovement's unconditional linearVelocity assignment.
        private Vector3 _pushVelocity;

        public void SetMovementEnabled(bool enabled)
        {
            _movementEnabled = enabled;
            if (!enabled)
            {
                _moveAxis = Vector2.zero;
                _dashHeld = false;
                _jumpRequested = false;

                Vector3 velocity = _rigidbody.linearVelocity;
                velocity.x = 0f;
                velocity.z = 0f;
                _rigidbody.linearVelocity = velocity;
            }
        }

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _stats = GetComponent<PlayerStats>();

            // Freeze ALL physics-driven rotation (Step 8A). Previously only X/Z were frozen,
            // leaving Y free for the physics solver - a collision with Stage geometry/Dominoes/
            // other Players could impart angular velocity around Y that nothing ever cancelled
            // (AngularDamping is only 0.05), so the Player would keep spinning indefinitely once
            // hit. Freezing Y here does NOT block ApplyMovement()'s own Rigidbody.MoveRotation()
            // calls below - MoveRotation is a scripted/kinematic-style move that Unity applies
            // regardless of rotational constraints, which only gate the physics solver's own
            // torque/collision response. So Yaw stays exclusively under this script's control.
            _rigidbody.constraints = RigidbodyConstraints.FreezeRotationX
                | RigidbodyConstraints.FreezeRotationY
                | RigidbodyConstraints.FreezeRotationZ;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner) return;

            if (ThirdPersonCamera.Instance != null)
            {
                Transform followPoint = cameraTarget != null ? cameraTarget : transform;
                ThirdPersonCamera.Instance.SetTarget(followPoint);
            }
        }

        private void Update()
        {
            if (!IsOwner || !_movementEnabled) return;
            ReadInput();
        }

        private void ReadInput()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            float z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            _moveAxis = Vector2.ClampMagnitude(new Vector2(x, z), 1f);
            _dashHeld = keyboard.leftShiftKey.isPressed;

            if (keyboard.spaceKey.wasPressedThisFrame && _isGrounded)
            {
                _jumpRequested = true;
            }
        }

        private void FixedUpdate()
        {
            if (!IsOwner || !_movementEnabled) return;

            CheckGrounded();
            DecayPushVelocity();
            ApplyMovement();
            ApplyJump();
        }

        private void DecayPushVelocity()
        {
            _pushVelocity = Vector3.MoveTowards(_pushVelocity, Vector3.zero, pushVelocityDecay * Time.fixedDeltaTime);
        }

        // Called on the target Player's own Owner client (Server-validated - see
        // PlayerPushController) to apply a Player-vs-Player collision push. Only ever adds a
        // horizontal velocity: never composes into something that lifts a Player off the
        // ground, and is clamped so repeated pushes cannot accelerate without bound.
        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        public void ApplyPushRpc(Vector3 horizontalVelocity)
        {
            horizontalVelocity.y = 0f;
            _pushVelocity = Vector3.ClampMagnitude(_pushVelocity + horizontalVelocity, maxPushSpeed);
        }

        private void CheckGrounded()
        {
            Vector3 origin = transform.position + Vector3.up * 0.1f;
            _isGrounded = Physics.Raycast(origin, Vector3.down, groundCheckDistance + 0.1f, groundMask, QueryTriggerInteraction.Ignore);
        }

        private void ApplyMovement()
        {
            bool confined = IsConfinedToStartArea();

            // Screen-relative movement basis: always derived from the currently-active Camera
            // transform (Third Person during BUILDING/READY, Overview once RUNNING's blend
            // completes - Step 7.7), ground-projected so W/A/S/D map to up/left/down/right on
            // screen regardless of which camera is active. BUILDING's feel is unchanged since
            // its camera never pitches steeply; Overview (Stage-authored, can be steep) gets a
            // fallback via the camera's Up axis, which is guaranteed horizontal exactly when
            // Forward's ground projection collapses (near-straight-down/up look).
            Vector3 forward;
            Vector3 right;
            if (ThirdPersonCamera.Instance != null)
            {
                Transform cam = ThirdPersonCamera.Instance.transform;
                forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
                if (forward.sqrMagnitude < 0.0001f)
                {
                    forward = Vector3.ProjectOnPlane(cam.up, Vector3.up);
                }
                forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
                right = Vector3.Cross(Vector3.up, forward);
            }
            else
            {
                forward = transform.forward;
                right = transform.right;
            }

            Vector3 direction = forward * _moveAxis.y + right * _moveAxis.x;
            float speed = _stats.moveSpeed * (_dashHeld ? _stats.dashMultiplier : 1f);

            Vector3 targetVelocity = direction * speed + _pushVelocity;
            targetVelocity.y = _rigidbody.linearVelocity.y;

            if (confined)
            {
                targetVelocity = ConstrainToStartArea(targetVelocity);
            }

            _rigidbody.linearVelocity = targetVelocity;

            if (direction.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
                _rigidbody.MoveRotation(Quaternion.RotateTowards(_rigidbody.rotation, targetRotation, _stats.rotationSpeed * Time.fixedDeltaTime));
            }
        }

        // Step 8.5: deliberately IsMovementConfined, not IsChallengeActive - SUCCESS keeps the
        // Overview Camera (which does key off IsChallengeActive) but releases movement so
        // Players can walk the Stage they built. See GameFlowManager.IsMovementConfined.
        private static bool IsConfinedToStartArea()
        {
            return GameFlowManager.Instance != null && GameFlowManager.Instance.IsMovementConfined;
        }

        // Clamps the horizontal component of targetVelocity so the Player's next FixedUpdate
        // position never leaves the Start Area's local-space bounds. This only ever shrinks
        // the outward-pointing part of the velocity (never adds force), so it composes safely
        // with Rigidbody physics and needs no per-frame Teleport - a Player already resting
        // exactly on the boundary (e.g. right after RUNNING begins) is nudged fully inside by
        // the same clamp instead of a separate one-off correction.
        private Vector3 ConstrainToStartArea(Vector3 targetVelocity)
        {
            StartAreaZone startArea = GameFlowManager.Instance.StartArea;
            if (startArea == null) return targetVelocity;

            Vector3 currentPos = _rigidbody.position;
            Vector3 candidatePos = currentPos + new Vector3(targetVelocity.x, 0f, targetVelocity.z) * Time.fixedDeltaTime;
            Vector3 clampedPos = startArea.ClampToLocalBounds(candidatePos);

            Vector3 allowedVelocity = (clampedPos - currentPos) / Time.fixedDeltaTime;
            targetVelocity.x = allowedVelocity.x;
            targetVelocity.z = allowedVelocity.z;
            return targetVelocity;
        }

        private void ApplyJump()
        {
            if (!_jumpRequested) return;
            _jumpRequested = false;
            Vector3 velocity = _rigidbody.linearVelocity;
            velocity.y = _stats.jumpForce;
            _rigidbody.linearVelocity = velocity;
        }
    }
}
