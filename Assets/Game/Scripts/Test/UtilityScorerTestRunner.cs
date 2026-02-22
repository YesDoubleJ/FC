using UnityEngine;
using Game.Scripts.AI.DecisionMaking;
using Game.Scripts.Data;

namespace Game.Scripts.Test
{
    /// <summary>
    /// Simple manual unit test runner for UtilityScorer logic.
    /// Attach to any GameObject in a Test Scene to run tests on Start.
    /// </summary>
    [AddComponentMenu("Testing/UtilityScorerTestRunner")]
    public class UtilityScorerTestRunner : MonoBehaviour
    {
        private UtilityScorer scorer;
        private GameObject mockAgentGO;
        private PlayerStats mockStats;

        void Start()
        {
            Debug.Log("<b>[Unit Tests] Starting UtilityScorer Tests...</b>");
            
            var config = ScriptableObject.CreateInstance<MatchEngineConfig>();
            scorer = new UtilityScorer(config);
            
            SetupMocks();

            RunTest_ShootScore_Close();
            RunTest_ShootScore_Far();
            RunTest_ShootScore_Angle();
            
            // Cleanup
            TeardownMocks();
            
            Debug.Log("<b>[Unit Tests] Finished.</b>");
        }

        void SetupMocks()
        {
            mockAgentGO = new GameObject("MockAgent_Test");
            mockStats = mockAgentGO.AddComponent<PlayerStats>();

            // [FIX] 스탯을 명시적으로 초기화: 테스트는 능력치 99인 선수 기준으로 검증
            // 슈팅 스탯 99 → pStat = 0.4 + (99/100)*0.6 ≈ 0.994
            // 근거리 중앙 슈팅: pDistance=1.0, pAngle=1.0, pStat≈0.994 → Score≈0.994 > 0.8 (PASS)
            mockStats.shooting = 99f;
            mockStats.passing = 99f;
            mockStats.dribbling = 99f;
        }

        void TeardownMocks()
        {
            if (mockAgentGO != null) Destroy(mockAgentGO);
        }

        private void RunTest_ShootScore_Close()
        {
            // Goal at roughly Z=52.5 (Standard field half-length)
            Vector3 goalPos = new Vector3(0, 0, 52.5f);
            Vector3 shotPos = new Vector3(0, 0, 42.5f); // 10m away
            
            float score = scorer.CalculateShootScore(shotPos, mockStats, goalPos);
            
            // Expect High Score (Sweet Spot is < 22m, central position, high stat)
            // Expected: pDistance=1.0, pAngle=1.0, pStat≈0.994 → ~0.994
            Assert("ShootScore_Close", score > 0.8f, $"Expected > 0.8, Got {score:F2}");
        }

        private void RunTest_ShootScore_Far()
        {
            Vector3 goalPos = new Vector3(0, 0, 52.5f);
            Vector3 shotPos = new Vector3(0, 0, 0f); // ~50m away (> MaxShootRange 35m)
            
            float score = scorer.CalculateShootScore(shotPos, mockStats, goalPos);
            
            // Expect Low Score (Long shot penalty: pDistance=0.05)
            Assert("ShootScore_Far", score < 0.2f, $"Expected < 0.2, Got {score:F2}");
        }

         private void RunTest_ShootScore_Angle()
        {
            Vector3 goalPos = new Vector3(0, 0, 52.5f);
            Vector3 centerPos = new Vector3(0, 0, 30f); // Central
            Vector3 widePos = new Vector3(30, 0, 30f);  // Wide flank
            
            float scoreCenter = scorer.CalculateShootScore(centerPos, mockStats, goalPos);
            float scoreWide = scorer.CalculateShootScore(widePos, mockStats, goalPos);
            
            // Expect Center to be better than Wide
            Assert("ShootScore_Angle", scoreCenter > scoreWide, $"Expected Center ({scoreCenter:F2}) > Wide ({scoreWide:F2})");
        }

        private void Assert(string testName, bool condition, string message)
        {
            if (condition)
            {
                Debug.Log($"<color=green>[PASS]</color> {testName}");
            }
            else
            {
                Debug.LogError($"<color=red>[FAIL]</color> {testName}: {message}");
            }
        }
    }
}
