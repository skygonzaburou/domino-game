using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Domino.Menu
{
    // Step 12.1. Mirrors StageSelectController's structure closely: owns the Character grid
    // content and the Single Player "select and continue" vs Online "each Player selects
    // independently, Host advances" duality. MainMenuController only ever shows/hides this Panel.
    //
    // Step 12.1.1: Online now gates on every connected Player having selected at least once
    // (Section 14-30) before the Host can even attempt Stage Select, and the Host must explicitly
    // confirm via a Yes/No dialog once everyone has.
    public class CharacterSelectController : MonoBehaviour
    {
        [SerializeField] private CharacterCatalog catalog;
        [SerializeField] private RectTransform gridContainer;
        [SerializeField] private Button cardTemplate;

        [Tooltip("Step 13.2: CharacterDefinition.Portraitが未設定(None)のCharacter Cardに使うFallback画像")]
        [SerializeField] private Sprite placeholderPortrait;

        [Header("Online (Step 12.1)")]
        [SerializeField] private TextMeshProUGUI selectionStatusText;
        [SerializeField] private Button stageSelectButton;
        [SerializeField] private TextMeshProUGUI stageSelectButtonText;

        [Header("Online Player/Selection Count (Step 12.1.1)")]
        [SerializeField] private TextMeshProUGUI selectionCountText;

        [Header("Host Confirmation (Step 12.1.1)")]
        [SerializeField] private GameObject confirmationPanel;
        [SerializeField] private TextMeshProUGUI confirmationMessageText;
        [SerializeField] private Button confirmYesButton;
        [SerializeField] private Button confirmNoButton;

        private readonly List<Button> _spawnedCards = new List<Button>();
        private readonly List<int> _spawnedCardCharacterIds = new List<int>();

        private void Awake()
        {
            if (confirmYesButton != null) confirmYesButton.onClick.AddListener(OnConfirmYes);
            if (confirmNoButton != null) confirmNoButton.onClick.AddListener(OnConfirmNo);
        }

        private void OnEnable()
        {
            Populate();
            RefreshSelectionHighlight();
            SetActive(confirmationPanel, false);

            if (selectionStatusText != null) selectionStatusText.gameObject.SetActive(GameSessionContext.IsOnline);
            if (selectionCountText != null) selectionCountText.gameObject.SetActive(GameSessionContext.IsOnline);
            if (GameSessionContext.IsOnline) RefreshOnlineGating();
        }

        private void Update()
        {
            // Online-only: Host-only Stage Select gating can change as Player Count/Selection
            // Count changes - mirrors GameplayHUD's continuous-poll pattern for the same reason
            // (no single event fires for "IsHost/PlayerCount changed" here).
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

                // Step 13.2 (Section 13): a Character with no Portrait set falls back to the
                // shared Placeholder rather than showing a blank/broken Image - adding a new
                // Character without remembering to set Portrait must never break the UI.
                Transform portraitTransform = card.transform.Find("Portrait");
                Image portraitImage = portraitTransform != null ? portraitTransform.GetComponent<Image>() : null;
                if (portraitImage != null)
                {
                    portraitImage.sprite = character.Portrait != null ? character.Portrait : placeholderPortrait;
                }

                CharacterDefinition captured = character;
                card.onClick.AddListener(() => OnCharacterCardClicked(captured));

                _spawnedCards.Add(card);
                _spawnedCardCharacterIds.Add(character.CharacterId);
            }
        }

        // Section 8/17/22: a click always updates SelectedCharacterId (local highlight,
        // optimistic) and never immediately advances to Stage Select. Online additionally informs
        // the Server (which tracks HasSelectedCharacter separately - Section 15) so it can resolve
        // this Client's pick at Gameplay Player Spawn time, and remains changeable at any point
        // up until the Host's Stage Select Confirmation actually fires (Section 22).
        private void OnCharacterCardClicked(CharacterDefinition character)
        {
            GameSessionContext.SelectedCharacterId = character.CharacterId;
            RefreshSelectionHighlight();

            if (GameSessionContext.IsOnline)
            {
                GameSessionContext.HasSelectedCharacterThisSession = true;
                NetworkSessionState.Instance?.RequestSelectCharacterRpc(character.CharacterId);
            }
        }

        // Section 9/21/36: Single Player advances immediately. Online now shows a Host
        // Confirmation first (Section 21) instead of advancing directly - the actual Phase
        // Transition only happens from OnConfirmYes.
        public void OnStageSelectButtonClicked()
        {
            if (!GameSessionContext.IsOnline)
            {
                MainMenuController.Instance.ShowState(MenuState.StageSelect);
                return;
            }

            if (OnlineSessionController.Instance == null || !OnlineSessionController.Instance.IsHost) return;

            if (confirmationMessageText != null) confirmationMessageText.text = "All players have selected.\nProceed to Stage Select?";
            SetActive(confirmationPanel, true);
        }

        // Section 24: the actual Server call (and its own independent re-validation) only happens
        // here - Yes is the single point where Character Selection for this Attempt is committed.
        private void OnConfirmYes()
        {
            SetActive(confirmationPanel, false);
            OnlineSessionController.Instance?.OnStageSelectButtonClicked();
        }

        // Section 23: No just closes the dialog - no Selection/HasSelected change, Stage Select
        // stays available, every Player can keep changing their Character.
        private void OnConfirmNo()
        {
            SetActive(confirmationPanel, false);
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

        // Section 18-20/26/27: the All-player Gate. stageSelectButton is only ever interactable
        // for the Host once every currently-connected Player has selected at least once - Character
        // Cards themselves are never gated here (Section 16: Host and Client alike can always pick
        // their own Character, right up until the Host's Confirmation actually commits).
        private void RefreshOnlineGating()
        {
            bool isHost = OnlineSessionController.Instance != null && OnlineSessionController.Instance.IsHost;
            int selectedCount = NetworkSessionState.Instance != null ? NetworkSessionState.Instance.SelectedCount : 0;
            int totalCount = OnlineSessionController.Instance != null ? OnlineSessionController.Instance.PlayerCount : 0;
            bool allSelected = totalCount > 0 && selectedCount >= totalCount;

            if (selectionCountText != null)
            {
                selectionCountText.text = $"Players: {totalCount} / {OnlineSessionController.MaxPlayersPublic}\nSelected: {selectedCount} / {totalCount}";
            }

            if (stageSelectButton != null) stageSelectButton.interactable = isHost && allSelected;
            if (stageSelectButtonText != null)
            {
                stageSelectButtonText.text = !isHost
                    ? "STAGE SELECT (Host Only)"
                    : (allSelected ? "STAGE SELECT" : "STAGE SELECT (Waiting)");
            }

            if (selectionStatusText != null)
            {
                selectionStatusText.text = BuildStatusText(isHost, allSelected);
            }
        }

        // Section 19/20/26: Host and Client see different framing of the same underlying state.
        private string BuildStatusText(bool isHost, bool allSelected)
        {
            bool hasSelectedSelf = GameSessionContext.HasSelectedCharacterThisSession;

            if (isHost)
            {
                return allSelected
                    ? "All players have selected."
                    : "Waiting for all players to choose a character...";
            }

            if (allSelected) return "All players selected.\nWaiting for the host...";
            if (hasSelectedSelf) return $"Selected: {ResolveDisplayName(GameSessionContext.SelectedCharacterId)}\nWaiting for other players...";
            return "Choose your character.";
        }

        private string ResolveDisplayName(int characterId)
        {
            CharacterDefinition definition = catalog != null ? catalog.FindById(characterId) : null;
            return definition != null ? definition.DisplayName : characterId.ToString();
        }

        private static void SetActive(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active) go.SetActive(active);
        }
    }
}
