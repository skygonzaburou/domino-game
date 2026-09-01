using TMPro;
using UnityEngine;
using Domino.GameFlow;

namespace Domino.UI
{
    // Prototype Gameplay HUD (Step 8B). Deliberately a plain MonoBehaviour, not a NetworkObject:
    // every Client renders this purely from the already-networked GameFlowManager/ChallengeTimer
    // state it reads locally, so nothing here needs its own network traffic. Not wired to
    // NetworkBootstrap/MPPM/Host-start in any way - this only ever reacts to GamePhase, so it
    // will work unmodified once Step 9 drops it into a Gameplay Scene reached via a Lobby.
    public class GameplayHUD : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private GameObject buildingPanel;
        [SerializeField] private GameObject readyPanel;
        [SerializeField] private GameObject runningTimerPanel;
        [SerializeField] private GameObject failedPanel;
        [SerializeField] private GameObject resultPanel;

        [Header("Texts")]
        [SerializeField] private TextMeshProUGUI runningTimerText;
        [SerializeField] private TextMeshProUGUI resultTimeText;

        private void Update()
        {
            GamePhase? phase = GameFlowManager.Instance != null ? GameFlowManager.Instance.CurrentPhase : (GamePhase?)null;

            SetActive(buildingPanel, phase == GamePhase.BUILDING);
            SetActive(readyPanel, phase == GamePhase.READY);
            SetActive(failedPanel, phase == GamePhase.FAILED);
            SetActive(resultPanel, phase == GamePhase.SUCCESS);

            bool showRunningTimer = phase == GamePhase.RUNNING;
            SetActive(runningTimerPanel, showRunningTimer);
            if (showRunningTimer && ChallengeTimer.Instance != null && runningTimerText != null)
            {
                runningTimerText.text = FormatTime(ChallengeTimer.Instance.GetElapsedSeconds());
            }

            if (phase == GamePhase.SUCCESS && ChallengeTimer.Instance != null && resultTimeText != null)
            {
                resultTimeText.text = FormatTime(ChallengeTimer.Instance.FinalElapsedSeconds);
            }
        }

        private static void SetActive(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active) go.SetActive(active);
        }

        // MM:SS.xx - prototype format, matches the spec's example values exactly (00:05.27 etc).
        private static string FormatTime(double seconds)
        {
            if (seconds < 0.0) seconds = 0.0;
            int minutes = (int)(seconds / 60.0);
            double remainder = seconds - minutes * 60.0;
            return $"{minutes:00}:{remainder:00.00}";
        }
    }
}
