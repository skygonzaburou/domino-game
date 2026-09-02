using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Domino.Menu
{
    // Step 12.1. Mirrors StageSelectController's structure closely: owns the Character grid
    // content and the Single Player "select and continue" vs Online "each Player selects
    // independently, Host advances" duality. MainMenuController only ever shows/hides this Panel.
    public class CharacterSelectController : MonoBehaviour
    {
        [SerializeField] private CharacterCatalog catalog;
        [SerializeField] private RectTransform gridContainer;
        [SerializeField] private Button cardTemplate;

        [Header("Online (Step 12.1)")]
        [SerializeField] private TextMeshProUGUI selectionStatusText;
        [SerializeField] private Button stageSelectButton;
        [SerializeField] private TextMeshProUGUI stageSelectButtonText;

        private readonly List<Button> _spawnedCards = new List<Button>();
        private readonly List<int> _spawnedCardCharacterIds = new List<int>();

        private void OnEnable()
        {
            Populate();
            RefreshSelectionHighlight();

            if (selectionStatusText != null) selectionStatusText.gameObject.SetActive(GameSessionContext.IsOnline);
            if (GameSessionContext.IsOnline) RefreshOnlineGating();
        }

        private void Update()
        {
            // Online-only: Host-only Stage Select gating can change as Player Count changes -
            // mirrors GameplayHUD's continuous-poll pattern for the same reason (no single event
            // fires for "IsHost/PlayerCount changed" here).
            if (GameSessionContext.IsOnline) RefreshOnlineGating();
        }

        private void Populate()
        {
            for (int i = 0; i < _spawnedCards.Count; i++)
            {
                if (_spawnedCards[i] != null) Destroy(_spawnedCards[i].gameObject);
            }
            _spawnedCards.Clear();
            _spawnedCardCharacterIds.Clear();

            if (catalog == null || cardTemplate == null || gridContainer == null) return;

            foreach (CharacterDefinition character in catalog.Characters)
            {
                if (character == null) continue;

                Button card = Instantiate(cardTemplate, gridContainer);
                card.gameObject.SetActive(true);
                card.gameObject.name = "CharacterCard_" + character.CharacterId;

                TextMeshProUGUI label = card.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null) label.text = $"{character.CharacterId:00}\n{character.DisplayName}";

                CharacterDefinition captured = character;
                card.onClick.AddListener(() => OnCharacterCardClicked(captured));

                _spawnedCards.Add(card);
                _spawnedCardCharacterIds.Add(character.CharacterId);
            }
        }

        // Section 8: a click only ever changes SelectedCharacterId (local highlight, optimistic)
        // - it never immediately advances to Stage Select, Single Player or Online alike. Online
        // additionally informs the Server so it can resolve this Client's pick at Gameplay Player
        // Spawn time (Section 15-18).
        private void OnCharacterCardClicked(CharacterDefinition character)
        {
            GameSessionContext.SelectedCharacterId = character.CharacterId;
            RefreshSelectionHighlight();

            if (GameSessionContext.IsOnline)
            {
                NetworkSessionState.Instance?.RequestSelectCharacterRpc(character.CharacterId);
            }
        }

        // Section 9/22/36: the actual "next" action - Single Player advances immediately, Online
        // is Host-only (re-checked here defensively, matching every other Host-only action in
        // this project) and lets NetworkSessionState.PhaseChanged move every Client's Menu
        // together (OnlineSessionController.HandlePhaseChanged).
        public void OnStageSelectButtonClicked()
        {
            if (!GameSessionContext.IsOnline)
            {
                MainMenuController.Instance.ShowState(MenuState.StageSelect);
                return;
            }

            OnlineSessionController.Instance?.OnStageSelectButtonClicked();
        }

        private void RefreshSelectionHighlight()
        {
            int selectedId = GameSessionContext.SelectedCharacterId;
            for (int i = 0; i < _spawnedCards.Count; i++)
            {
                Button card = _spawnedCards[i];
                if (card == null) continue;

                Image image = card.GetComponent<Image>();
                if (image != null)
                {
                    bool isSelected = _spawnedCardCharacterIds[i] == selectedId;
                    image.color = isSelected
                        ? new Color(1f, 0.85f, 0.3f, 1f)
                        : new Color(0.2f, 0.55f, 0.9f, 1f);
                }
            }
        }

        // Section 20/32: the "STAGE SELECT" Button that advances everyone is Host-only when
        // Online - a Client sees it Disabled with a "(Host Only)" label, same pattern as
        // GameplayHUD's Pause Stage Select gating. Character Cards themselves are NOT gated here
        // - every connected Player (Host and Client alike) can always pick their own Character
        // (Section 16).
        private void RefreshOnlineGating()
        {
            bool isHost = OnlineSessionController.Instance != null && OnlineSessionController.Instance.IsHost;

            if (stageSelectButton != null) stageSelectButton.interactable = isHost;
            if (stageSelectButtonText != null) stageSelectButtonText.text = isHost ? "STAGE SELECT" : "STAGE SELECT (Host Only)";
            if (selectionStatusText != null)
            {
                selectionStatusText.text = isHost
                    ? "Select your Character, then press STAGE SELECT"
                    : "Select your Character. Waiting for the host to proceed...";
            }
        }
    }
}
