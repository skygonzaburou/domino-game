using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Domino.CameraSystem;
using Domino.DominoSystem;
using Domino.GameFlow;

namespace Domino.Player
{
    [RequireComponent(typeof(PlayerMotor))]
    public class PlacementController : NetworkBehaviour
    {
        [Header("Input")]
        [SerializeField] private Key enterPlacementKey = Key.E;
        [SerializeField] private Key cancelKey = Key.Escape;
        [SerializeField] private Key rotateLeftKey = Key.A;
        [SerializeField] private Key rotateRightKey = Key.D;

        [Header("References")]
        [SerializeField] private GameObject ghostDominoPrefab;
        [SerializeField] private GameObject placementMarkerPrefab;

        [Header("Placement Position (Step 7.8: Character正面基準)")]
        [Tooltip("Character(Nose)正面、何m先に配置候補位置を出すか")]
        [SerializeField] private float placementDistance = 1.2f;
        [SerializeField] private float hoverHeight = 0.15f;
        [SerializeField] private float groundRaycastRange = 3f;
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private float dominoRestingHeight = 0.5f;

        [Header("Domino Yaw Rotation (Step 7.8)")]
        [Tooltip("Placement Mode中、A/Dキーを押し続けた時のDomino回転速度(度/秒)")]
        [SerializeField] private float yawRotationSpeed = 135f;

        private PlayerMotor _motor;
        private GameObject _ghostInstance;
        private GhostDomino _ghostDomino;
        private GameObject _markerInstance;
        private PlacementMarker _placementMarker;
        private bool _isPlacing;
        private bool _markerActive;
        private bool _markerLocked;

        // Yaw adjustment applied on top of _placementBaseRotation while in Placement Mode.
        // Reset to 0 every time Placement Mode is (re-)entered, so Yaw always starts aligned
        // with the Character/Nose facing direction at that moment (spec: Initial Yaw = Nose
        // Forward), and never carries over between separate placements.
        private float _yawOffsetDegrees;
        private Quaternion _placementBaseRotation;

        // Single source of truth for where a Domino would go: computed once by
        // ComputeGroundAnchor()/EnterPlacementMode(), then reused as-is by the Marker, the
        // Ghost (with its own hoverHeight offset for the wobble pivot) and the RPC sent
        // on Confirm - none of them ever recompute this independently.
        private Vector3 _anchorGroundPosition;
        private Quaternion _anchorFlatRotation;
        private Vector3 _anchorGroundNormal;

        private void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner)
            {
                enabled = false;
                return;
            }
        }

        private void Update()
        {
            if (!IsOwner) return;

            if (GameFlowManager.Instance != null && GameFlowManager.Instance.CurrentPhase != GamePhase.BUILDING)
            {
                if (_isPlacing) CancelPlacement();
                else if (_markerActive) HideMarker();
                _markerLocked = false;
                return;
            }

            if (!_isPlacing)
            {
                // Live Preview: as long as the marker isn't locked, it continuously
                // follows the Character's current front-facing candidate every frame.
                if (!_markerLocked)
                {
                    ComputeGroundAnchor();
                    ShowMarker(MarkerState.Preview);
                }

                var mouse = Mouse.current;
                if (mouse != null && mouse.rightButton.wasPressedThisFrame)
                {
                    // Always recompute so a second Right Click re-locks to the
                    // current live position rather than the old locked one.
                    ComputeGroundAnchor();
                    _markerLocked = true;
                    ShowMarker(MarkerState.Locked);
                }

                if (Keyboard.current != null && Keyboard.current[enterPlacementKey].wasPressedThisFrame)
                {
                    EnterPlacementMode();
                }
                return;
            }

            // Placement Mode: A/D freely rotate the Domino's Yaw (Player movement is already
            // disabled here via _motor.SetMovementEnabled(false), so A/D are unambiguous).
            if (Keyboard.current != null)
            {
                float yawInput = 0f;
                if (Keyboard.current[rotateLeftKey].isPressed) yawInput -= 1f;
                if (Keyboard.current[rotateRightKey].isPressed) yawInput += 1f;
                if (yawInput != 0f)
                {
                    _yawOffsetDegrees += yawInput * yawRotationSpeed * Time.deltaTime;
                    ApplyPlacementRotation();
                }
            }

            var m = Mouse.current;
            if (m != null && m.leftButton.wasPressedThisFrame)
            {
                ConfirmPlacement();
                return;
            }

            if (Keyboard.current != null && Keyboard.current[cancelKey].wasPressedThisFrame)
            {
                CancelPlacement();
            }
        }

        // Character-front raycast: Player Position + Player Forward * placementDistance, then
        // straight down to the Stage Surface. Camera orientation is never consulted here -
        // only where the Character/Nose is facing decides the candidate position (Step 7.8;
        // supersedes Step 7.7's Camera Aim approach). groundMask excludes the Domino and
        // Player Layers (Step 7.1/7.7) so already-placed Dominoes and Player bodies are never
        // mistaken for ground.
        private void ComputeGroundAnchor()
        {
            Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 0.0001f) flatForward = Vector3.forward;

            Vector3 basePos = transform.position + flatForward * placementDistance;
            float groundY = transform.position.y;
            Vector3 groundNormal = Vector3.up;

            Vector3 rayOrigin = basePos + Vector3.up * groundRaycastRange * 0.5f;
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, groundRaycastRange, groundMask, QueryTriggerInteraction.Ignore))
            {
                groundY = hit.point.y;
                groundNormal = hit.normal;
            }

            _anchorGroundPosition = new Vector3(basePos.x, groundY, basePos.z);
            _anchorFlatRotation = Quaternion.LookRotation(flatForward, Vector3.up);
            _anchorGroundNormal = groundNormal;
        }

        private void ShowMarker(MarkerState state)
        {
            EnsureMarkerInstance();
            Quaternion slopeAlign = Quaternion.FromToRotation(Vector3.up, _anchorGroundNormal);
            _markerInstance.transform.SetPositionAndRotation(_anchorGroundPosition, slopeAlign * _anchorFlatRotation);
            _markerInstance.SetActive(true);
            _markerActive = true;
            if (_placementMarker != null) _placementMarker.SetState(state);
        }

        // Applies the current Yaw-adjusted rotation to both the Ghost (via SetBaseRotation,
        // so its own Wobble keeps animating on top around the now-rotated local axis) and the
        // locked Marker (position untouched, rotation only) - "Markerも同じYawへ回転".
        private void ApplyPlacementRotation()
        {
            Quaternion currentRotation = _placementBaseRotation * Quaternion.AngleAxis(_yawOffsetDegrees, Vector3.up);

            if (_ghostDomino != null) _ghostDomino.SetBaseRotation(currentRotation);

            if (_markerInstance != null)
            {
                Quaternion slopeAlign = Quaternion.FromToRotation(Vector3.up, _anchorGroundNormal);
                _markerInstance.transform.rotation = slopeAlign * currentRotation;
            }
        }

        private void EnsureMarkerInstance()
        {
            if (_markerInstance != null) return;
            _markerInstance = Instantiate(placementMarkerPrefab);
            _placementMarker = _markerInstance.GetComponent<PlacementMarker>();
            _markerInstance.SetActive(false);
        }

        private void HideMarker()
        {
            if (_markerInstance != null) _markerInstance.SetActive(false);
            _markerActive = false;
        }

        private void EnterPlacementMode()
        {
            if (!_markerLocked)
            {
                // Bare E with no prior Right Click: lock to whatever the Live
                // Preview was showing this frame (already computed above).
                _markerLocked = true;
                ShowMarker(MarkerState.Locked);
            }

            _isPlacing = true;
            _yawOffsetDegrees = 0f;
            _placementBaseRotation = _anchorFlatRotation;
            _motor.SetMovementEnabled(false);

            Vector3 spawnPosition = _anchorGroundPosition + Vector3.up * hoverHeight;

            EnsureGhostInstance();
            _ghostInstance.transform.SetPositionAndRotation(spawnPosition, _placementBaseRotation);
            _ghostInstance.SetActive(true);
            _ghostDomino.ResetWobble();

            if (ThirdPersonCamera.Instance != null)
            {
                ThirdPersonCamera.Instance.EnterPlacementView(_ghostInstance.transform);
            }
        }

        private void EnsureGhostInstance()
        {
            if (_ghostInstance != null) return;

            _ghostInstance = Instantiate(ghostDominoPrefab);
            _ghostInstance.SetActive(false);
            _ghostDomino = _ghostInstance.GetComponent<GhostDomino>();
        }

        private void ConfirmPlacement()
        {
            // Ghost/Marker Y is the wobble pivot height (hoverHeight above ground), not a
            // physically correct resting height for a real Domino. Use the anchor's ground
            // Y plus the Domino's own resting height instead - this only affects Y; X/Z are
            // still used exactly as-is. Rotation (Yaw + Wobble Tilt) is taken from the Ghost's
            // current rotation with no correction whatsoever, preserving the Step 5 design
            // that placement timing/orientation directly determines the result.
            Vector3 position = new Vector3(_anchorGroundPosition.x, _anchorGroundPosition.y + dominoRestingHeight, _anchorGroundPosition.z);
            Quaternion rotation = _ghostDomino.CurrentRotation;

            RequestPlacementRpc(position, rotation);

            ExitPlacementMode();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestPlacementRpc(Vector3 position, Quaternion rotation, RpcParams rpcParams = default)
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;
            DominoSpawner.Instance.TryPlaceDomino(position, rotation, senderClientId);
        }

        private void CancelPlacement()
        {
            ExitPlacementMode();
        }

        private void ExitPlacementMode()
        {
            _isPlacing = false;
            _yawOffsetDegrees = 0f;
            if (_ghostInstance != null)
            {
                _ghostInstance.SetActive(false);
            }
            HideMarker();
            // Unlock so, if still BUILDING, next frame's Update() immediately
            // resumes following the Character's front-facing position again.
            _markerLocked = false;
            _motor.SetMovementEnabled(true);

            if (ThirdPersonCamera.Instance != null)
            {
                ThirdPersonCamera.Instance.ExitPlacementView();
            }
        }
    }
}
