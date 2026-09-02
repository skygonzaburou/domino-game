using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Domino.GameFlow;
using Domino.Player;

namespace Domino.UI
{
    // Step 11: purely observational Local UI - reads GameFlowManager/GameplayPauseManager/
    // PlacementController's already-existing public state and the Keyboard/Mouse devices to
    // decide what to show, but never sends input anywhere and never touches Existing Controls.
    // No NetworkVariable/RPC - Key-pressed visualization is this Client's own screen only
    // (Section 15).
    public class ControlGuideHUD : MonoBehaviour
    {
        [Header("Root")]
        [SerializeField] private GameObject rootPanel;

        [Header("External State (read-only references, same GameObjects GameplayHUD owns)")]
        [SerializeField] private GameObject confirmationPanel;

        [Header("Normal Guide Rows")]
        [SerializeField] private GameObject moveClusterRow;
        [SerializeField] private GameObject runRow;
        [SerializeField] private GameObject jumpRow;
        [SerializeField] private GameObject placeDominoRow;
        [SerializeField] private GameObject lockMarkerRow;
        [SerializeField] private GameObject lookRow;
        [SerializeField] private GameObject startChallengeRow;
        [SerializeField] private GameObject pauseRow;

        [Header("Placement Guide Rows")]
        [SerializeField] private GameObject rotateRow;
        [SerializeField] private GameObject placeRow;
        [SerializeField] private GameObject cancelRow;

        [Header("Pressed-state Keycaps (Hold inputs only - Section 12/13)")]
        [SerializeField] private Image keycapW;
        [SerializeField] private Image keycapA;
        [SerializeField] private Image keycapS;
        [SerializeField] private Image keycapD;
        [SerializeField] private Image keycapShift;
        [SerializeField] private Image keycapRotateA;
        [SerializeField] private Image keycapRotateD;

        [Header("Pressed-state Colors")]
        [SerializeField] private Color keycapNormalColor = new Color(0.18f, 0.18f, 0.18f, 0.95f);
        [SerializeField] private Color keycapPressedColor = new Color(0.2f, 0.55f, 0.9f, 1f);

        private void Update()
        {
            GamePhase? phase = GameFlowManager.Instance != null ? GameFlowManager.Instance.CurrentPhase : (GamePhase?)null;
            bool isPaused = GameplayPauseManager.Instance != null && GameplayPauseManager.Instance.IsPaused;
            bool confirmationOpen = confirmationPanel != null && confirmationPanel.activeSelf;
            bool placing = IsLocalPlayerPlacing();

            // Section 11/20: Pause / Confirmation / Result always win, before any other Context.
            bool isGameplayPhase = phase == GamePhase.BUILDING || phase == GamePhase.READY
                || phase == GamePhase.RUNNING || phase == GamePhase.FAILED;
            bool hideGuide = isPaused || confirmationOpen || !isGameplayPhase;

            SetActive(rootPanel, !hideGuide);
            if (hideGuide) return;

            bool showPlacementGuide = placing;
            bool showNormalGuide = !placing;

            // Normal Guide (Section 6/9/10): only what Current Code actually allows right now.
            bool canPlaceDomino = showNormalGuide && phase == GamePhase.BUILDING;
            bool canStartChallenge = showNormalGuide && phase == GamePhase.READY;
            bool canPause = showNormalGuide
                && (phase == GamePhase.BUILDING || phase == GamePhase.READY || phase == GamePhase.RUNNING);

            SetActive(moveClusterRow, showNormalGuide);
            SetActive(runRow, showNormalGuide);
            SetActive(jumpRow, showNormalGuide);
            SetActive(lookRow, showNormalGuide);
            SetActive(placeDominoRow, canPlaceDomino);
            SetActive(lockMarkerRow, canPlaceDomino);
            SetActive(startChallengeRow, canStartChallenge);
            SetActive(pauseRow, canPause);

            // Placement Guide (Section 7/8): Movement Hints (W/S/Jump/Run/Look) never shown here.
            SetActive(rotateRow, showPlacementGuide);
            SetActive(placeRow, showPlacementGuide);
            SetActive(cancelRow, showPlacementGuide);

            UpdatePressedStates(showNormalGuide, showPlacementGuide);
        }

        private void UpdatePressedStates(bool normalGuide, bool placementGuide)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (normalGuide)
            {
                SetPressed(keycapW, keyboard.wKey.isPressed);
                SetPressed(keycapA, keyboard.aKey.isPressed);
                SetPressed(keycapS, keyboard.sKey.isPressed);
                SetPressed(keycapD, keyboard.dKey.isPressed);
                SetPressed(keycapShift, keyboard.leftShiftKey.isPressed);
            }

            if (placementGuide)
            {
                SetPressed(keycapRotateA, keyboard.aKey.isPressed);
                SetPressed(keycapRotateD, keyboard.dKey.isPressed);
            }
        }

        private void SetPressed(Image keycap, bool pressed)
        {
            if (keycap == null) return;
            keycap.color = pressed ? keycapPressedColor : keycapNormalColor;
        }

        // Mirrors GameplayHUD.IsLocalPlayerPlacing() exactly (Section 19 - reads the same
        // existing PlacementController.IsPlacing this Guide must stay in sync with, rather than
        // tracking Placement state of its own).
        private static bool IsLocalPlayerPlacing()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || nm.LocalClient == null || nm.LocalClient.PlayerObject == null) return false;

            PlacementController placement = nm.LocalClient.PlayerObject.GetComponent<PlacementController>();
            return placement != null && placement.IsPlacing;
        }

        private static void SetActive(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active) go.SetActive(active);
        }
    }
}
