using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Domino.Menu
{
    // Owns Panel visibility / navigation only (Step 9, section 51 - responsibilities split
    // across MainMenuController / StageSelectController / OnlineSessionController). Does not
    // know about Stage data or Networking; StageSelectController and (from Step 9B)
    // OnlineSessionController own those concerns and call back into ShowState() to navigate.
    public class MainMenuController : MonoBehaviour
    {
        public static MainMenuController Instance { get; private set; }

        [Header("Panels")]
        [SerializeField] private GameObject titlePanel;
        [SerializeField] private GameObject modeSelectPanel;
        [SerializeField] private GameObject onlineSelectPanel;
        [SerializeField] private GameObject onlineCreatePanel;
        [SerializeField] private GameObject onlineJoinPanel;
        [SerializeField] private GameObject onlineRoomPanel;
        [SerializeField] private GameObject stageSelectPanel;

        private Dictionary<MenuState, GameObject> _panels;
        private readonly Stack<MenuState> _history = new Stack<MenuState>();
        public MenuState CurrentState { get; private set; }

        private void Awake()
        {
            Instance = this;
            _panels = new Dictionary<MenuState, GameObject>
            {
                { MenuState.Title, titlePanel },
                { MenuState.ModeSelect, modeSelectPanel },
                { MenuState.OnlineSelect, onlineSelectPanel },
                { MenuState.OnlineCreate, onlineCreatePanel },
                { MenuState.OnlineJoin, onlineJoinPanel },
                { MenuState.OnlineRoom, onlineRoomPanel },
                { MenuState.StageSelect, stageSelectPanel },
            };
        }

        private void Start()
        {
            ShowState(MenuState.Title, pushHistory: false);
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                OnBackClicked();
            }
        }

        public void ShowState(MenuState state, bool pushHistory = true)
        {
            if (pushHistory && CurrentState != state) _history.Push(CurrentState);

            foreach (var kv in _panels)
            {
                if (kv.Value != null) kv.Value.SetActive(kv.Key == state);
            }
            CurrentState = state;
        }

        // Bound to every Panel's Back button, and to the Escape key (Step 9, section 17).
        // Title has nowhere to go back to, so it is a no-op there.
        public void OnBackClicked()
        {
            if (CurrentState == MenuState.Title) return;
            MenuState previous = _history.Count > 0 ? _history.Pop() : MenuState.Title;
            ShowState(previous, pushHistory: false);
        }

        public void OnStartClicked()
        {
            ShowState(MenuState.ModeSelect);
        }

        public void OnTimeAttackClicked()
        {
            GameSessionContext.IsOnline = false;
            ShowState(MenuState.StageSelect);
        }

        public void OnOnlinePlayClicked()
        {
            ShowState(MenuState.OnlineSelect);
        }

        public void OnCreateRoomClicked()
        {
            ShowState(MenuState.OnlineCreate);
        }

        public void OnJoinRoomClicked()
        {
            ShowState(MenuState.OnlineJoin);
        }
    }
}
