using System.Collections.Generic;
using UnityEngine;

namespace Domino.CameraSystem
{
    // Stage-authored marker for where the RUNNING/SUCCESS/FAILED Overview Camera looks from.
    // Position/Rotation come straight from this Transform, so a Stage Author sets up a new
    // Stage's Overview purely by placing/rotating GameObjects with this Component - no script
    // changes needed.
    //
    // Step 14.4: a Stage may now author more than one of these (e.g. under a shared
    // "CameraPoints" parent, sibling order deciding cycle order) - this class keeps a static,
    // Hierarchy-sibling-ordered List of every instance currently in the Scene instead of a
    // single Instance singleton, so ThirdPersonCamera can cycle between them during RUNNING
    // (see that file). A Stage with only one Point (every Stage as of this Step) behaves
    // exactly as before - Points is simply a 1-element List.
    public class OverviewCameraPoint : MonoBehaviour
    {
        private static readonly List<OverviewCameraPoint> _points = new List<OverviewCameraPoint>();

        public static IReadOnlyList<OverviewCameraPoint> Points => _points;

        [SerializeField] private float fieldOfView = 60f;

        public float FieldOfView => fieldOfView;

        private void Awake()
        {
            _points.Add(this);
            _points.Sort((a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
        }

        private void OnDestroy()
        {
            _points.Remove(this);
        }
    }
}
