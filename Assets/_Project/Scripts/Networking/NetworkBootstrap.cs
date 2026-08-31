using Unity.Multiplayer.PlayMode;
using Unity.Netcode;
using UnityEngine;

namespace Domino.Networking
{
    public class NetworkBootstrap : MonoBehaviour
    {
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
                    return;
                }
                if (arg == "-client")
                {
                    NetworkManager.Singleton.StartClient();
                    return;
                }
            }
        }
    }
}
