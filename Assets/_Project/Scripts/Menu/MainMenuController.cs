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
        [SerializeField] private GameObject characterSelectPanel;
        [SerializeField] private GameObject stageSelectPanel;

        private Dictionary<MenuState, GameObject> _panels;
        private readonly Stack<MenuState> _history = new Stack<MenuState>();
        public MenuState CurrentState { get; private set; }

        private void Awake()
        {
            Instance = this;

            // Step 10.2: MainMenu is the single source of truth for Menu-usable Cursor state,
            // applied unconditionally every time this Scene loads - whether that is the very
            // first launch or a return from Gameplay via any path (Pause/Result x Stage Select/
            // Title x Single Player/Online/Host disconnect). Gameplay's own Cursor Lock
            // (ThirdPersonCamera.Start()) is a one-way "enter Gameplay" action with nothing that
            // ever reverses it - rather than adding a matching Cursor Unlock to every place
            // Gameplay can be exited from, this one Entry Point guarantees the correct state
            // regardless of which of those paths was actually taken.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            _panels = new Dictionary<MenuState, GameObject>
            {
                { MenuState.Title, titlePanel },
                { MenuState.ModeSelect, modeSelectPanel },
                { MenuState.OnlineSelect, onlineSelectPanel },
                { MenuState.OnlineCreate, onlineCreatePanel },
                { MenuState.OnlineJoin, onlineJoinPanel },
                { MenuState.OnlineRoom, onlineRoomPanel },
                { MenuState.CharacterSelect, characterSelectPanel },
                { MenuState.StageSelect, stageSelectPanel },
            };
        }

        private void Start()
        {
            // Step 10B (Section 29): a Gameplay Pause Menu / SUCCESS Result "Stage Select"
            // action sets this before loading MainMenu, so the Player lands back on Stage
            // Select directly instead of Title. Consumed once - an ordinary Title return (or a
            // fresh MainMenu Start with nothing special set) leaves this null and falls through
            // to the original default.
            if (GameSessionContext.ReturnDestination.HasValue)
            {
                MenuState destination = GameSessionContext.ReturnDestination.Value;
                GameSessionContext.ReturnDestination = null;
                ShowState(destination, pushHistory: false);
                return;
            }

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

            // Step 12.1 (Section 37): Online Character Select intentionally has no Back - a
            // Client popping Local History back toward Room/OnlineSelect would desync from the
            // actual Session Phase (still CharacterSelect for everyone else), and the Host doing
            // the same would abandon the very Session it is supposed to be driving forward.
            // Leaving is only ever via the existing Leave Session flow (Online Room Panel).
            if (CurrentState == MenuState.CharacterSelect && GameSessionContext.IsOnline) return;

            MenuState previous = _history.Count > 0 ? _history.Pop() : MenuState.Title;
            ShowState(previous, pushHistory: false);
        }

        public void OnStartClicked()
        {
            ShowState(MenuState.ModeSelect);
        }

        // Step 12.1 (Section 2): Time Attack now leads to Character Select first - Stage Select
        // is reached one step later, from CharacterSelectController's own "STAGE SELECT" Button.
        public void OnTimeAttackClicked()
        {
            GameSessionContext.IsOnline = false;
            ShowState(MenuState.CharacterSelect);
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
