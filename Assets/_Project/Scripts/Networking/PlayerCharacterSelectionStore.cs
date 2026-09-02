using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Domino.Networking
{
    // Step 12.1.1: Server-only Character Selection store that survives Scene transitions.
    //
    // Root Cause this exists to fix (Step 12.1 Bug B): GameplayPlayerSpawner.HandleLoadEventCompleted
    // runs AFTER the Gameplay Scene has fully loaded - by which point the MainMenu-bound
    // NetworkSessionState (a Scene-placed NetworkObject, not DontDestroyOnLoad) has already been
    // destroyed. Step 12.1 read Character Selections from NetworkSessionState.Instance there,
    // which was always null by then, silently falling back to Character 1 for every Player.
    //
    // Fix: this component lives on the persistent "NetworkManager" GameObject (DontDestroyOnLoad
    // via NetworkBootstrap.Awake(), same GameObject as GameplayPlayerSpawner/DominoSpawner), so it
    // survives every Menu<->Gameplay Scene transition for the lifetime of the Application/Session.
    // NetworkSessionState (Section 10 of the Step 12.1.1 request) remains the UI/Network RPC entry
    // point while actually IN Character Select, but writes through to this store rather than
    // keeping its own now-removed Dictionary.
    //
    // Only ever holds a CharacterId (int) per Client - never a Prefab/Animator/Material/GameObject
    // reference (Section 12).
    public class PlayerCharacterSelectionStore : MonoBehaviour
    {
        public static PlayerCharacterSelectionStore Instance { get; private set; }

        private const int DefaultCharacterId = 1;

        private readonly Dictionary<ulong, int> _characterIdByClient = new Dictionary<ulong, int>();
        private readonly HashSet<ulong> _hasSelectedByClient = new HashSet<ulong>();

        // Section 28: fired only when a Client that HAD a selection disconnects, so
        // NetworkSessionState (when alive - i.e. currently in Character Select) can keep its
        // synced SelectedCount accurate. A single source of truth for removal (this component's
        // own OnClientDisconnectCallback subscription) avoids relying on handler-subscription
        // ordering between two independent disconnect listeners.
        public event System.Action<ulong> ClientSelectionRemoved;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
            }
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            bool hadSelected = _hasSelectedByClient.Remove(clientId);
            _characterIdByClient.Remove(clientId);
            if (hadSelected) ClientSelectionRemoved?.Invoke(clientId);
        }

        public bool HasSelected(ulong clientId) => _hasSelectedByClient.Contains(clientId);

        // Section 24 (Step 12.1's original design): a Client absent from the store (never
        // selected) resolves to Character 1 (Classic Capsule) - used by GameplayPlayerSpawner as
        // the final Fallback, and safe even though the actual Stage Select Gate (Section 18-19)
        // should make this case unreachable for a normal Online flow.
        public int GetCharacterId(ulong clientId)
        {
            return _characterIdByClient.TryGetValue(clientId, out int characterId) ? characterId : DefaultCharacterId;
        }

        public void SetSelection(ulong clientId, int characterId)
        {
            _characterIdByClient[clientId] = characterId;
            _hasSelectedByClient.Add(clientId);
        }

        public int CountSelectedAmong(IEnumerable<ulong> connectedClientIds)
        {
            int count = 0;
            foreach (ulong clientId in connectedClientIds)
            {
                if (_hasSelectedByClient.Contains(clientId)) count++;
            }
            return count;
        }

        public bool AllSelected(IEnumerable<ulong> connectedClientIds)
        {
            foreach (ulong clientId in connectedClientIds)
            {
                if (!_hasSelectedByClient.Contains(clientId)) return false;
            }
            return true;
        }

        // Section 31: Pause -> Character Select returns with every currently-connected Player's
        // prior Gameplay Character Selection already valid - marks them Selected (keeping their
        // existing CharacterId, defaulting only if somehow absent) so the Host's Stage Select
        // stays immediately available instead of forcing a fresh empty re-selection round.
        public void MarkAllConnectedAsSelected(IEnumerable<ulong> connectedClientIds)
        {
            foreach (ulong clientId in connectedClientIds)
            {
                if (!_characterIdByClient.ContainsKey(clientId)) _characterIdByClient[clientId] = DefaultCharacterId;
                _hasSelectedByClient.Add(clientId);
            }
        }

        public void ResetForNewSession()
        {
            _characterIdByClient.Clear();
            _hasSelectedByClient.Clear();
        }
    }
}
