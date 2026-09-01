using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Domino.Menu
{
    // Owns the Stage grid content and the actual Session-starting logic (Step 9, section 51).
    // MainMenuController only ever shows/hides this Panel - it has no knowledge of StageCatalog
    // or NetworkManager.
    public class StageSelectController : MonoBehaviour
    {
        [SerializeField] private StageCatalog catalog;
        [SerializeField] private RectTransform gridContainer;
        [SerializeField] private Button cardTemplate;

        [Header("Online (Step 9C)")]
        [SerializeField] private TextMeshProUGUI selectionStatusText;
        [SerializeField] private Button startGameButton;

        private readonly List<Button> _spawnedCards = new List<Button>();
        private readonly List<int> _spawnedCardStageIds = new List<int>();

        private void OnEnable()
        {
            Populate();

            if (GameSessionContext.IsOnline && NetworkSessionState.Instance != null)
            {
                NetworkSessionState.Instance.SelectedStageChanged += HandleSelectedStageChanged;
                RefreshOnlineUI();
            }

            if (startGameButton != null) startGameButton.gameObject.SetActive(GameSessionContext.IsOnline);
            if (selectionStatusText != null) selectionStatusText.gameObject.SetActive(GameSessionContext.IsOnline);
        }

        private void OnDisable()
        {
            if (NetworkSessionState.Instance != null)
            {
                NetworkSessionState.Instance.SelectedStageChanged -= HandleSelectedStageChanged;
            }
        }

        private void Populate()
        {
            for (int i = 0; i < _spawnedCards.Count; i++)
            {
                if (_spawnedCards[i] != null) Destroy(_spawnedCards[i].gameObject);
            }
            _spawnedCards.Clear();
            _spawnedCardStageIds.Clear();

            if (catalog == null || cardTemplate == null || gridContainer == null) return;

            foreach (StageDefinition stage in catalog.Stages)
            {
                if (stage == null) continue;

                Button card = Instantiate(cardTemplate, gridContainer);
                card.gameObject.SetActive(true);
                card.gameObject.name = "StageCard_" + stage.StageId;

                TextMeshProUGUI label = card.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null) label.text = stage.DisplayName;

                StageDefinition capturedStage = stage;
                card.onClick.AddListener(() => OnStageCardClicked(capturedStage));

                _spawnedCards.Add(card);
                _spawnedCardStageIds.Add(stage.StageId);
            }
        }

        // Single Player (Step 9A): a click both selects and immediately starts - one step.
        // Online (Step 9C): a click only highlights a choice (Host-only); starting is a
        // separate explicit action via the START GAME button (section 37).
        private void OnStageCardClicked(StageDefinition stage)
        {
            if (!GameSessionContext.IsOnline)
            {
                GameSessionContext.SelectedStageId = stage.StageId;
                StartLocalHostAndLoadGameplay(stage.GameplaySceneName);
                return;
            }

            if (OnlineSessionController.Instance == null || !OnlineSessionController.Instance.IsHost) return;
            NetworkSessionState.Instance?.HostSelectStage(stage.StageId);
        }

        public void OnStartGameButtonClicked()
        {
            OnlineSessionController.Instance?.OnStartGameButtonClicked();
        }

        private void HandleSelectedStageChanged(int stageId)
        {
            RefreshOnlineUI();
        }

        // Host-only card interactability (section 34), Selected Stage highlight (section 35),
        // and the Waiting/Selected status text (section 36) - all driven off the same
        // NetworkSessionState.SelectedStageId read, so Host and Client render consistently
        // from the same synced source rather than tracking a separate local copy.
        private void RefreshOnlineUI()
        {
            bool isHost = OnlineSessionController.Instance != null && OnlineSessionController.Instance.IsHost;
            int selectedStageId = NetworkSessionState.Instance != null ? NetworkSessionState.Instance.SelectedStageId : -1;

            for (int i = 0; i < _spawnedCards.Count; i++)
            {
                Button card = _spawnedCards[i];
                if (card == null) continue;

                card.interactable = isHost;

                Image image = card.GetComponent<Image>();
                if (image != null)
                {
                    bool isSelected = _spawnedCardStageIds[i] == selectedStageId;
                    image.color = isSelected
                        ? new Color(1f, 0.85f, 0.3f, 1f)
                        : new Color(0.2f, 0.55f, 0.9f, 1f);
                }
            }

            if (startGameButton != null)
            {
                startGameButton.interactable = isHost && selectedStageId >= 0;
            }

            if (selectionStatusText != null)
            {
                StageDefinition selectedStage = catalog != null ? catalog.FindById(selectedStageId) : null;
                string stageName = selectedStage != null ? selectedStage.DisplayName : null;

                if (isHost)
                {
                    selectionStatusText.text = stageName != null ? $"SELECTED: {stageName}" : "Select a Stage";
                }
                else
                {
                    selectionStatusText.text = stageName != null
                        ? $"Host selected: {stageName}"
                        : "Waiting for the host to choose a stage...";
                }
            }
        }

        // Single Player = "Local Host, Connected Players = 1" (Step 9, section 11) - reuses the
        // exact same Host-Authoritative Gameplay path Online will use, no Relay connection.
        private void StartLocalHostAndLoadGameplay(string sceneName)
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null) return;

            if (!networkManager.IsListening)
            {
                // Same CreatePlayerObject = false deferral Online uses (Step 9.2) - without
                // this, the Gameplay Player auto-spawns the instant StartHost() connects (while
                // still in MainMenu, no Ground) and falls indefinitely; GameplayPlayerSpawner
                // creates it properly once SampleScene has actually finished loading.
                OnlineSessionController.SetupConnectionApproval();

                bool started = networkManager.StartHost();
                if (!started)
                {
                    // A failed StartHost() (e.g. transport bind failure) must not fall through
                    // to a Scene load - NetworkSceneManager.LoadScene() on a non-listening
                    // NetworkManager is undefined behavior, not a safe no-op.
                    Debug.LogError("[StageSelectController] StartHost() failed - not loading Gameplay Scene.");
                    return;
                }
            }

            networkManager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }
    }
}
