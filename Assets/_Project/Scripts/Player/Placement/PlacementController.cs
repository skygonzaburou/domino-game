using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Domino.CameraSystem;
using Domino.DominoSystem;

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

        [Header("Placement Position")]
        [SerializeField] private float forwardDistance = 1.2f;
        [SerializeField] private float hoverHeight = 0.15f;
        [SerializeField] private float groundRaycastRange = 3f;
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private float dominoRestingHeight = 0.5f;

        private PlayerMotor _motor;
        private GameObject _ghostInstance;
        private GhostDomino _ghostDomino;
        private bool _isPlacing;

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

            if (!_isPlacing)
            {
                if (Keyboard.current != null && Keyboard.current[enterPlacementKey].wasPressedThisFrame)
                {
                    EnterPlacementMode();
                }
                return;
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                ConfirmPlacement();
                return;
            }

            bool cancelledByMouse = mouse != null && mouse.rightButton.wasPressedThisFrame;
            bool cancelledByKey = Keyboard.current != null && Keyboard.current[cancelKey].wasPressedThisFrame;
            if (cancelledByMouse || cancelledByKey)
            {
                CancelPlacement();
            }
        }

        private void EnterPlacementMode()
        {
            _isPlacing = true;
            _motor.SetMovementEnabled(false);

            Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 0.0001f) flatForward = Vector3.forward;

            Vector3 basePos = transform.position + flatForward * forwardDistance;
            float groundY = transform.position.y;

            Vector3 rayOrigin = basePos + Vector3.up * groundRaycastRange * 0.5f;
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, groundRaycastRange, groundMask, QueryTriggerInteraction.Ignore))
            {
                groundY = hit.point.y;
            }

            Vector3 spawnPosition = new Vector3(basePos.x, groundY + hoverHeight, basePos.z);
            Quaternion spawnRotation = Quaternion.LookRotation(flatForward, Vector3.up);

            EnsureGhostInstance();
            _ghostInstance.transform.SetPositionAndRotation(spawnPosition, spawnRotation);
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
            // The Ghost's Y is its metronome pivot height (hoverHeight), not a physically
            // correct resting height for a real Domino. Spawning the real Domino at that
            // pivot embeds it deep in the ground, which can topple even a vertical Domino
            // via chaotic depenetration. Re-derive ground Y and use the Domino's own resting
            // height instead - this only adjusts Y; X/Z and rotation are still used exactly
            // as-is, so the "no angle correction" placement rule is untouched.
            Vector3 ghostPosition = _ghostInstance.transform.position;
            float groundY = ghostPosition.y - hoverHeight;
            Vector3 position = new Vector3(ghostPosition.x, groundY + dominoRestingHeight, ghostPosition.z);
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
            _motor.SetMovementEnabled(true);

            if (ThirdPersonCamera.Instance != null)
            {
                ThirdPersonCamera.Instance.ExitPlacementView();
            }
        }
    }
}
