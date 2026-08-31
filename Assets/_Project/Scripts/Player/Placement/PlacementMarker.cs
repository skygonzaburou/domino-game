using UnityEngine;

namespace Domino.Player
{
    public enum MarkerState
    {
        Preview,
        Locked
    }

    public class PlacementMarker : MonoBehaviour
    {
        [Header("Size (Step 7.8: rectangular Domino footprint)")]
        [Tooltip("Local X。Dominoの幅(0.35)に少し余裕を持たせた値")]
        [SerializeField] private float width = 0.45f;
        [Tooltip("Local Z。Dominoの奥行き(0.15)に少し余裕を持たせた値。Chain方向に沿う")]
        [SerializeField] private float depth = 0.25f;
        [SerializeField] private float groundOffset = 0.02f;

        [Header("Opacity")]
        [SerializeField, Range(0f, 1f)] private float previewOpacity = 0.2f;
        [SerializeField, Range(0f, 1f)] private float lockedOpacity = 0.45f;

        public float GroundOffset => groundOffset;

        private Material _material;
        private MarkerState _state = MarkerState.Preview;

        private void Awake()
        {
            Vector3 scale = transform.localScale;
            scale.x = width;
            scale.z = depth;
            transform.localScale = scale;

            var meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                _material = meshRenderer.material;
            }

            ApplyOpacity();
        }

        public void SetState(MarkerState state)
        {
            if (_state == state) return;
            _state = state;
            ApplyOpacity();
        }

        private void ApplyOpacity()
        {
            if (_material == null) return;
            float opacity = _state == MarkerState.Locked ? lockedOpacity : previewOpacity;
            Color c = _material.color;
            c.a = opacity;
            _material.color = c;
        }
    }
}
