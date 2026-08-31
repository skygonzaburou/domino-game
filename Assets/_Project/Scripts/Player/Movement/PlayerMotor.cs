using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Domino.CameraSystem;

namespace Domino.Player
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(PlayerStats))]
    public class PlayerMotor : NetworkBehaviour
    {
        [SerializeField] private float groundCheckDistance = 0.25f;
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private Transform cameraTarget;

        private Rigidbody _rigidbody;
        private PlayerStats _stats;
        private Vector2 _moveAxis;
        private bool _dashHeld;
        private bool _jumpRequested;
        private bool _isGrounded;
        private bool _movementEnabled = true;

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
            _rigidbody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
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
            ApplyMovement();
            ApplyJump();
        }

        private void CheckGrounded()
        {
            Vector3 origin = transform.position + Vector3.up * 0.1f;
            _isGrounded = Physics.Raycast(origin, Vector3.down, groundCheckDistance + 0.1f, groundMask, QueryTriggerInteraction.Ignore);
        }

        private void ApplyMovement()
        {
            Transform view = ThirdPersonCamera.Instance != null ? ThirdPersonCamera.Instance.transform : transform;
            Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(view.right, Vector3.up).normalized;

            Vector3 direction = forward * _moveAxis.y + right * _moveAxis.x;
            float speed = _stats.moveSpeed * (_dashHeld ? _stats.dashMultiplier : 1f);

            Vector3 targetVelocity = direction * speed;
            targetVelocity.y = _rigidbody.linearVelocity.y;
            _rigidbody.linearVelocity = targetVelocity;

            if (direction.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
                _rigidbody.MoveRotation(Quaternion.RotateTowards(_rigidbody.rotation, targetRotation, _stats.rotationSpeed * Time.fixedDeltaTime));
            }
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
