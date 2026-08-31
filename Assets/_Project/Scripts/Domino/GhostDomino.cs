using UnityEngine;

namespace Domino.DominoSystem
{
    public class GhostDomino : MonoBehaviour
    {
        [SerializeField] private float maxTiltAngle = 25f;
        [SerializeField] private float periodSeconds = 1.5f;
        [SerializeField] private Vector3 tiltAxis = Vector3.right;

        private float _timer;
        private Quaternion _baseRotation;

        public void ResetWobble()
        {
            _timer = 0f;
            _baseRotation = transform.rotation;
        }

        // Step 7.8: lets PlacementController drive Yaw (A/D) live during Placement Mode
        // without touching the Wobble timer/phase, so free rotation and the ongoing tilt
        // animation compose cleanly - Update() below re-applies the tilt on top of whatever
        // base was set most recently, around the Domino's own (now Yaw-rotated) local axis.
        public void SetBaseRotation(Quaternion baseRotation)
        {
            _baseRotation = baseRotation;
        }

        public Quaternion CurrentRotation => transform.rotation;

        private void Update()
        {
            _timer += Time.deltaTime;
            float phase = periodSeconds > 0f ? (_timer / periodSeconds) * Mathf.PI * 2f : 0f;
            float angle = Mathf.Sin(phase) * maxTiltAngle;
            transform.rotation = _baseRotation * Quaternion.AngleAxis(angle, tiltAxis);
        }
    }
}
