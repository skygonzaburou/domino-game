using UnityEngine;
using UnityEngine.InputSystem;
using Domino.GameFlow;

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

        [Header("Overview Camera (Step 7.6)")]
        [Tooltip("Normal/Placement View から Overview View への遷移速度。値が大きいほど速い(既定2 = 約0.5秒で遷移)")]
        [SerializeField] private float overviewBlendSpeed = 2f;

        private Transform _target;
        private float _yaw;
        private float _pitch = 15f;

        private bool _inPlacementMode;
        private Transform _placementTarget;
        private float _placementBlend;

        // Step 14.2: +1 = Placement View sits on the Target's right (original/default
        // behaviour), -1 = mirrored to the left. Persists across Placement sessions as the
        // "previous side" fallback (Section 4) rather than resetting every time, so a
        // near-center Entry never has to guess from scratch.
        private float _placementSideSign = 1f;

        private Camera _camera;
        private float _normalFieldOfView;
        private float _overviewBlend;

        private void Awake()
        {
            Instance = this;
            _camera = GetComponent<Camera>();
            if (_camera != null) _normalFieldOfView = _camera.fieldOfView;
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

            // Step 14.2 (Section 3/4): decide once, right now, which side the already-orbiting
            // Camera is currently viewing the Player from - never re-evaluated on later frames
            // while still in Placement Mode, so the close-up view cannot flip mid-Placement.
            // transform.position here is still last frame's LateUpdate result (Update() always
            // runs before LateUpdate() in the same frame), i.e. exactly "where the Camera was
            // an instant before E was pressed".
            float side = Vector3.Dot(transform.position - placementTarget.position, placementTarget.right);
            const float sideDeadZone = 0.05f;
            if (Mathf.Abs(side) > sideDeadZone)
            {
                _placementSideSign = Mathf.Sign(side);
            }
            // Section 4: too close to dead-center to tell confidently - keep whichever side was
            // last determined (defaults to Right/+1 the very first time, matching the original,
            // pre-Step-14.2 behaviour).
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

            float placementBlendTarget = _inPlacementMode ? 1f : 0f;
            _placementBlend = Mathf.MoveTowards(_placementBlend, placementBlendTarget, Time.deltaTime * placementBlendSpeed);

            Vector3 basePosition;
            Quaternion baseRotation;

            if (_placementBlend <= 0f || _placementTarget == null)
            {
                basePosition = normalPosition;
                baseRotation = normalRotation;
            }
            else
            {
                Vector3 lookPoint = _placementTarget.position + Vector3.up * placementLookHeight;
                Vector3 placementPosition = lookPoint
                    + _placementTarget.right * (placementSideDistance * _placementSideSign)
                    + Vector3.up * placementHeight;
                Quaternion placementRotation = Quaternion.LookRotation(lookPoint - placementPosition, Vector3.up);

                basePosition = Vector3.Lerp(normalPosition, placementPosition, _placementBlend);
                baseRotation = Quaternion.Slerp(normalRotation, placementRotation, _placementBlend);
            }

            bool wantOverview = GameFlowManager.Instance != null && GameFlowManager.Instance.IsChallengeActive
                && OverviewCameraPoint.Instance != null;
            float overviewBlendTarget = wantOverview ? 1f : 0f;
            _overviewBlend = Mathf.MoveTowards(_overviewBlend, overviewBlendTarget, Time.deltaTime * overviewBlendSpeed);

            Vector3 finalPosition = basePosition;
            Quaternion finalRotation = baseRotation;
            float finalFieldOfView = _normalFieldOfView;

            if (_overviewBlend > 0f && OverviewCameraPoint.Instance != null)
            {
                Transform overview = OverviewCameraPoint.Instance.transform;
                finalPosition = Vector3.Lerp(basePosition, overview.position, _overviewBlend);
                finalRotation = Quaternion.Slerp(baseRotation, overview.rotation, _overviewBlend);
                finalFieldOfView = Mathf.Lerp(_normalFieldOfView, OverviewCameraPoint.Instance.FieldOfView, _overviewBlend);
            }

            transform.SetPositionAndRotation(finalPosition, finalRotation);
            if (_camera != null) _camera.fieldOfView = finalFieldOfView;
        }
    }
}
