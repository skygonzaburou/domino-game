using System;
using System.Collections;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;
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

        [Header("Stage Resolution (Step 9C)")]
        [SerializeField] private StageCatalog catalog;

        // Step 10B: static, not an instance field. This Canvas-based Controller is destroyed
        // whenever the Gameplay Scene loads (MainMenu is not persistent - only the NetworkManager
        // GameObject is, via NetworkBootstrap's DontDestroyOnLoad). A live Online Session must
        // survive that round trip so a Host returning to Stage Select (Section 26: the Session
        // itself must stay connected) - or any Player returning to Title (Section 33/34: needs
        // to LeaveAsync the Session it actually joined) - has something to act on. Statics are
        // not Scene-bound, so this persists for free across the Scene reload; Start()/OnDestroy()
        // below handle re-subscribing/unsubscribing the instance-level event handlers around it.
        private static ISession _session;
        private bool _servicesInitialized;
        private Coroutine _waitForSessionStateRoutine;

        public bool IsHost => _session != null && _session.IsHost;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            // Step 10B (Section 33/34/42): returning from Gameplay specifically to leave the
            // Session (Title). Takes priority over plain rehydration below - there is nothing to
            // restore UI for, we are about to leave.
            if (GameSessionContext.PendingLeaveOnlineSession)
            {
                GameSessionContext.PendingLeaveOnlineSession = false;
                _ = LeaveSessionAsync();
                return;
            }

            // Step 10B (Section 26): returning from Gameplay to Stage Select with the Session
            // still alive (Online, Host-only action) - re-attach this fresh instance's UI/event
            // handling to the Session that already exists, instead of creating/joining a new one.
            if (_session != null)
            {
                RehydrateSession();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;

            // Defensive unsubscribe (Step 10B): without this, an old (destroyed) instance's
            // RefreshRoomUI()/HandleSessionStateChanged() would still be invoked by the
            // Session's events (event subscriptions keep the delegate target alive/reachable
            // regardless of Unity's own Destroy()), throwing once they touch any already-
            // destroyed UI reference. LeaveSessionAsync() already does this on an intentional
            // Leave; this covers every other way this instance can go away (Scene transition).
            if (_session != null)
            {
                _session.Changed -= RefreshRoomUI;
                _session.StateChanged -= HandleSessionStateChanged;
            }
            if (NetworkSessionState.Instance != null)
            {
                NetworkSessionState.Instance.PhaseChanged -= HandlePhaseChanged;
            }
        }

        // Step 10B: shared by OnSessionJoinedOrCreated() (a brand new Create/Join) and Start()'s
        // rehydration path (returning from Gameplay to Stage Select) - both need the exact same
        // event wiring and initial UI refresh, just from a different starting point.
        private void RehydrateSession()
        {
            _session.Changed += RefreshRoomUI;
            _session.StateChanged += HandleSessionStateChanged;
            RefreshRoomUI();

            if (_waitForSessionStateRoutine != null) StopCoroutine(_waitForSessionStateRoutine);
            _waitForSessionStateRoutine = StartCoroutine(WaitForNetworkSessionStateAndSubscribe());
        }

        // Must be set before Create/JoinSessionAsync triggers StartHost()/StartClient()
        // internally (Step 9C, section 40) - NetworkConfig.ConnectionApproval is read at
        // listen-start time, so this always runs from EnsureServicesInitializedInternal(),
        // which every Create/Join path awaits first. Also called directly (bypassing Unity
        // Services entirely) by StageSelectController for the Single Player Local Host path
        // (Step 9.2) - Single Player never touches Unity Gaming Services, but needs the exact
        // same CreatePlayerObject = false deferral, for the exact same reason.
        public static void SetupConnectionApproval()
        {
            if (NetworkManager.Singleton == null) return;
            NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
            NetworkManager.Singleton.ConnectionApprovalCallback = HandleConnectionApproval;
        }

        // Server-only. Two independent concerns share this single callback (Step 9.2):
        //
        // 1. Reject new connections once the game has actually started (Step 9C, section
        //    40-41): Client UI hiding the Join button is not sufficient on its own - a Client
        //    bypassing the UI (or racing a Join against Start Game) must still be refused here.
        //    Allowed through Room and Stage Select (section 39) - only StartingGame/InGame
        //    reject. For Single Player, NetworkSessionState.Phase never leaves Room (nothing
        //    ever calls HostAdvanceToStageSelect/HostTryStartGame there), so this is always a
        //    harmless no-op approval.
        //
        // 2. CreatePlayerObject = false, always (Root Cause fix, section 2-3): the Gameplay
        //    Player must never auto-spawn at connection time - GameplayPlayerSpawner explicitly
        //    creates it once the Gameplay Scene has actually finished loading, at a proper Spawn
        //    Point, for both Single Player and Online alike.
        private static void HandleConnectionApproval(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            SessionPhase phase = NetworkSessionState.Instance != null ? NetworkSessionState.Instance.Phase : SessionPhase.Room;
            bool gameStarted = phase == SessionPhase.StartingGame || phase == SessionPhase.InGame;

            response.Approved = !gameStarted;
            response.CreatePlayerObject = false;
            if (gameStarted)
            {
                response.Reason = "This game has already started.";
            }
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

            SetupConnectionApproval();

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

        // Step 12.1 (Section 19): the Room Panel's "next" Button now leads to Character Select
        // first, not Stage Select directly.
        public void OnCharacterSelectButtonClicked()
        {
            if (_session == null || !_session.IsHost) return;
            NetworkSessionState.Instance?.HostAdvanceToCharacterSelect(_session.PlayerCount);
        }

        // Step 12.1 (Section 22/36): now called from the Character Select Panel's own "STAGE
        // SELECT" Button instead of the Room Panel - body/target Phase unchanged, only the
        // caller moved (NetworkSessionState.HostAdvanceToStageSelect itself now requires
        // CharacterSelect as the source Phase, enforced there).
        public void OnStageSelectButtonClicked()
        {
            if (_session == null || !_session.IsHost) return;
            NetworkSessionState.Instance?.HostAdvanceToStageSelect(_session.PlayerCount);
        }

        // Host-only (Step 9C, section 37). Re-validates independently of whatever the UI
        // already gated - NetworkSessionState.HostTryStartGame() re-checks Player Count and
        // Phase server-side, and the resolved Stage must exist in the Catalog. Session Lock is
        // committed before the Scene Load starts (section 57), so a Join racing against Start
        // Game cannot slip in between "decided to start" and "actually locked".
        public void OnStartGameButtonClicked()
        {
            if (_session == null || !_session.IsHost) return;
            if (NetworkSessionState.Instance == null) return;

            int stageId = NetworkSessionState.Instance.SelectedStageId;
            StageDefinition stage = catalog != null ? catalog.FindById(stageId) : null;
            if (stage == null)
            {
                Debug.LogError("[OnlineSessionController] StartGame: no valid Stage selected.");
                return;
            }

            bool started = NetworkSessionState.Instance.HostTryStartGame(_session.PlayerCount);
            if (!started)
            {
                Debug.LogError("[OnlineSessionController] StartGame: server-side validation rejected the request (Player Count / Phase / Selected Stage).");
                return;
            }

            if (_session is IHostSession hostSession)
            {
                hostSession.IsLocked = true;
            }

            GameSessionContext.SelectedStageId = stage.StageId;
            NetworkManager.Singleton.SceneManager.LoadScene(stage.GameplaySceneName, LoadSceneMode.Single);
        }

        private void OnSessionJoinedOrCreated()
        {
            GameSessionContext.IsOnline = true;
            MainMenuController.Instance.ShowState(MenuState.OnlineRoom);
            RehydrateSession();
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
            if (phase == SessionPhase.CharacterSelect)
            {
                MainMenuController.Instance.ShowState(MenuState.CharacterSelect);
            }
            else if (phase == SessionPhase.StageSelect)
            {
                MainMenuController.Instance.ShowState(MenuState.StageSelect);
            }
            else if (phase == SessionPhase.Room)
            {
                // Reached either on first subscribe (harmless no-op re-show of the Panel
                // already showing) or via HostReturnToRoom() when Player Count drops below the
                // minimum while still in Stage Select (section 38).
                MainMenuController.Instance.ShowState(MenuState.OnlineRoom);
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

            GameSessionContext.IsOnline = false;
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

            // Section 38: dropping below the minimum while already in Character Select or Stage
            // Select must not leave those Phases reachable/continuable - returning to Room is
            // the simplest way to enforce that without a separate "paused" state (Step 12.1
            // extends this same guard to the new Character Select Phase).
            if (_session.IsHost && !canStart && NetworkSessionState.Instance != null
                && (NetworkSessionState.Instance.Phase == SessionPhase.StageSelect
                    || NetworkSessionState.Instance.Phase == SessionPhase.CharacterSelect))
            {
                NetworkSessionState.Instance.HostReturnToRoom();
            }
        }
    }
}
