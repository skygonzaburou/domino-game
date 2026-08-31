using UnityEngine;
using UnityEngine.InputSystem;

namespace Domino.CameraSystem
{
    public class ThirdPersonCamera : MonoBehaviour
    {
        public static ThirdPersonCamera Instance { get; private set; }

        [SerializeField] private float distance = 4f;
        [SerializeField] private float pitchMin = -30f;
        [SerializeField] private float pitchMax = 60f;
        [SerializeField] private float sensitivity = 2f;
        [SerializeField] private bool lockCursor = true;

        [Header("Placement View")]
        [SerializeField] private float placementSideDistance = 2.2f;
        [SerializeField] private float placementHeight = 1.3f;
        [SerializeField] private float placementLookHeight = 0.6f;
        [SerializeField] private float placementBlendSpeed = 3f;

        private Transform _target;
        private float _yaw;
        private float _pitch = 15f;

        private bool _inPlacementMode;
        private Transform _placementTarget;
        private float _placementBlend;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            if (lockCursor)
            {
                Cursor.lockState = CursorLockMode.Locked;
            }

            if (_target == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    Transform followPoint = player.transform.Find("CameraTarget");
                    SetTarget(followPoint != null ? followPoint : player.transform);
                }
            }
        }

        public void SetTarget(Transform target)
        {
            _target = target;
            if (_target != null)
            {
                _yaw = _target.eulerAngles.y;
            }
        }

        public void EnterPlacementView(Transform placementTarget)
        {
            _inPlacementMode = true;
            _placementTarget = placementTarget;
        }

        public void ExitPlacementView()
        {
            _inPlacementMode = false;
        }

        private void LateUpdate()
        {
            if (_target == null) return;

            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                _yaw += delta.x * sensitivity * 0.1f;
                _pitch -= delta.y * sensitivity * 0.1f;
                _pitch = Mathf.Clamp(_pitch, pitchMin, pitchMax);
            }

            Quaternion normalRotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 normalFocusPoint = _target.position;
            Vector3 normalPosition = normalFocusPoint - normalRotation * Vector3.forward * distance;

            float blendTarget = _inPlacementMode ? 1f : 0f;
            _placementBlend = Mathf.MoveTowards(_placementBlend, blendTarget, Time.deltaTime * placementBlendSpeed);

            if (_placementBlend <= 0f || _placementTarget == null)
            {
                transform.SetPositionAndRotation(normalPosition, normalRotation);
                return;
            }

            Vector3 lookPoint = _placementTarget.position + Vector3.up * placementLookHeight;
            Vector3 placementPosition = lookPoint
                + _placementTarget.right * placementSideDistance
                + Vector3.up * placementHeight;
            Quaternion placementRotation = Quaternion.LookRotation(lookPoint - placementPosition, Vector3.up);

            Vector3 finalPosition = Vector3.Lerp(normalPosition, placementPosition, _placementBlend);
            Quaternion finalRotation = Quaternion.Slerp(normalRotation, placementRotation, _placementBlend);
            transform.SetPositionAndRotation(finalPosition, finalRotation);
        }
    }
}
