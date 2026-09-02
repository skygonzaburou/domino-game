using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Domino.Menu;
using Domino.Player;

namespace Domino.Networking
{
    // Lives on the persistent "NetworkManager" GameObject (same as NetworkBootstrap - Step 9.2).
    //
    // Root Cause this exists to fix: Connection Approval previously left CreatePlayerObject at
    // its default (true), so NGO spawned each Player's NetworkObject the instant they connected
    // - while still in MainMenu (Room/Stage Select), which has no Ground. With Rigidbody gravity
    // active and nothing to land on, the Player fell continuously; the SAME NetworkObject then
    // carried that fall straight through the Menu -> Gameplay Scene transition (confirmed live:
    // Y = -149 while still in the Room, Y = -937 moments after the Gameplay Scene finished
    // loading - one persistent object, never repositioned).
    //
    // Fix: Connection Approval (OnlineSessionController for Online, this class's own Awake for
    // Single Player - see StageSelectController.StartLocalHostAndLoadGameplay) now always sets
    // CreatePlayerObject = false, so no Gameplay Player exists at all until the Gameplay Scene
    // has actually finished loading. This class listens for that completion
    // (NetworkSceneManager.OnLoadEventCompleted, Server-only) and explicitly spawns one Player
    // per completed Client at a Scene-authored Spawn Point inside Start Area.
    [DefaultExecutionOrder(-100)]
    public class GameplayPlayerSpawner : MonoBehaviour
    {
        // Step 10A: exposed so GameFlowManager.RepositionAllPlayersToSpawnPoints() (Restart/
        // Retry) can reuse the exact same Spawn Point resolution as the initial Spawn below,
        // instead of duplicating the GameObject.Find lookup.
        public static GameplayPlayerSpawner Instance { get; private set; }

        [Tooltip("Player Spawn対象のGameplay Sceneかどうかを判定するStage Catalog。ここに登録されたいずれかのStageDefinition.GameplaySceneNameと一致するSceneだけがGameplay Scene扱いになる(Step 13: 特定のScene名をHard-codeしない - MainMenuはCatalogに存在しないため誤判定しない)")]
        [SerializeField] private StageCatalog stageCatalog;

        [Tooltip("Gameplay Scene内でSpawn Pointの子Transformを探す、親GameObjectの名前")]
        [SerializeField] private string spawnPointsContainerName = "PlayerSpawnPoints";

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnServerStarted += HandleServerStarted;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;

            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnServerStarted -= HandleServerStarted;
                if (NetworkManager.Singleton.SceneManager != null)
                {
                    NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;
                }
            }
        }

        // Re-subscribed fresh every time the Server (re)starts (Single Player Retry-into-a-new
        // session, or Leave + Create a new Online Room both call StartHost() again later in the
        // same Application lifetime) - the defensive -= first avoids double-firing if NGO ever
        // reuses the same NetworkSceneManager instance across a Shutdown/StartHost cycle.
        private void HandleServerStarted()
        {
            if (NetworkManager.Singleton == null || NetworkManager.Singleton.SceneManager == null) return;
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += HandleLoadEventCompleted;
        }

        // Late Join is already rejected server-side once the Session reaches StartingGame/InGame
        // (Step 9C, OnlineSessionController.HandleConnectionApproval - unchanged) before this
        // Scene Load even begins, so every currently-connected Client is exactly who needs a
        // Gameplay Player and no one else ever will for this Attempt.
        private void HandleLoadEventCompleted(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            if (!IsGameplayScene(sceneName)) return;

            GameObject prefab = NetworkManager.Singleton.NetworkConfig.PlayerPrefab;
            if (prefab == null)
            {
                Debug.LogError("[GameplayPlayerSpawner] NetworkConfig.PlayerPrefab is not set - cannot spawn Gameplay Players.");
                return;
            }

            Transform[] spawnPoints = ResolveSpawnPoints();

            for (int i = 0; i < clientsCompleted.Count; i++)
            {
                ulong clientId = clientsCompleted[i];

                // Defensive (section 16): never create a second Player Object for a Client that
                // somehow already has one.
                if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) && client.PlayerObject != null)
                {
                    continue;
                }

                Vector3 position = Vector3.zero;
                Quaternion rotation = Quaternion.identity;
                if (spawnPoints != null && spawnPoints.Length > 0)
                {
                    Transform point = spawnPoints[i % spawnPoints.Length];
                    position = point.position;
                    rotation = point.rotation;
                }
                else
                {
                    Debug.LogWarning("[GameplayPlayerSpawner] No Spawn Points found under '" + spawnPointsContainerName + "' - spawning at World Origin.");
                }

                NetworkObject spawned = NetworkObject.InstantiateAndSpawn(prefab, NetworkManager.Singleton, ownerClientId: clientId, isPlayerObject: true, position: position, rotation: rotation);

                // Step 12.1 (Section 23): Server-authoritative Character assignment, applied
                // right after Spawn. Single Player has exactly one Client (this Host itself), so
                // its own local GameSessionContext.SelectedCharacterId is authoritative directly.
                //
                // Step 12.1.1 Bug B fix: Online used to resolve this via
                // NetworkSessionState.Instance, a Scene-placed NetworkObject bound to
                // MainMenu.unity - but this method only runs AFTER the Gameplay Scene has
                // finished loading (OnLoadEventCompleted), by which point MainMenu (and its
                // NetworkSessionState) is already unloaded/destroyed. Instance was always null
                // here, so every Online Player silently fell back to Character 1. Resolving via
                // PlayerCharacterSelectionStore instead - a Persistent component on this same
                // GameObject's Scene (see that file) - fixes this, since it survives the Scene
                // transition.
                int characterId = GameSessionContext.IsOnline
                    ? (PlayerCharacterSelectionStore.Instance != null ? PlayerCharacterSelectionStore.Instance.GetCharacterId(clientId) : 1)
                    : GameSessionContext.SelectedCharacterId;

                PlayerCharacterVisual characterVisual = spawned.GetComponent<PlayerCharacterVisual>();
                characterVisual?.ServerSetCharacterId(characterId);
            }
        }

        // Step 10B (Section 31): symmetric counterpart to the Spawn-on-Scene-Load logic above.
        // Called (Server-only) right before returning from Gameplay to MainMenu's Stage Select
        // while staying networked (Online "Stage Select" from the Pause Menu / SUCCESS Result) -
        // a NetworkManager.SceneManager.LoadScene(..., LoadSceneMode.Single) does NOT by itself
        // destroy dynamically-Spawned Player NetworkObjects (only Scene-placed ones get torn
        // down automatically). This is exactly the Root Cause mechanism Step 9.2 fixed for the
        // forward Menu -> Gameplay direction (a Player silently surviving a Scene transition);
        // the same risk exists in reverse and must be handled explicitly here so no Gameplay
        // Player ever reappears - falling - in MainMenu's Room/Stage Select.
        public void DespawnAllGameplayPlayers()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject != null && client.PlayerObject.IsSpawned)
                {
                    client.PlayerObject.Despawn(true);
                }
            }
        }

        // Step 13: any Scene referenced by some StageDefinition.GameplaySceneName in the Catalog
        // counts as a Gameplay Scene - MainMenu is never in the Catalog, so it can never
        // accidentally match here (Section 15: must not regress the Step 9.2 MainMenu-Spawn bug).
        // Adding a new Stage Scene (StageScene_3, etc.) needs no change here - it starts working
        // the moment some StageDefinition's GameplaySceneName points at it.
        private bool IsGameplayScene(string sceneName)
        {
            if (stageCatalog == null) return false;
            foreach (StageDefinition stage in stageCatalog.Stages)
            {
                if (stage != null && stage.GameplaySceneName == sceneName) return true;
            }
            return false;
        }

        // One-time (per Gameplay Scene Load, not per-frame) lookup - Spawn Points live in the
        // Gameplay Scene itself, so a serialized reference from this persistent
        // (DontDestroyOnLoad) component would not survive across the Menu -> Gameplay Scene
        // boundary.
        public Transform[] ResolveSpawnPoints()
        {
            GameObject container = GameObject.Find(spawnPointsContainerName);
            if (container == null) return null;

            var points = new List<Transform>();
            foreach (Transform child in container.transform) points.Add(child);
            return points.ToArray();
        }
    }
}
