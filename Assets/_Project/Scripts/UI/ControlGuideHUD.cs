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
        // Step 11.1: this script's own GameObject (rootPanel's former target) must stay Always
        // Active - a MonoBehaviour that SetActive(false)'s the GameObject it lives on stops its
        // own Update() forever, with nothing left running to ever SetActive(true) it again. The
        // toggle target is now a child Content object instead (Section 2).
        [Header("Content (toggled - NOT this script's own GameObject)")]
        [SerializeField] private GameObject content;

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

        [Header("Pressed-state Keycaps - Normal Guide")]
        [SerializeField] private Image keycapW;
        [SerializeField] private Image keycapA;
        [SerializeField] private Image keycapS;
        [SerializeField] private Image keycapD;
        [SerializeField] private Image keycapShift;
        [SerializeField] private Image keycapSpace;
        [SerializeField] private Image keycapE;
        [SerializeField] private Image keycapRmb;
        [SerializeField] private Image keycapF;
        [SerializeField] private Image keycapPauseEsc;

        [Header("Pressed-state Keycaps - Placement Guide")]
        [SerializeField] private Image keycapRotateA;
        [SerializeField] private Image keycapRotateD;
        [SerializeField] private Image keycapLmb;
        [SerializeField] private Image keycapCancelEsc;

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

            SetActive(content, !hideGuide);
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

            UpdatePressedStates(showNormalGuide, showPlacementGuide, canPlaceDomino, canStartChallenge, canPause);
        }

        // Step 11.1 (Section 9-13): every Key/Mouse Button actually shown gets a Pressed State,
        // not just Hold-style ones - a One-shot Key (E/F/Esc) simply reads as briefly Pressed for
        // whatever real Frames its physical key is down, with no Flash/delay machinery added.
        private void UpdatePressedStates(bool normalGuide, bool placementGuide, bool canPlaceDomino, bool canStartChallenge, bool canPause)
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            if (normalGuide && keyboard != null)
            {
                SetPressed(keycapW, keyboard.wKey.isPressed);
                SetPressed(keycapA, keyboard.aKey.isPressed);
                SetPressed(keycapS, keyboard.sKey.isPressed);
                SetPressed(keycapD, keyboard.dKey.isPressed);
                SetPressed(keycapShift, keyboard.leftShiftKey.isPressed);
                SetPressed(keycapSpace, keyboard.spaceKey.isPressed);
                if (canPlaceDomino) SetPressed(keycapE, keyboard.eKey.isPressed);
                if (canStartChallenge) SetPressed(keycapF, keyboard.fKey.isPressed);
                if (canPause) SetPressed(keycapPauseEsc, keyboard.escapeKey.isPressed);
            }
            if (normalGuide && canPlaceDomino && mouse != null)
            {
                SetPressed(keycapRmb, mouse.rightButton.isPressed);
            }

            if (placementGuide && keyboard != null)
            {
                SetPressed(keycapRotateA, keyboard.aKey.isPressed);
                SetPressed(keycapRotateD, keyboard.dKey.isPressed);
                SetPressed(keycapCancelEsc, keyboard.escapeKey.isPressed);
            }
            if (placementGuide && mouse != null)
            {
                SetPressed(keycapLmb, mouse.leftButton.isPressed);
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
