using Unity.Netcode;
using UnityEngine;
using Domino.Menu;

namespace Domino.Player
{
    // Step 12.1: the only thing that changes between Characters is which of Player's existing
    // Visual children is Active - Gameplay Architecture (Rigidbody/CapsuleCollider/PlayerMotor/
    // NetworkTransform/PlacementController/etc, Section 0) is completely untouched. Server-
    // authoritative CharacterId (Section 15 - Client-Local-only Visual switching is explicitly
    // forbidden) is set once by GameplayPlayerSpawner right after Spawn; every Client (owner and
    // observers alike) reacts identically to the synced value, so Host and every connected
    // Client always render the same Character for a given Player.
    //
    // No per-Id hardcoded switch (Section 5/12/41): CharacterCatalog resolves an Id to a
    // CharacterDefinition.VisualKey, and this script just activates whichever entry in
    // visualRoots has that name - adding a future Character is "add a new Visual child + a new
    // Definition asset", never a C# change here.
    public class PlayerCharacterVisual : NetworkBehaviour
    {
        [Tooltip("Character IDをVisualへ解決するためのCatalog参照")]
        [SerializeField] private CharacterCatalog catalog;

        [Tooltip("Playerが持つ全Visual Root(例: Visual[Capsule], GreenBoyVisual)。CharacterDefinition.VisualKeyと同名のものだけがActiveになる")]
        [SerializeField] private GameObject[] visualRoots;

        [Tooltip("GreenBoyVisual配下の共有SkinnedMeshRenderer(Model/char1)。色違いCharacter(Step 12.1.2b)のMaterial切り替え先")]
        [SerializeField] private SkinnedMeshRenderer greenBoyRenderer;

        [Tooltip("CharacterDefinition.VisualMaterialが未設定の場合に使うDefault Material(GreenBoy_Material)")]
        [SerializeField] private Material greenBoyDefaultMaterial;

        private const int FallbackCharacterId = 1;

        private readonly NetworkVariable<int> _characterId = new NetworkVariable<int>(
            FallbackCharacterId, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public int CharacterId => _characterId.Value;

        public override void OnNetworkSpawn()
        {
            _characterId.OnValueChanged += HandleCharacterIdChanged;
            ApplyVisual(_characterId.Value);
        }

        public override void OnNetworkDespawn()
        {
            _characterId.OnValueChanged -= HandleCharacterIdChanged;
        }

        // Server-only. Called by GameplayPlayerSpawner immediately after this Player's
        // NetworkObject Spawns (Section 23). An Id the Catalog does not recognize (Section 24:
        // "must never be Invisible") safely falls back to Character 1 rather than being applied
        // as-is.
        public void ServerSetCharacterId(int characterId)
        {
            if (!IsServer) return;

            CharacterDefinition definition = catalog != null ? catalog.FindById(characterId) : null;
            _characterId.Value = definition != null ? characterId : FallbackCharacterId;
        }

        private void HandleCharacterIdChanged(int previous, int current) => ApplyVisual(current);

        private void ApplyVisual(int characterId)
        {
            CharacterDefinition definition = catalog != null ? catalog.FindById(characterId) : null;
            string visualKey = definition != null ? definition.VisualKey : ResolveFallbackVisualKey();

            if (visualRoots != null)
            {
                foreach (GameObject root in visualRoots)
                {
                    if (root != null) root.SetActive(root.name == visualKey);
                }
            }

            // Step 12.1.2b: GreenBoyVisual is a single shared Model/Animator (Section 6 - never
            // duplicated per Character), so a color variant is expressed as a Material swap on
            // this one Renderer rather than a new Visual Root. Always resolved here (not only
            // when a custom Material is set) so switching back to a Definition without one
            // reliably restores the Default rather than leaving a previous selection's color.
            if (greenBoyRenderer != null)
            {
                Material material = definition != null && definition.VisualMaterial != null
                    ? definition.VisualMaterial
                    : greenBoyDefaultMaterial;
                if (material != null) greenBoyRenderer.sharedMaterial = material;
            }
        }

        private string ResolveFallbackVisualKey()
        {
            CharacterDefinition fallback = catalog != null ? catalog.FindById(FallbackCharacterId) : null;
            return fallback != null ? fallback.VisualKey : null;
        }
    }
}
