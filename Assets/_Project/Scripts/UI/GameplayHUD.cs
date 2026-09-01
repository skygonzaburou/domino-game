using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Domino.GameFlow;
using Domino.Player;

namespace Domino.UI
{
    // Prototype Gameplay HUD (Step 8B, BUILD TIME + Retry added in Step 8.5, Pause Menu +
    // Confirmation Dialog added in Step 10A). Deliberately a plain MonoBehaviour, not a
    // NetworkObject: every Client renders this purely from the already-networked
    // GameFlowManager/ChallengeTimer/GameplayPauseManager state it reads locally, so nothing
    // here needs its own network traffic. Not wired to NetworkBootstrap/MPPM/Host-start in any
    // way - this only ever reacts to GamePhase/IsPaused, so it works unmodified regardless of
    // how the Gameplay Scene was reached.
    public class GameplayHUD : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private GameObject buildingPanel;
        [SerializeField] private GameObject readyPanel;
        [SerializeField] private GameObject failedPanel;
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private GameObject resultActionContainer;

        [Header("Texts")]
        [SerializeField] private TextMeshProUGUI buildTimeText;

        [Header("Retry (Step 8.5)")]
        [SerializeField] private Button retryButton;
        [SerializeField] private Key retryKey = Key.R;

        [Header("Pause (Step 10A)")]
        [SerializeField] private GameObject pausePanel;
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button restartButton;
        [SerializeField] private Key pauseKey = Key.Escape;

        [Header("Confirmation Dialog (Step 10A)")]
        [SerializeField] private GameObject confirmationPanel;
        [SerializeField] private TextMeshProUGUI confirmationMessageText;
        [SerializeField] private Button confirmYesButton;
        [SerializeField] private Button confirmNoButton;

        // Step 10B will add StageSelect/Title here alongside Restart.
        private enum PendingConfirmAction { None, Restart }
        private PendingConfirmAction _pendingAction = PendingConfirmAction.None;

        private void Awake()
        {
            if (retryButton != null) retryButton.onClick.AddListener(RequestRetry);
            if (resumeButton != null) resumeButton.onClick.AddListener(RequestResume);
            if (restartButton != null) restartButton.onClick.AddListener(OnRestartButtonClicked);
            if (confirmYesButton != null) confirmYesButton.onClick.AddListener(OnConfirmYes);
            if (confirmNoButton != null) confirmNoButton.onClick.AddListener(OnConfirmNo);
        }

        private void Update()
        {
            GamePhase? phase = GameFlowManager.Instance != null ? GameFlowManager.Instance.CurrentPhase : (GamePhase?)null;
            bool isPaused = GameplayPauseManager.Instance != null && GameplayPauseManager.Instance.IsPaused;
            bool confirmationOpen = confirmationPanel != null && confirmationPanel.activeSelf;

            SetActive(buildingPanel, phase == GamePhase.BUILDING);
            SetActive(readyPanel, phase == GamePhase.READY);
            SetActive(failedPanel, phase == GamePhase.FAILED);
            SetActive(resultPanel, phase == GamePhase.SUCCESS);
            SetActive(resultActionContainer, phase == GamePhase.SUCCESS);

            // Section 46 priority: Confirmation > Pause > Gameplay HUD. The Pause Panel and the
            // Confirmation Dialog are mutually exclusive, never stacked - Section 15-18's "No"
            // returns to the Pause Menu, it does not layer on top of it.
            SetActive(pausePanel, isPaused && !confirmationOpen);
            if (!isPaused && confirmationOpen)
            {
                // Safety net: never strand an open Confirmation if Pause ends from underneath it
                // (e.g. Section 44's Host-disconnect-during-Pause, handled in Step 10B).
                CloseConfirmation();
            }

            if (buildTimeText != null)
            {
                var timer = ChallengeTimer.Instance;
                buildTimeText.text = "BUILD TIME\n" + (timer != null && timer.HasStarted ? FormatTime(timer.GetBuildSeconds()) : "--:--.--");
            }

            if (phase == GamePhase.SUCCESS && !isPaused && Keyboard.current != null && Keyboard.current[retryKey].wasPressedThisFrame)
            {
                RequestRetry();
            }

            HandlePauseKey(phase, isPaused, confirmationOpen);
        }

        // Escape means exactly one of three things per press, in this priority order (Section
        // 47): close an open Confirmation; Resume if already Paused; otherwise open Pause -
        // unless the Local Player is currently mid-Placement, in which case Escape belongs to
        // PlacementController's own Cancel (Section 12, frozen Placement behaviour) and this
        // method must not also act on that same press.
        private void HandlePauseKey(GamePhase? phase, bool isPaused, bool confirmationOpen)
        {
            if (Keyboard.current == null) return;
            if (!Keyboard.current[pauseKey].wasPressedThisFrame) return;

            if (confirmationOpen)
            {
                CloseConfirmation();
                return;
            }

            if (isPaused)
            {
                RequestResume();
                return;
            }

            if (IsLocalPlayerPlacing()) return;

            if (phase == GamePhase.BUILDING || phase == GamePhase.READY || phase == GamePhase.RUNNING)
            {
                GameplayPauseManager.Instance?.RequestPauseRpc();
            }
        }

        private static bool IsLocalPlayerPlacing()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || nm.LocalClient == null || nm.LocalClient.PlayerObject == null) return false;

            PlacementController placement = nm.LocalClient.PlayerObject.GetComponent<PlacementController>();
            return placement != null && placement.IsPlacing;
        }

        private static void RequestRetry()
        {
            GameFlowManager.Instance?.RetryRequestRpc();
        }

        private static void RequestResume()
        {
            GameplayPauseManager.Instance?.RequestResumeRpc();
        }

        private void OnRestartButtonClicked()
        {
            ShowConfirmation("Restart the current stage?\nAll progress will be lost.", PendingConfirmAction.Restart);
        }

        private void ShowConfirmation(string message, PendingConfirmAction action)
        {
            _pendingAction = action;
            if (confirmationMessageText != null) confirmationMessageText.text = message;
            SetActive(confirmationPanel, true);
        }

        private void CloseConfirmation()
        {
            _pendingAction = PendingConfirmAction.None;
            SetActive(confirmationPanel, false);
        }

        private void OnConfirmYes()
        {
            PendingConfirmAction action = _pendingAction;
            CloseConfirmation();

            switch (action)
            {
                case PendingConfirmAction.Restart:
                    GameFlowManager.Instance?.RequestRestartRpc();
                    break;
            }
        }

        private void OnConfirmNo()
        {
            CloseConfirmation();
        }

        private static void SetActive(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active) go.SetActive(active);
        }

        // MM:SS.xx - prototype format, matches the spec's example values exactly (00:05.27 etc).
        private static string FormatTime(double seconds)
        {
            if (seconds < 0.0) seconds = 0.0;
            int minutes = (int)(seconds / 60.0);
            double remainder = seconds - minutes * 60.0;
            return $"{minutes:00}:{remainder:00.00}";
        }
    }
}
