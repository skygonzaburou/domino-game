using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Domino.CameraSystem;
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
        [SerializeField] private GameObject slowWalkRow;
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
        [SerializeField] private Image keycapCtrl;
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

        // Step 14.3 (Section 9): the F key now does double duty (Start Challenge while READY,
        // Cancel Run while RUNNING) - the same Row/Text is reused for both rather than adding a
        // second Row, so no new Scene-placed GameObject is needed here (Stage 2/3's own copies
        // of this Canvas are frozen this Step and must not be touched). The child Text is found
        // at Runtime via GetComponentInChildren instead of a new serialized field for the same
        // reason - nothing about the Scene's own serialized wiring needs to change.
        private const string StartChallengeLabelText = "Start Challenge";
        private const string CancelRunLabelText = "Cancel Run";
        private TextMeshProUGUI _startChallengeLabel;
        private bool _startChallengeLabelInitialized;

        // Step 14.4.1: same reasoning as the Start/Cancel Label reuse above - SampleScene/
        // StageScene_2/StageScene_3 each carry their own independent copy of this Canvas, so a
        // new Scene-authored Row/Field here would need identical Wiring repeated in all three
        // (Stage 2/3 are Read/Inspect/Test-only this Step). Instead, two new Rows are built
        // once at Runtime by cloning the existing Run Row (its "Shift" Keycap is already sized
        // for a multi-letter Label, unlike the single-letter E/F Keycaps) and only swapping the
        // two Text contents - identical visual style, zero new Scene content, works the same in
        // any Scene this script runs in without touching that Scene's own file at all.
        private const string NextCameraKeyText = "LMB";
        private const string NextCameraLabelText = "Next Camera";
        private const string PreviousCameraKeyText = "RMB";
        private const string PreviousCameraLabelText = "Previous Camera";
        private GameObject _nextCameraRow;
        private GameObject _previousCameraRow;
        private Image _keycapNextCamera;
        private Image _keycapPreviousCamera;
        private bool _cameraGuideRowsBuilt;

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
            // Step 14.3: F now also cancels a RUNNING Challenge back to BUILDING - shares the
            // same Row/keycap as Start Challenge (they can never both be true, READY and
            // RUNNING being mutually exclusive Phases) rather than a second Row.
            bool canCancelChallenge = showNormalGuide && phase == GamePhase.RUNNING;
            bool canUseFKey = canStartChallenge || canCancelChallenge;
            bool canPause = showNormalGuide
                && (phase == GamePhase.BUILDING || phase == GamePhase.READY || phase == GamePhase.RUNNING);
            // Step 14.4.1 (Section 22/23): RUNNING-only (Placement's own LMB/RMB meanings are
            // BUILDING-only, so the two Phases never overlap) and only when there is more than
            // one Overview Camera Point to actually cycle between - a single-Point Stage (every
            // Stage as of Step 14.4) never shows this row, matching the recommended UX.
            bool canCycleCamera = canCancelChallenge && OverviewCameraPoint.Points.Count > 1;

            SetActive(moveClusterRow, showNormalGuide);
            SetActive(runRow, showNormalGuide);
            SetActive(slowWalkRow, showNormalGuide);
            SetActive(jumpRow, showNormalGuide);
            SetActive(lookRow, showNormalGuide);
            SetActive(placeDominoRow, canPlaceDomino);
            SetActive(lockMarkerRow, canPlaceDomino);
            SetActive(startChallengeRow, canUseFKey);
            SetActive(pauseRow, canPause);

            if (canUseFKey)
            {
                EnsureStartChallengeLabel();
                if (_startChallengeLabel != null)
                {
                    _startChallengeLabel.text = canCancelChallenge ? CancelRunLabelText : StartChallengeLabelText;
                }
            }

            if (canCycleCamera) EnsureCameraGuideRows();
            SetActive(_nextCameraRow, canCycleCamera);
            SetActive(_previousCameraRow, canCycleCamera);

            // Placement Guide (Section 7/8): Movement Hints (W/S/Jump/Run/Look) never shown here.
            SetActive(rotateRow, showPlacementGuide);
            SetActive(placeRow, showPlacementGuide);
            SetActive(cancelRow, showPlacementGuide);

            UpdatePressedStates(showNormalGuide, showPlacementGuide, canPlaceDomino, canUseFKey, canPause, canCycleCamera);
        }

        // Step 11.1 (Section 9-13): every Key/Mouse Button actually shown gets a Pressed State,
        // not just Hold-style ones - a One-shot Key (E/F/Esc) simply reads as briefly Pressed for
        // whatever real Frames its physical key is down, with no Flash/delay machinery added.
        private void UpdatePressedStates(bool normalGuide, bool placementGuide, bool canPlaceDomino, bool canUseFKey, bool canPause, bool canCycleCamera)
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
                SetPressed(keycapCtrl, keyboard.leftCtrlKey.isPressed);
                SetPressed(keycapSpace, keyboard.spaceKey.isPressed);
                if (canPlaceDomino) SetPressed(keycapE, keyboard.eKey.isPressed);
                if (canUseFKey) SetPressed(keycapF, keyboard.fKey.isPressed);
                if (canPause) SetPressed(keycapPauseEsc, keyboard.escapeKey.isPressed);
            }
            if (normalGuide && canPlaceDomino && mouse != null)
            {
                SetPressed(keycapRmb, mouse.rightButton.isPressed);
            }
            if (normalGuide && canCycleCamera && mouse != null)
            {
                SetPressed(_keycapNextCamera, mouse.leftButton.isPressed);
                SetPressed(_keycapPreviousCamera, mouse.rightButton.isPressed);
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

        private void EnsureStartChallengeLabel()
        {
            if (_startChallengeLabelInitialized) return;
            _startChallengeLabelInitialized = true;
            if (startChallengeRow != null) _startChallengeLabel = startChallengeRow.GetComponentInChildren<TextMeshProUGUI>(true);
        }

        // Step 14.4.1: builds the two new Rows once, the first time they are actually needed
        // (a multi-Point Stage reaches RUNNING) - never eagerly, so a single-Point Stage (every
        // Stage as of Step 14.4) never even instantiates them. Clones runRow (see field comment
        // above for why that specific Row) twice, inserts both immediately before
        // startChallengeRow in the same parent Layout Group, and swaps each clone's two Text
        // contents (Keycap Letter, Action Label) - everything else (Image color, sizing, font)
        // comes along automatically via Instantiate().
        private void EnsureCameraGuideRows()
        {
            if (_cameraGuideRowsBuilt) return;
            _cameraGuideRowsBuilt = true;
            if (runRow == null || startChallengeRow == null) return;

            int insertIndex = startChallengeRow.transform.GetSiblingIndex();
            _nextCameraRow = CreateCameraGuideRow("NextCameraRow", insertIndex, NextCameraKeyText, NextCameraLabelText, out _keycapNextCamera);
            _previousCameraRow = CreateCameraGuideRow("PreviousCameraRow", insertIndex + 1, PreviousCameraKeyText, PreviousCameraLabelText, out _keycapPreviousCamera);
        }

        private GameObject CreateCameraGuideRow(string rowName, int siblingIndex, string keyText, string labelText, out Image keycapImage)
        {
            GameObject row = Instantiate(runRow, runRow.transform.parent);
            row.name = rowName;
            row.transform.SetSiblingIndex(siblingIndex);

            TextMeshProUGUI[] texts = row.GetComponentsInChildren<TextMeshProUGUI>(true);
            if (texts.Length >= 2)
            {
                texts[0].text = keyText;   // the Keycap's own inner Letter (was "Shift")
                texts[1].text = labelText; // the Row's main Action Label (was "Run")
            }

            keycapImage = row.GetComponentInChildren<Image>(true);
            row.SetActive(false); // Update() decides real visibility on the very next call
            return row;
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
