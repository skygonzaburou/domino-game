using System;
using System.Collections;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UI;

namespace Domino.Menu
{
    // Owns Online Session Networking (Step 9, section 51 responsibility split): Unity Gaming
    // Services init/auth, Create/Join/Leave Room, Player Count UI, and reacting to
    // NetworkSessionState.Phase changes to move every connected Player's local Menu together.
    // MainMenuController still owns Panel visibility - this class only calls ShowState().
    //
    // Architecture note (Step 9, section 19-20): uses the current com.unity.services.multiplayer
    // (2.3.1) unified Session API rather than separately wiring Lobby+Relay - that package
    // internalizes both (confirmed via package dependency inspection: no com.unity.services.relay
    // or com.unity.services.lobby package exists; com.unity.services.multiplayer pulls in only
    // core/authentication/qos/wire/deployment). SessionOptions.WithRelayNetwork() is sufficient
    // for the "Join Code between people who already know it" requirement (section 20/57) - no
    // Public Room List / Matchmaker functionality is used.
    public class OnlineSessionController : MonoBehaviour
    {
        public static OnlineSessionController Instance { get; private set; }

        private const int MaxPlayers = 4;
        private const int MinPlayersToStart = 2;

        [Header("Online Create Panel")]
        [SerializeField] private TextMeshProUGUI createStatusText;

        [Header("Online Join Panel")]
        [SerializeField] private TMP_InputField joinCodeInputField;
        [SerializeField] private TextMeshProUGUI joinErrorText;

        [Header("Online Room Panel")]
        [SerializeField] private TextMeshProUGUI joinCodeText;
        [SerializeField] private TextMeshProUGUI playerCountText;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private Button stageSelectButton;

        private ISession _session;
        private bool _servicesInitialized;
        private Coroutine _waitForSessionStateRoutine;

        private void Awake()
        {
            Instance = this;
        }

        private System.Threading.Tasks.Task EnsureServicesInitializedAsync()
        {
            return EnsureServicesInitializedInternal();
        }

        private async System.Threading.Tasks.Task EnsureServicesInitializedInternal()
        {
            if (_servicesInitialized) return;

            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            _servicesInitialized = true;
        }

        public async void OnCreateRoomButtonClicked()
        {
            MainMenuController.Instance.ShowState(MenuState.OnlineCreate);
            if (createStatusText != null) createStatusText.text = "Creating room...";

            try
            {
                await EnsureServicesInitializedAsync();

                SessionOptions options = new SessionOptions { MaxPlayers = MaxPlayers }.WithRelayNetwork();
                IHostSession hostSession = await MultiplayerService.Instance.CreateSessionAsync(options);
                _session = hostSession;

                OnSessionJoinedOrCreated();
            }
            catch (Exception e)
            {
                Debug.LogError($"[OnlineSessionController] CreateRoom failed: {e}");
                if (createStatusText != null) createStatusText.text = "Could not create the room.";
                MainMenuController.Instance.ShowState(MenuState.OnlineSelect);
            }
        }

        public async void OnJoinButtonClicked()
        {
            string code = joinCodeInputField != null ? joinCodeInputField.text.Trim() : string.Empty;
            if (string.IsNullOrEmpty(code)) return;

            if (joinErrorText != null) joinErrorText.text = "Joining...";

            try
            {
                await EnsureServicesInitializedAsync();

                ISession session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code, null);
                _session = session;

                OnSessionJoinedOrCreated();
            }
            catch (Exception e)
            {
                // Console gets the real exception (section 26: detail is fine in Console, not
                // in the UI). The Player only ever sees a short, generic message - this covers
                // Invalid Code / Expired Session / Room Full / Host disconnected / Network
                // failure alike, since the Session API does not reliably distinguish them.
                Debug.LogError($"[OnlineSessionController] JoinRoom failed: {e}");
                if (joinErrorText != null) joinErrorText.text = "Could not join the room.\nPlease check the Join Code.";
            }
        }

        public async void OnLeaveButtonClicked()
        {
            await LeaveSessionAsync();
            MainMenuController.Instance.ShowState(MenuState.OnlineSelect);
        }

        public void OnStageSelectButtonClicked()
        {
            if (_session == null || !_session.IsHost) return;
            NetworkSessionState.Instance?.HostAdvanceToStageSelect(_session.PlayerCount);
        }

        private void OnSessionJoinedOrCreated()
        {
            _session.Changed += RefreshRoomUI;
            _session.StateChanged += HandleSessionStateChanged;

            MainMenuController.Instance.ShowState(MenuState.OnlineRoom);
            RefreshRoomUI();

            if (_waitForSessionStateRoutine != null) StopCoroutine(_waitForSessionStateRoutine);
            _waitForSessionStateRoutine = StartCoroutine(WaitForNetworkSessionStateAndSubscribe());
        }

        // The Host's in-Scene NetworkSessionState spawns synchronously as part of StartHost()
        // (Step 6's established Scene-placed NetworkObject pattern), but a joining Client only
        // receives it after NGO's Scene/Object synchronization completes a moment later - so
        // Instance may briefly be null right after JoinSessionByCodeAsync returns.
        private IEnumerator WaitForNetworkSessionStateAndSubscribe()
        {
            float timeout = 5f;
            float elapsed = 0f;
            while (NetworkSessionState.Instance == null && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (NetworkSessionState.Instance == null)
            {
                Debug.LogError("[OnlineSessionController] NetworkSessionState never appeared after joining the Session.");
                yield break;
            }

            NetworkSessionState.Instance.PhaseChanged += HandlePhaseChanged;
            HandlePhaseChanged(NetworkSessionState.Instance.Phase);
        }

        private void HandlePhaseChanged(SessionPhase phase)
        {
            if (phase == SessionPhase.StageSelect)
            {
                MainMenuController.Instance.ShowState(MenuState.StageSelect);
            }
        }

        private void HandleSessionStateChanged(SessionState state)
        {
            // Host left / Session removed while still in the Room or Stage Select (section 30):
            // Clients cannot continue, so return everyone to Online Select.
            if (state == SessionState.Deleted || state == SessionState.Disconnected)
            {
                _ = HandleSessionEndedUnexpectedly();
            }
        }

        private async System.Threading.Tasks.Task HandleSessionEndedUnexpectedly()
        {
            await LeaveSessionAsync();
            if (statusText != null) statusText.text = "Host disconnected.";
            MainMenuController.Instance.ShowState(MenuState.OnlineSelect);
        }

        private async System.Threading.Tasks.Task LeaveSessionAsync()
        {
            if (_waitForSessionStateRoutine != null)
            {
                StopCoroutine(_waitForSessionStateRoutine);
                _waitForSessionStateRoutine = null;
            }

            if (NetworkSessionState.Instance != null)
            {
                NetworkSessionState.Instance.PhaseChanged -= HandlePhaseChanged;
            }

            if (_session != null)
            {
                _session.Changed -= RefreshRoomUI;
                _session.StateChanged -= HandleSessionStateChanged;

                try
                {
                    // Observed in testing: ISession.LeaveAsync() can hang indefinitely (no
                    // exception, no completion) when the local Player is the Session's only
                    // member. A stuck await here would leave the Player unable to back out of
                    // the Room at all, so this is guarded with a timeout - Menu state must
                    // always be able to recover, even if the underlying Service call does not.
                    Task leaveTask = _session.LeaveAsync();
                    Task completed = await Task.WhenAny(leaveTask, Task.Delay(TimeSpan.FromSeconds(5)));
                    if (completed != leaveTask)
                    {
                        Debug.LogWarning("[OnlineSessionController] LeaveAsync() timed out after 5s - proceeding with local cleanup anyway.");
                    }
                    else if (leaveTask.IsFaulted)
                    {
                        Debug.LogError($"[OnlineSessionController] LeaveAsync failed: {leaveTask.Exception}");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[OnlineSessionController] LeaveAsync failed: {e}");
                }

                _session = null;
            }

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }
        }

        private void RefreshRoomUI()
        {
            if (_session == null) return;

            if (joinCodeText != null) joinCodeText.text = _session.Code;
            if (playerCountText != null) playerCountText.text = $"{_session.PlayerCount} / {_session.MaxPlayers}";

            bool canStart = _session.IsHost && _session.PlayerCount >= MinPlayersToStart;
            if (stageSelectButton != null) stageSelectButton.interactable = canStart;

            if (statusText != null)
            {
                statusText.text = _session.IsHost
                    ? (canStart ? string.Empty : "Waiting for players...")
                    : "Waiting for the host...";
            }
        }
    }
}
