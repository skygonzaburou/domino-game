using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Domino.GameFlow
{
    [RequireComponent(typeof(Collider))]
    public abstract class DominoFallenZone : MonoBehaviour
    {
        [Header("Domino Fallen Detection")]
        [Tooltip("この角度(度)以上傾いたDominoを「倒れた」と判定する")]
        [SerializeField] private float fallenAngleThreshold = 60f;
        [Tooltip("RUNNING開始時点でこの角度(度)以上傾いているDominoは、BUILDING中の配置事故(Overlap Spawnによる弾き飛ばし等)で生じた不安定な状態とみなし、fallenAngleThreshold未満でも以後このZoneを満たせないよう除外する(Wrong Route誤判定防止)")]
        [SerializeField] private float preExistingTiltExclusionThreshold = 10f;

        public bool IsSatisfied { get; private set; }

        private readonly HashSet<Transform> _dominoesInside = new HashSet<Transform>();
        private readonly HashSet<Transform> _excludedDominoes = new HashSet<Transform>();

        // Called by GameFlowManager when RUNNING begins. Dominoes already meaningfully
        // tilted and already inside at this moment are excluded for the rest of this run,
        // so pre-existing BUILDING-time accidents can never satisfy this zone. Uses
        // preExistingTiltExclusionThreshold (not the higher fallenAngleThreshold) because a
        // Domino displaced-but-not-yet-fallen by an Overlap Spawn explosion sits below
        // fallenAngleThreshold and so escapes BUILDING's own fallen-Domino cleanup, only to
        // topple past fallenAngleThreshold later during RUNNING from an unrelated nudge -
        // at which point it would otherwise satisfy (or Wrong-Route-fail) this Zone despite
        // never having been part of this run's actual Domino Chain.
        public void OnRunStart()
        {
            IsSatisfied = false;
            _excludedDominoes.Clear();

            // A Domino destroyed while inside this trigger (e.g. BUILDING-time Cleanup
            // despawn) does not reliably raise OnTriggerExit, so _dominoesInside can hold
            // stale/destroyed Transform references. Purge them before touching any entry.
            _dominoesInside.RemoveWhere(t => t == null);

            foreach (var t in _dominoesInside)
            {
                if (IsPreExistingTilted(t)) _excludedDominoes.Add(t);
            }
        }

        // Ground-truth count of currently Standing (non-fallen) "Domino"-tagged objects
        // physically inside this Zone's trigger right now (Step 8.5, used by StartAreaZone for
        // the READY condition). Independent of the RUNNING-only IsSatisfied/_excludedDominoes
        // bookkeeping above - just reuses the same _dominoesInside trigger-tracked set, pruning
        // stale (destroyed) references opportunistically the same way OnRunStart() does. Bounded
        // by how many Dominoes are actually inside this Zone, never a Scene-wide search.
        public int CountStandingInside()
        {
            _dominoesInside.RemoveWhere(t => t == null);

            int count = 0;
            foreach (var t in _dominoesInside)
            {
                if (!IsFallen(t)) count++;
            }
            return count;
        }

        protected virtual void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Domino")) return;
            _dominoesInside.Add(other.transform);
            CheckDomino(other.transform);
        }

        protected virtual void OnTriggerStay(Collider other)
        {
            if (!other.CompareTag("Domino")) return;
            CheckDomino(other.transform);
        }

        protected virtual void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Domino")) return;
            _dominoesInside.Remove(other.transform);
        }

        private void CheckDomino(Transform dominoTransform)
        {
            // NetworkManager.Singleton is null for a brief window while Play Mode is
            // tearing down (colliders still fire Enter/Exit as objects get destroyed).
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
            if (IsSatisfied) return;
            if (GameFlowManager.Instance == null || GameFlowManager.Instance.CurrentPhase != GamePhase.RUNNING) return;
            if (_excludedDominoes.Contains(dominoTransform)) return;

            if (IsFallen(dominoTransform))
            {
                IsSatisfied = true;
                GameFlowManager.Instance.OnZoneFallen(this);
            }
        }

        private bool IsFallen(Transform t)
        {
            if (t == null) return false;
            return Vector3.Angle(t.up, Vector3.up) >= fallenAngleThreshold;
        }

        private bool IsPreExistingTilted(Transform t)
        {
            if (t == null) return false;
            return Vector3.Angle(t.up, Vector3.up) >= preExistingTiltExclusionThreshold;
        }
    }
}
