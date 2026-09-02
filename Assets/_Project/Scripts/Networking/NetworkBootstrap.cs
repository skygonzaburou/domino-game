using Unity.Multiplayer.PlayMode;
using Unity.Netcode;
using UnityEngine;

namespace Domino.Networking
{
    // Lives on the persistent "NetworkManager" GameObject (Step 9: moved from the Gameplay
    // Scene into MainMenu.unity, DontDestroyOnLoad'd so it survives the Menu -> Gameplay
    // transition for both Single Player and Online). This class only handles Development
    // Auto-start Mode (MPPM tags / CLI args) - the normal Interactive Menu Mode path (a real
    // Player choosing Time Attack / Online Play) is driven entirely by MainMenuController and
    // never touches this class.
    //
    // Step 9.1: Normal Interactive Mode must ALWAYS land on MainMenu's Title screen - merely
    // having an MPPM "Host" Tag (a Development-only Player identity, set once on the Main
    // Editor for this project's MPPM setup) must never by itself start Networking or load
    // Gameplay. Development Auto-start is Explicit Opt-in only: developmentAutoStartViaMppmTag
    // must be deliberately enabled in the Inspector (defaults to false), and the CLI-arg path
    // already only ever applies outside the Editor (a genuine Standalone launch with an
    // explicit -host/-client argument - never a normal double-click launch).
    [DefaultExecutionOrder(-100)]
    public class NetworkBootstrap : MonoBehaviour
    {
        // Step 13.1 Root Cause: an Online Host returning from Gameplay to Stage/Character
        // Select (RequestReturnToStageSelect/RequestReturnToCharacterSelect) stays connected -
        // it reloads MainMenu.unity via NetworkManager.SceneManager.LoadScene() without ever
        // calling StartHost()/Shutdown() again. That reload instantiates a brand new copy of
        // this Scene-placed "NetworkManager" GameObject (Unity has no way to know a
        // DontDestroyOnLoad'd copy from the PREVIOUS load of the same Scene already exists),
        // and without this guard its Awake() would DontDestroyOnLoad THAT copy too - a second,
        // permanently-surviving root. NGO's own NetworkManager component self-protects, but its
        // plain-MonoBehaviour siblings on this GameObject (GameplayPlayerSpawner,
        // PlayerCharacterSelectionStore) do not, so each one's own Awake() would silently
        // overwrite its static Instance with the fresh, empty duplicate - which is exactly how
        // PlayerCharacterSelectionStore lost every recorded Character Selection on a second
        // Stage Select entry, making the next HostTryStartGame()'s AllSelected() check fail.
        public static NetworkBootstrap Instance { get; private set; }

        [Header("Development Auto-start (Explicit Opt-in only)")]
        [Tooltip("ONの場合のみ、この Player(Main EditorまたはMPPM Virtual Player)のMPPM Tag(Host/Client)に応じて自動的にNetworkを開始しGameplayへ直行します。通常のInteractive Menu Mode(普通にPlayした場合)では必ずOFFのままにしてください - MainMenuからのUser Actionでのみ開始するのが標準動作です。")]
        [SerializeField] private bool developmentAutoStartViaMppmTag = false;

        [Tooltip("Development Auto-start (MPPM Tag Opt-in、または-host/-client CLI引数)が発火した際、Hostであれば自動的にロードするGameplay Scene名")]
        [SerializeField] private string developmentGameplaySceneName = "SampleScene";

        private void Awake()
        {
            // Only one persistent NetworkManager root may exist across Menu <-> Gameplay
            // scene loads (Step 9, section 46: Duplicate NetworkManager禁止). Step 13.1: this
            // is now enforced, not just assumed - a duplicate reload of MainMenu.unity destroys
            // itself here before it can DontDestroyOnLoad a second root or let any sibling
            // component overwrite its own static Instance.
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (developmentAutoStartViaMppmTag && TryStartFromMultiplayerPlayModeTags()) return;
            StartFromCommandLineArgs();
        }

        private bool TryStartFromMultiplayerPlayModeTags()
        {
            var tags = CurrentPlayer.Tags;
            for (int i = 0; i < tags.Count; i++)
            {
                if (tags[i] == "Host")
                {
                    NetworkManager.Singleton.StartHost();
                    LoadDevelopmentGameplaySceneIfServer();
                    return true;
                }
                if (tags[i] == "Client")
                {
                    NetworkManager.Singleton.StartClient();
                    return true;
                }
            }
            return false;
        }

        private void StartFromCommandLineArgs()
        {
            if (Application.isEditor) return;

            string[] args = System.Environment.GetCommandLineArgs();
            foreach (var arg in args)
            {
                if (arg == "-host")
                {
                    NetworkManager.Singleton.StartHost();
                    LoadDevelopmentGameplaySceneIfServer();
                    return;
                }
                if (arg == "-client")
                {
                    NetworkManager.Singleton.StartClient();
                    return;
                }
            }
        }

        // Dev-auto-start historically dropped the tester directly into Gameplay (there was no
        // Menu to click through). Preserved here so MPPM/CLI testing keeps that same one-step
        // experience now that the Menu sits in front of it (Step 9, section 47). Client
        // instances need no equivalent call - NGO's Network Scene Management (EnableSceneManagement
        // is already on) synchronizes them to whatever Scene the Server loads.
        private void LoadDevelopmentGameplaySceneIfServer()
        {
            if (!NetworkManager.Singleton.IsServer) return;
            NetworkManager.Singleton.SceneManager.LoadScene(developmentGameplaySceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
    }
}
