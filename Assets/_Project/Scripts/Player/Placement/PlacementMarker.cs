using UnityEngine;

namespace Domino.Player
{
    public class PlacementMarker : MonoBehaviour
    {
        [SerializeField] private float size = 0.6f;
        [SerializeField, Range(0f, 1f)] private float opacity = 0.35f;
        [SerializeField] private float groundOffset = 0.02f;

        public float GroundOffset => groundOffset;

        private void Awake()
        {
            Vector3 scale = transform.localScale;
            scale.x = size;
            scale.z = size;
            transform.localScale = scale;

            var meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                Material mat = meshRenderer.material;
                Color c = mat.color;
                c.a = opacity;
                mat.color = c;
            }
        }
    }
}
