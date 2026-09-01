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
    [DefaultExecutionOrder(-100)]
    public class NetworkBootstrap : MonoBehaviour
    {
        [Tooltip("Development Auto-start (MPPM tag / CLI arg) が発火した際、Hostであれば自動的にロードするGameplay Scene名")]
        [SerializeField] private string developmentGameplaySceneName = "SampleScene";

        private void Awake()
        {
            // Only one persistent NetworkManager root may exist across Menu <-> Gameplay
            // scene loads (Step 9, section 46: Duplicate NetworkManager禁止). NetworkManager
            // itself already self-persists once Singleton is set, but this call is made
            // explicit here (idempotent if NGO also does it internally) so persistence does
            // not silently depend on undocumented internal behavior.
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (TryStartFromMultiplayerPlayModeTags()) return;
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
