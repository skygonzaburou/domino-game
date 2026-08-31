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

        [Header("Placement Position (Step 7.7: Camera Aim基準)")]
        [Tooltip("Player中心からの水平距離で、実際に配置できる範囲(DominoSpawnerの距離Validationと一致させること)")]
        [SerializeField] private float maxPlacementRange = 3f;
        [SerializeField] private float hoverHeight = 0.15f;
        [Tooltip("画面中央のCamera Aim Raycastの最大距離(奥のStage Surfaceを検出するため)")]
        [SerializeField] private float cameraAimRange = 50f;
        [Tooltip("Range Clamp後のXZ位置で、地面の高さ・傾きを再検出する垂直Raycastの距離")]
        [SerializeField] private float groundRaycastRange = 3f;
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private float dominoRestingHeight = 0.5f;

        private PlayerMotor _motor;
        private GameObject _ghostInstance;
        private GhostDomino _ghostDomino;
        private GameObject _markerInstance;
        private PlacementMarker _placementMarker;
        private bool _isPlacing;
        private bool _markerActive;
        private bool _markerLocked;
        private GUIStyle _reticleStyle;

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
                // follows the current Camera Aim every frame.
                if (!_markerLocked)
                {
                    ComputeGroundAnchor();
                    ShowMarker(MarkerState.Preview);
                }

                var mouse = Mouse.current;
                if (mouse != null && mouse.rightButton.wasPressedThisFrame)
                {
                    // Always recompute so a second Right Click re-locks to the
                    // current Camera Aim position rather than the old locked one.
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

        // Screen-center ("Camera Aim") raycast against the Stage Surface, range-clamped around
        // the Player so aiming far away never proposes a spot DominoSpawner's own distance
        // Validation would reject. groundMask excludes the Domino and Player Layers (Step 7.1/
        // 7.7) so already-placed Dominoes and Player bodies are never mistaken for ground.
        private void ComputeGroundAnchor()
        {
            Camera cam = ThirdPersonCamera.Instance != null ? ThirdPersonCamera.Instance.GetComponent<Camera>() : null;
            Vector3 rayOrigin = cam != null ? cam.transform.position : transform.position + Vector3.up * 1.5f;
            Vector3 rayDirection = (cam != null ? cam.transform.forward : transform.forward).normalized;

            Vector3 aimPoint;
            if (Physics.Raycast(rayOrigin, rayDirection, out RaycastHit aimHit, cameraAimRange, groundMask, QueryTriggerInteraction.Ignore))
            {
                aimPoint = aimHit.point;
            }
            else
            {
                aimPoint = rayOrigin + rayDirection * cameraAimRange;
            }

            Vector3 flatOffset = new Vector3(aimPoint.x - transform.position.x, 0f, aimPoint.z - transform.position.z);
            if (flatOffset.magnitude > maxPlacementRange)
            {
                flatOffset = flatOffset.normalized * maxPlacementRange;
            }
            Vector3 candidateXZ = transform.position + flatOffset;

            float groundY = transform.position.y;
            Vector3 groundNormal = Vector3.up;
            Vector3 verticalOrigin = new Vector3(candidateXZ.x, transform.position.y + groundRaycastRange, candidateXZ.z);
            if (Physics.Raycast(verticalOrigin, Vector3.down, out RaycastHit vHit, groundRaycastRange * 2f, groundMask, QueryTriggerInteraction.Ignore))
            {
                groundY = vHit.point.y;
                groundNormal = vHit.normal;
            }

            Vector3 facing = flatOffset.sqrMagnitude > 0.0001f ? flatOffset.normalized : transform.forward;

            _anchorGroundPosition = new Vector3(candidateXZ.x, groundY, candidateXZ.z);
            _anchorFlatRotation = Quaternion.LookRotation(facing, Vector3.up);
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

        // Simple screen-center Reticle so the Player can see where Camera Aim is pointing
        // before locking a Marker there. Deliberately not a Canvas/UI Toolkit element - a
        // single OnGUI() label is enough for this Prototype and needs no scene setup.
        private void OnGUI()
        {
            if (!IsOwner) return;
            if (GameFlowManager.Instance != null && GameFlowManager.Instance.CurrentPhase != GamePhase.BUILDING) return;

            if (_reticleStyle == null)
            {
                _reticleStyle = new GUIStyle
                {
                    fontSize = 20,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white }
                };
            }

            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            GUI.Label(new Rect(cx - 10f, cy - 12f, 20f, 24f), "+", _reticleStyle);
        }

        private void EnterPlacementMode()
        {
            if (!_markerLocked)
            {
                // Bare E with no prior Right Click: lock to whatever the Camera Aim
                // was showing this frame (already computed above).
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
            // resumes following Camera Aim again.
            _markerLocked = false;
            _motor.SetMovementEnabled(true);

            if (ThirdPersonCamera.Instance != null)
            {
                ThirdPersonCamera.Instance.ExitPlacementView();
            }
        }
    }
}
