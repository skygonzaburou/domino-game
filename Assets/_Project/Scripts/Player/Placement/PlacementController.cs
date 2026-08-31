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
        [SerializeField] private GameObject dominoPrefab;
        [SerializeField] private GameObject ghostDominoPrefab;

        [Header("Placement Position")]
        [SerializeField] private float forwardDistance = 1.2f;
        [SerializeField] private float hoverHeight = 0.15f;
        [SerializeField] private float groundRaycastRange = 3f;
        [SerializeField] private LayerMask groundMask = ~0;

        private PlayerMotor _motor;
        private GameObject _ghostInstance;
        private GhostDomino _ghostDomino;
        private bool _isPlacing;
        private Transform _placedDominoParent;

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

            _placedDominoParent = FindOrCreatePlacedDominoParent();
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
            Vector3 position = _ghostInstance.transform.position;
            Quaternion rotation = _ghostDomino.CurrentRotation;

            Instantiate(dominoPrefab, position, rotation, _placedDominoParent);

            ExitPlacementMode();
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

        private static Transform FindOrCreatePlacedDominoParent()
        {
            GameObject existing = GameObject.Find("PlacedDominoes");
            if (existing == null)
            {
                existing = new GameObject("PlacedDominoes");
            }
            return existing.transform;
        }
    }
}
