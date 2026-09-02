using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Domino.GameFlow;
using Domino.Networking;

namespace Domino.Menu
{
    // Step 10B. Lives in the Gameplay Scene (SampleScene) and owns the two ways to leave
    // Gameplay for MainMenu: "Stage Select" (Online Host-only, Session stays connected, NGO
    // Network Scene Management keeps every Client in sync) and "Return to Title" (Single Player
    // or Online, any Player, ends this Client's own participation). GameplayHUD calls into this
    // after its own Confirmation Dialog is accepted - this class does not know about UI at all.
    public class GameplayNavigationController : MonoBehaviour
    {
        public static GameplayNavigationController Instance { get; private set; }

        [Tooltip("MainMenu Sceneの名前")]
        [SerializeField] private string mainMenuSceneName = "MainMenu";

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            if (NetworkManager.Singleton != null) NetworkManager.Singleton.OnClientStopped += HandleClientStopped;
        }

        private void OnDisable()
        {
            if (NetworkManager.Singleton != null) NetworkManager.Singleton.OnClientStopped -= HandleClientStopped;
        }

        // Section 44/56: fires for this Client's own connection ending, for ANY reason - our own
        // intentional Shutdown() below (Title), or an unexpected one (the Host itself leaving for
        // Title severs every other Client's connection automatically via NGO - no separate RPC
        // needed to "tell" them). Either way, the correct outcome is identical: safely land back
        // on MainMenu with Pause guaranteed cleared, so this is the single place that performs
        // the actual local Scene Load for every "we are no longer networked" case. The Stage
        // Select Return path (below) deliberately does NOT go through Shutdown()/this handler -
        // it stays networked and uses NGO Scene Management directly instead.
        private void HandleClientStopped(bool wasHost)
        {
            GameplayPauseManager.ForceLocalUnpauseImmediate();

            // An unexpected disconnect (not a Title button we clicked ourselves) never set
            // PendingLeaveOnlineSession - IsOnline is still cleared here regardless so the fresh
            // MainMenu does not think it is sitting on a still-valid Online Session.
            GameSessionContext.IsOnline = false;

            SceneManager.LoadScene(mainMenuSceneName, LoadSceneMode.Single);
        }

        // Section 25/26/27/28: Host-only when Online (the caller/UI is expected to have already
        // gated the button on this - GameFlowManager-style "never trust a single gate" defensive
        // re-check happens here too via NetworkManager.IsServer). Single Player behaves like the
        // existing Retry/Restart Local Host pattern: fully shut the session down rather than
        // keeping it "alive" for nobody.
        public void RequestReturnToStageSelect()
        {
            GameplayPauseManager.ForceLocalUnpauseImmediate();

            if (GameSessionContext.IsOnline)
            {
                if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

                // NetworkSessionState does not exist here - it is a normal Scene-placed
                // NetworkObject bound to MainMenu.unity (unlike NetworkManager, not
                // DontDestroyOnLoad), so it was already destroyed when we left MainMenu for
                // Gameplay. The fresh instance that spawns once MainMenu reloads applies this
                // intent itself (NetworkSessionState.OnNetworkSpawn).
                GameplayPlayerSpawner.Instance?.DespawnAllGameplayPlayers();
                GameSessionContext.ReturnDestination = MenuState.StageSelect;
                GameSessionContext.PendingHostReturnToStageSelect = true;
                NetworkManager.Singleton.SceneManager.LoadScene(mainMenuSceneName, LoadSceneMode.Single);
            }
            else
            {
                GameSessionContext.ReturnDestination = MenuState.StageSelect;
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                {
                    // HandleClientStopped performs the actual Scene Load - calling it here too
                    // would race a second LoadScene against that same-frame callback.
                    NetworkManager.Singleton.Shutdown();
                }
                else
                {
                    SceneManager.LoadScene(mainMenuSceneName, LoadSceneMode.Single);
                }
            }
        }

        // Step 12.1 (Section 30/31): identical shape to RequestReturnToStageSelect above, just
        // targeting Character Select instead - Online is Host-only (re-checked here, same as
        // Stage Select), Single Player fully shuts the session down and lets MainMenu's Start()
        // land on the stored ReturnDestination once it reloads.
        public void RequestReturnToCharacterSelect()
        {
            GameplayPauseManager.ForceLocalUnpauseImmediate();

            if (GameSessionContext.IsOnline)
            {
                if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

                GameplayPlayerSpawner.Instance?.DespawnAllGameplayPlayers();
                GameSessionContext.ReturnDestination = MenuState.CharacterSelect;
                GameSessionContext.PendingHostReturnToCharacterSelect = true;
                NetworkManager.Singleton.SceneManager.LoadScene(mainMenuSceneName, LoadSceneMode.Single);
            }
            else
            {
                GameSessionContext.ReturnDestination = MenuState.CharacterSelect;
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                {
                    NetworkManager.Singleton.Shutdown();
                }
                else
                {
                    SceneManager.LoadScene(mainMenuSceneName, LoadSceneMode.Single);
                }
            }
        }

        // Section 32/33/34: identical call for Single Player, Online Client, and Online Host -
        // NetworkManager.Shutdown() already behaves correctly differently for each (a Client
        // shutting down only disconnects itself; the Host shutting down ends the whole session,
        // which is exactly the asymmetry Section 33 vs 34 calls for). The actual Scene Load is
        // left to HandleClientStopped above so Host and Client alike take the same path.
        public void RequestReturnToTitle()
        {
            GameSessionContext.ReturnDestination = null;
            GameSessionContext.PendingLeaveOnlineSession = GameSessionContext.IsOnline;
            GameplayPauseManager.ForceLocalUnpauseImmediate();

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }
            else
            {
                SceneManager.LoadScene(mainMenuSceneName, LoadSceneMode.Single);
            }
        }
    }
}
