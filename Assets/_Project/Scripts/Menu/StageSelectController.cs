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

        private readonly List<GameObject> _spawnedCards = new List<GameObject>();

        private void OnEnable()
        {
            Populate();
        }

        private void Populate()
        {
            for (int i = 0; i < _spawnedCards.Count; i++)
            {
                if (_spawnedCards[i] != null) Destroy(_spawnedCards[i]);
            }
            _spawnedCards.Clear();

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

                _spawnedCards.Add(card.gameObject);
            }
        }

        // Step 9A: only the Single Player (Local Host) path exists. Online Host selection
        // (Step 9C) will branch here on GameSessionContext.IsOnline instead of starting a
        // Session directly.
        private void OnStageCardClicked(StageDefinition stage)
        {
            if (GameSessionContext.IsOnline) return;

            GameSessionContext.SelectedStageId = stage.StageId;
            StartLocalHostAndLoadGameplay(stage.GameplaySceneName);
        }

        // Single Player = "Local Host, Connected Players = 1" (Step 9, section 11) - reuses the
        // exact same Host-Authoritative Gameplay path Online will use, no Relay connection.
        private void StartLocalHostAndLoadGameplay(string sceneName)
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null) return;

            if (!networkManager.IsListening)
            {
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
