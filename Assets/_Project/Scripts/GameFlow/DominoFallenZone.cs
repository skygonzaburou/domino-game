using System.Collections;
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
        [Tooltip("Step 14.1.1: Trigger内でまだFallenAngleThreshold未満のDominoがTriggerの外へ出てしまった場合、この秒数だけ引き続き傾きを監視し、その間にFallenAngleThresholdを超えたら「このZone内で倒れた」ものとして扱う。Player接触Impulse等で勢いよく倒れ始めたDominoがOnTriggerStayの監視対象から外れる(=判定漏れでこのZoneのStart/Checkpointが永遠に成立しない)Race Conditionの救済用")]
        [SerializeField] private float lateFallGraceDuration = 0.6f;

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

            // Step 14.1.1 Root Cause: a Domino already mid-topple (e.g. from the Player Contact
            // Assist impulse) can physically slide/roll out of this trigger before its tilt
            // actually crosses fallenAngleThreshold - confirmed via Runtime, the tracked Start
            // Domino exiting StartArea at tilt=42.8 (well under 60) while still moving fast
            // enough to keep tipping. CheckDomino() is only ever invoked from OnTriggerEnter/
            // OnTriggerStay, so once a Domino leaves, nothing here watches it finish falling -
            // this Zone (and therefore _startTriggered/_expectedZoneIndex) never advances, even
            // though the Chain Reaction it kicked off continues completely normally afterward
            // and goes on to legitimately fell a later Zone - which that Zone correctly reports
            // as a Wrong Route Fail, since as far as GameFlowManager's bookkeeping is concerned
            // this Zone was still never actually satisfied. A short bounded grace window keeps
            // watching a still-unsatisfied, not-yet-fallen, not-excluded Domino for a moment
            // after it leaves, so this Zone still gets credited if it was genuinely finishing
            // the same topple, without weakening Wrong Route Detection itself (a Domino that
            // settles back upright, or never reaches fallenAngleThreshold within the grace
            // window, is correctly never credited - same as today).
            if (!IsSatisfied && !_excludedDominoes.Contains(other.transform)
                && GameFlowManager.Instance != null && GameFlowManager.Instance.CurrentPhase == GamePhase.RUNNING
                && !IsFallen(other.transform))
            {
                StartCoroutine(WatchForLateFall(other.transform));
            }
        }

        // See OnTriggerExit above for why this exists. Bounded by lateFallGraceDuration - a
        // Domino that genuinely stopped/settled back upright outside the Zone is simply never
        // credited, exactly as before this Step.
        private IEnumerator WatchForLateFall(Transform dominoTransform)
        {
            float elapsed = 0f;
            while (elapsed < lateFallGraceDuration)
            {
                if (dominoTransform == null) yield break;
                if (IsSatisfied) yield break;
                if (_dominoesInside.Contains(dominoTransform)) yield break; // back on OnTriggerStay's own watch

                if (IsFallen(dominoTransform))
                {
                    IsSatisfied = true;
                    GameFlowManager.Instance.OnZoneFallen(this);
                    yield break;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }
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
