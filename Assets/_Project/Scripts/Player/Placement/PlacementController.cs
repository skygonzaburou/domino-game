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

        [Header("References")]
        [SerializeField] private GameObject ghostDominoPrefab;
        [SerializeField] private GameObject placementMarkerPrefab;

        [Header("Placement Position")]
        [SerializeField] private float forwardDistance = 1.2f;
        [SerializeField] private float hoverHeight = 0.15f;
        [SerializeField] private float groundRaycastRange = 3f;
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private float dominoRestingHeight = 0.5f;
        [Tooltip("Live Preview中、Player Forwardの変化にAnchorが追従する最大速度(度/秒)。PlayerStats.rotationSpeed(720)より意図的に低く抑え、Camera操作をしながら移動キーを押した際にPlayerの体が素早く向きを変えても、Markerがそれにつられて激しく振れ回らないようにする")]
        [SerializeField] private float previewFollowSpeed = 240f;

        private PlayerMotor _motor;
        private GameObject _ghostInstance;
        private GhostDomino _ghostDomino;
        private GameObject _markerInstance;
        private PlacementMarker _placementMarker;
        private bool _isPlacing;
        private bool _markerActive;
        private bool _markerLocked;

        // Smoothed stand-in for the player's raw flat-forward direction, used only to
        // drive the Live Preview anchor. The player's own body can reorient near-instantly
        // (rotationSpeed=720deg/s in PlayerStats) to chase the camera-relative move
        // direction while a movement key is held; without this, ComputeGroundAnchor()
        // would snap the Marker to match every frame, which reads as the Marker orbiting
        // the player during a fast camera spin. Reset (not eased) whenever Live Preview
        // is about to resume, so the very first frame after Lock/Cancel/Confirm/BUILDING
        // re-entry is exact, and only frame-to-frame tracking after that is damped.
        private Vector3 _previewForward = Vector3.forward;
        private bool _previewForwardValid;

        // Single source of truth for where a Domino would go: computed once by
        // UpdateMarker()/EnterPlacementMode(), then reused as-is by the Marker, the
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
                _previewForwardValid = false;
                return;
            }

            if (!_isPlacing)
            {
                // Live Preview: as long as the marker isn't locked, it continuously
                // follows the player's current placement candidate every frame.
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

        private void ComputeGroundAnchor()
        {
            Vector3 targetFlatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            if (targetFlatForward.sqrMagnitude < 0.0001f) targetFlatForward = Vector3.forward;

            if (!_previewForwardValid)
            {
                _previewForward = targetFlatForward;
                _previewForwardValid = true;
            }
            else
            {
                float maxRadiansDelta = previewFollowSpeed * Mathf.Deg2Rad * Time.deltaTime;
                _previewForward = Vector3.RotateTowards(_previewForward, targetFlatForward, maxRadiansDelta, 0f).normalized;
            }

            Vector3 flatForward = _previewForward;
            Vector3 basePos = transform.position + flatForward * forwardDistance;
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
            _motor.SetMovementEnabled(false);

            Vector3 spawnPosition = _anchorGroundPosition + Vector3.up * hoverHeight;

            EnsureGhostInstance();
            _ghostInstance.transform.SetPositionAndRotation(spawnPosition, _anchorFlatRotation);
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
            // Y plus the Domino's own resting height instead - this only affects Y; X/Z and
            // rotation are still used exactly as-is, so "no angle correction" is untouched.
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
            if (_ghostInstance != null)
            {
                _ghostInstance.SetActive(false);
            }
            HideMarker();
            // Unlock so, if still BUILDING, next frame's Update() immediately
            // resumes the Live Preview following the player again.
            _markerLocked = false;
            _previewForwardValid = false;
            _motor.SetMovementEnabled(true);

            if (ThirdPersonCamera.Instance != null)
            {
                ThirdPersonCamera.Instance.ExitPlacementView();
            }
        }
    }
}
