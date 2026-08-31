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
