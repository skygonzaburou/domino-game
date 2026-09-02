using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Domino.GameFlow;
using Domino.Menu;
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
        [SerializeField] private Key restartKey = Key.R; // Step 10.1

        [Header("Pause Navigation (Step 10B)")]
        [SerializeField] private Button pauseStageSelectButton;
        [SerializeField] private TextMeshProUGUI pauseStageSelectButtonText;
        [SerializeField] private Button pauseTitleButton;
        [SerializeField] private Key stageSelectKey = Key.S; // Step 10.1
        [SerializeField] private Key titleKey = Key.T; // Step 10.1

        [Header("Pause Navigation - Character Select (Step 12.1)")]
        [SerializeField] private Button pauseCharacterSelectButton;
        [SerializeField] private TextMeshProUGUI pauseCharacterSelectButtonText;
        [SerializeField] private Key characterSelectKey = Key.C;

        [Header("Confirmation Dialog (Step 10A)")]
        [SerializeField] private GameObject confirmationPanel;
        [SerializeField] private TextMeshProUGUI confirmationMessageText;
        [SerializeField] private Button confirmYesButton;
        [SerializeField] private Button confirmNoButton;
        [SerializeField] private Key confirmYesKey = Key.Enter; // Step 10.1 (No reuses pauseKey/Escape)

        [Header("Result Navigation (Step 10B)")]
        [SerializeField] private Button resultStageSelectButton;
        [SerializeField] private TextMeshProUGUI resultStageSelectButtonText;
        [SerializeField] private Button resultTitleButton;

        private enum PendingConfirmAction { None, Restart, CharacterSelect, StageSelect, Title }
        private PendingConfirmAction _pendingAction = PendingConfirmAction.None;

        private void Awake()
        {
            if (retryButton != null) retryButton.onClick.AddListener(RequestRetry);
            if (resumeButton != null) resumeButton.onClick.AddListener(RequestResume);
            if (restartButton != null) restartButton.onClick.AddListener(OnRestartButtonClicked);
            if (pauseCharacterSelectButton != null) pauseCharacterSelectButton.onClick.AddListener(OnPauseCharacterSelectButtonClicked);
            if (pauseStageSelectButton != null) pauseStageSelectButton.onClick.AddListener(OnPauseStageSelectButtonClicked);
            if (pauseTitleButton != null) pauseTitleButton.onClick.AddListener(OnPauseTitleButtonClicked);
            if (confirmYesButton != null) confirmYesButton.onClick.AddListener(OnConfirmYes);
            if (confirmNoButton != null) confirmNoButton.onClick.AddListener(OnConfirmNo);
            if (resultStageSelectButton != null) resultStageSelectButton.onClick.AddListener(OnResultStageSelectButtonClicked);
            if (resultTitleButton != null) resultTitleButton.onClick.AddListener(OnResultTitleButtonClicked);
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

            HandleGameplayMenuInput(phase, isPaused, confirmationOpen);
            UpdateStageSelectGating();
            UpdateCharacterSelectGating();
        }

        // Section 28/40: Online Stage Select (from either the Pause Menu or the SUCCESS Result)
        // is Host-only - a Client sees a Disabled Button labeled "Host Only" rather than a
        // silently-ignored Click. Single Player always allows it (there is no one else to defer
        // to). NetworkManager.Singleton.IsHost is the same Authority check used everywhere else
        // in this project - "Host" and "Server" are one and the same in this Host+Client model.
        private void UpdateStageSelectGating()
        {
            bool canStageSelect = !GameSessionContext.IsOnline || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost);
            // Step 10.1: the (S) Shortcut hint only makes sense while the action is actually
            // usable - a Host-only-disabled Button has no working Shortcut to advertise.
            string label = canStageSelect ? "Stage Select (S)" : "Stage Select (Host Only)";

            if (pauseStageSelectButton != null) pauseStageSelectButton.interactable = canStageSelect;
            if (pauseStageSelectButtonText != null) pauseStageSelectButtonText.text = label;
            if (resultStageSelectButton != null) resultStageSelectButton.interactable = canStageSelect;
            if (resultStageSelectButtonText != null) resultStageSelectButtonText.text = label;
        }

        // Step 12.1 (Section 31/32): Pause Menu's "Character Select" is Host-only Online, exactly
        // like Stage Select above (Section 38 explicitly keeps Result unchanged - no equivalent
        // Button there).
        private void UpdateCharacterSelectGating()
        {
            bool canCharacterSelect = !GameSessionContext.IsOnline || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost);
            string label = canCharacterSelect ? "Character Select (C)" : "Character Select (Host Only)";

            if (pauseCharacterSelectButton != null) pauseCharacterSelectButton.interactable = canCharacterSelect;
            if (pauseCharacterSelectButtonText != null) pauseCharacterSelectButtonText.text = label;
        }

        // Step 10.1: single Context-sensitive dispatcher for every Gameplay Menu Keyboard
        // Shortcut (Pause Menu / Confirmation Dialog / SUCCESS Result). Each branch is mutually
        // exclusive and returns immediately after handling a key, so exactly one context is ever
        // "listening" for a given press - R/S/T do nothing during ordinary Gameplay (Section 4),
        // and an open Confirmation takes priority over everything else (Section 7: R/S/T must
        // never fire the Pause Menu/Result action underneath it). Every branch calls the exact
        // same method its on-screen Button already calls (Section 5) - no separate Keyboard-only
        // Navigation Logic exists.
        private void HandleGameplayMenuInput(GamePhase? phase, bool isPaused, bool confirmationOpen)
        {
            if (Keyboard.current == null) return;

            if (confirmationOpen)
            {
                if (Keyboard.current[confirmYesKey].wasPressedThisFrame) OnConfirmYes();
                else if (Keyboard.current[pauseKey].wasPressedThisFrame) OnConfirmNo();
                return;
            }

            if (isPaused)
            {
                if (Keyboard.current[pauseKey].wasPressedThisFrame) RequestResume();
                else if (Keyboard.current[restartKey].wasPressedThisFrame) OnRestartButtonClicked();
                else if (Keyboard.current[characterSelectKey].wasPressedThisFrame) OnPauseCharacterSelectButtonClicked();
                else if (Keyboard.current[stageSelectKey].wasPressedThisFrame) OnPauseStageSelectButtonClicked();
                else if (Keyboard.current[titleKey].wasPressedThisFrame) OnPauseTitleButtonClicked();
                return;
            }

            if (phase == GamePhase.SUCCESS)
            {
                if (Keyboard.current[retryKey].wasPressedThisFrame) RequestRetry();
                else if (Keyboard.current[stageSelectKey].wasPressedThisFrame) OnResultStageSelectButtonClicked();
                else if (Keyboard.current[titleKey].wasPressedThisFrame) OnResultTitleButtonClicked();
                return;
            }

            // Ordinary Gameplay: only Escape has any meaning here (open Pause) - Section 47's
            // Placement-Cancel priority is unchanged from Step 10A.
            if (!Keyboard.current[pauseKey].wasPressedThisFrame) return;
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

        // Section 16: Pause Menu's Stage Select always confirms first, regardless of Single
        // Player / Online - unlike Title (below), the wording does not change either.
        private void OnPauseStageSelectButtonClicked()
        {
            ShowConfirmation("Return to Stage Select?\nCurrent progress will be lost.", PendingConfirmAction.StageSelect);
        }

        // Step 12.1 (Section 28): always confirms first too, but (like Title) the wording
        // differs - Online explicitly says "everyone" since this is a Host-only action that
        // pulls every connected Player back to Character Select together.
        private void OnPauseCharacterSelectButtonClicked()
        {
            string message = GameSessionContext.IsOnline
                ? "Return everyone to Character Select?\nCurrent progress will be lost."
                : "Return to Character Select?\nCurrent progress will be lost.";
            ShowConfirmation(message, PendingConfirmAction.CharacterSelect);
        }

        // Section 17: Pause Menu's Title always confirms first too, but the wording differs -
        // Online explicitly warns about disconnecting from the Session, Single Player warns
        // about losing progress.
        private void OnPauseTitleButtonClicked()
        {
            string message = GameSessionContext.IsOnline
                ? "Leave the online game and return to Title?\nYou will be disconnected from the session."
                : "Return to Title?\nCurrent progress will be lost.";
            ShowConfirmation(message, PendingConfirmAction.Title);
        }

        // Section 40: SUCCESS Result's Stage Select needs no confirmation (the Run is already
        // over) - still Host-only-gated when Online (UpdateStageSelectGating above), so this is
        // a defensive re-check matching the disabled Button, not the only line of defense.
        private void OnResultStageSelectButtonClicked()
        {
            bool canStageSelect = !GameSessionContext.IsOnline || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost);
            if (!canStageSelect) return;
            GameplayNavigationController.Instance?.RequestReturnToStageSelect();
        }

        // Section 41/42: SUCCESS Result's Title needs no confirmation for Single Player (the Run
        // is already over, nothing left to lose), but Online always confirms - leaving here
        // disconnects from the Session, which Single Player has no equivalent of.
        private void OnResultTitleButtonClicked()
        {
            if (GameSessionContext.IsOnline)
            {
                ShowConfirmation("Leave the online game?\nYou will be disconnected from the session.", PendingConfirmAction.Title);
            }
            else
            {
                GameplayNavigationController.Instance?.RequestReturnToTitle();
            }
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
                case PendingConfirmAction.CharacterSelect:
                    GameplayNavigationController.Instance?.RequestReturnToCharacterSelect();
                    break;
                case PendingConfirmAction.StageSelect:
                    GameplayNavigationController.Instance?.RequestReturnToStageSelect();
                    break;
                case PendingConfirmAction.Title:
                    GameplayNavigationController.Instance?.RequestReturnToTitle();
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
