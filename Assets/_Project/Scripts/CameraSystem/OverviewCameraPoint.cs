using UnityEngine;

namespace Domino.CameraSystem
{
    // Stage-authored marker for where the RUNNING/SUCCESS/FAILED Overview Camera looks from.
    // Position/Rotation come straight from this Transform, so a Stage Author sets up a new
    // Stage's Overview purely by moving/rotating this GameObject - no script changes needed.
    public class OverviewCameraPoint : MonoBehaviour
    {
        public static OverviewCameraPoint Instance { get; private set; }

        [SerializeField] private float fieldOfView = 60f;

        public float FieldOfView => fieldOfView;

        private void Awake()
        {
            Instance = this;
        }
    }
}
