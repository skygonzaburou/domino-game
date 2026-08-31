using UnityEngine;

namespace Domino.Player
{
    public class PlayerStats : MonoBehaviour
    {
        [Header("Movement")]
        public float moveSpeed = 5f;
        public float dashMultiplier = 1.8f;
        public float jumpForce = 6f;
        public float rotationSpeed = 720f;

        [Header("Camera")]
        public float mouseSensitivity = 2f;
    }
}
