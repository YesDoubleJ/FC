using UnityEngine;
using System.Collections.Generic;
using Game.Scripts.Managers;

namespace Game.Scripts.Tactics
{
    /// <summary>
    /// 포메이션 매니저 — 지침서 §6.2 동적 포메이션 테더링 추가
    /// Anchor Point: 공 위치에 따라 포메이션 전체 이동
    /// Tether: 선수가 Anchor에서 일정 거리 이상 벗어나면 복귀 Steering Force
    /// Line Height: Low Block(25m), Mid Block(40m), High Line(하프라인)
    /// </summary>
    public class FormationManager : MonoBehaviour
    {
        [Header("Managers")]
        [SerializeField] private HomeFormationManager homeManager;
        [SerializeField] private AwayFormationManager awayManager;

        // =========================================================
        // §6.2 테더링 설정
        // =========================================================
        [Header("Tethering — §6.2")]
        [Tooltip("선수가 앵커에서 이 거리 이상 벗어나면 복귀력 발생 (미터)")]
        public float TetherRadius = 15f;

        [Tooltip("테더 복귀력 최대 강도")]
        public float TetherForceMax = 3.0f;

        [Tooltip("테더 작동 시작 거리 (TetherRadius의 배수 초과 시 강제 복귀)")]
        public float TetherHardLimit = 2.0f;

        [Header("Line Height — §6.2")]
        [Tooltip("현재 수비 라인 높이")]
        public LineHeight CurrentLineHeight = LineHeight.MidBlock;

        private Transform ballTransform;

        private void Awake()
        {
            var ballObj = GameObject.Find("Ball"); 
            if (ballObj != null) ballTransform = ballObj.transform;

            // Auto-find managers if not assigned
            if (homeManager == null) homeManager = FindFirstObjectByType<HomeFormationManager>();
            if (awayManager == null) awayManager = FindFirstObjectByType<AwayFormationManager>();
            
            // Auto-create if missing (Safety fallback)
            if (homeManager == null) 
            {
                var go = new GameObject("HomeFormationManager");
                homeManager = go.AddComponent<HomeFormationManager>();
            }
            if (awayManager == null) 
            {
                var go = new GameObject("AwayFormationManager");
                awayManager = go.AddComponent<AwayFormationManager>();
            }
        }

        private void ForceRepositionPlayer(string name, Vector3 pos)
        {
            GameObject obj = GameObject.Find(name);
            if (obj != null)
            {
                var agent = obj.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent != null) agent.Warp(pos);
                else obj.transform.position = pos;
                
                float lookZ = (pos.z < 0) ? 1 : -1;
                obj.transform.rotation = Quaternion.LookRotation(new Vector3(0, 0, lookZ));
            }
        }

        // Default for backward compatibility (Home Team)
        public Vector3 GetAnchorPosition(FormationPosition pos)
        {
            return GetAnchorPosition(pos, Game.Scripts.Data.Team.Home);
        }

        public Vector3 GetAnchorPosition(FormationPosition pos, Game.Scripts.Data.Team team)
        {
            float ballZ = 0f;
            if (MatchManager.Instance != null && MatchManager.Instance.Ball != null)
            {
                ballZ = MatchManager.Instance.Ball.transform.position.z;
            }
            else if (ballTransform != null)
            {
                ballZ = ballTransform.position.z;
            }
            
            // Get Offset from Managers
            Vector3 offset = Vector3.zero;
            if (team == Game.Scripts.Data.Team.Home && homeManager != null) offset = homeManager.GetOffset(pos);
            else if (team == Game.Scripts.Data.Team.Away && awayManager != null) offset = awayManager.GetOffset(pos);
            
            // Get Field Dimensions
            float fieldLimit = 45f;
            float gkLimit = 49.5f;
            if (FieldManager.Instance != null)
            {
                fieldLimit = (FieldManager.Instance.Length / 2f) - 5f; // 50 - 5 = 45
                gkLimit = (FieldManager.Instance.Length / 2f) - 0.5f; // 49.5
            }
            
            // §6.2: Line Height에 따른 앵커 Z 클램프 범위 조정
            float lineHeightOffset = GetLineHeightValue();

            // Anchor Logic:
            float teamCenterZ = Mathf.Clamp(ballZ, -30f, 30f);
            
            // Since Away offsets are already mirrored (e.g. +20 for CB), we just ADD.
            float finalZ = teamCenterZ + offset.z;
            
            // Clamp
            float minZ = -fieldLimit;
            float maxZ = fieldLimit;
            
            if (pos == FormationPosition.GK)
            {
                // GK Clamping depends on team?
                if (team == Game.Scripts.Data.Team.Home) { minZ = -gkLimit; maxZ = 40f; }
                else { minZ = -40f; maxZ = gkLimit; }
            }
            else
            {
                // Field Player Clamping
                if (team == Game.Scripts.Data.Team.Home) { minZ = -fieldLimit; maxZ = 45f; }
                else { minZ = -45f; maxZ = fieldLimit; }
            }

            finalZ = Mathf.Clamp(finalZ, minZ, maxZ);
            
            return new Vector3(offset.x, 0.05f, finalZ);
        }

        // kick-off specific position
        public Vector3 GetKickoffPosition(FormationPosition pos, Game.Scripts.Data.Team team, bool isKickOffTeam)
        {
            Vector3 offset = Vector3.zero;
            if (team == Game.Scripts.Data.Team.Home && homeManager != null) offset = homeManager.GetOffset(pos);
            else if (team == Game.Scripts.Data.Team.Away && awayManager != null) offset = awayManager.GetOffset(pos);

            // KICKOFF START POSITION LOGIC
            // User Request: Don't mess up the formation settings! Just enforce Rules.

            // 1. Base Position directly from Offset (Captured from Scene)
            // Assumes offsets are defined in Field Coordinates (Home < 0, Away > 0)
            float finalX = offset.x;
            float finalZ = offset.z;

            // 2. KICKOFF TAKER EXCEPTION (Rules: 1 or 2 players at center)
            bool isStriker = (pos == FormationPosition.ST_Center || pos == FormationPosition.ST_Left || pos == FormationPosition.ST_Right);
            
            // If it is MY Kickoff, put me on the ball (Center Spot)
            if (isKickOffTeam && isStriker)
            {
                 // Central Striker takes priority for center spot
                 if (pos == FormationPosition.ST_Center)
                 {
                     float strikerZ = (team == Game.Scripts.Data.Team.Home) ? -0.9f : 0.9f; // 0.9m (Safe but close)
                     return new Vector3(0, 0.05f, strikerZ); 
                 }
                 // Fallback for side strikers if Center is missing (rare in 6v6 but safe)
                 float finalKickerZ = (team == Game.Scripts.Data.Team.Home) ? -0.9f : 0.9f;
                 // Actually, let's put ST_Left/Right just slightly back.
                 float finalKickerX = (pos == FormationPosition.ST_Left) ? -0.8f : 0.8f;
                 if (team == Game.Scripts.Data.Team.Away) finalKickerX *= -1; 
                 
                 return new Vector3(finalKickerX, 0.05f, finalKickerZ);
            }

            // 3. ENFORCE RULES (Stay in Own Half)
            if (team == Game.Scripts.Data.Team.Home)
            {
                // Home must be Z < 0
                if (finalZ > -0.5f) finalZ = -0.5f; 
            }
            else
            {
                // Away must be Z > 0
                if (finalZ < 0.5f) finalZ = 0.5f;
            }

            // 4. DEFENDING TEAM RULE: Outside Center Circle (9.15m)
            if (!isKickOffTeam)
            {
                float distToCenter = Mathf.Sqrt(finalX*finalX + finalZ*finalZ);
                if (distToCenter < 9.15f)
                {
                    // Push out radially
                    Vector3 posVec = new Vector3(finalX, 0f, finalZ);
                    posVec = posVec.normalized * 9.5f; // Push to 9.5m
                    finalX = posVec.x;
                    finalZ = posVec.z;
                }
            }
            
            return new Vector3(finalX, 0.05f, finalZ);
        }

        // Helper: Get Offside Line (Z position of the last defender of the OPPOSING team)
        public float GetOffsideLine(Game.Scripts.Data.Team attackingTeam)
        {
            float extremeZ = (attackingTeam == Game.Scripts.Data.Team.Home) ? 50f : -50f;
            
            // Find all defenders of OPPOSING team
            var agents = FindObjectsByType<Game.Scripts.AI.HybridAgentController>(FindObjectsSortMode.None);
            
            if (attackingTeam == Game.Scripts.Data.Team.Home)
            {
                List<float> defenderZs = new List<float>();
                
                foreach (var agent in agents)
                {
                    if (agent.TeamID == Game.Scripts.Data.Team.Away)
                    {
                        defenderZs.Add(agent.transform.position.z);
                    }
                }
                
                defenderZs.Sort();
                
                if (defenderZs.Count >= 2)
                {
                    return defenderZs[defenderZs.Count - 2]; 
                }
                return 50f;
            }
            else
            {
                List<float> defenderZs = new List<float>();
                foreach (var agent in agents)
                {
                    if (agent.TeamID == Game.Scripts.Data.Team.Home)
                    {
                        defenderZs.Add(agent.transform.position.z);
                    }
                }
                
                defenderZs.Sort();
                
                if (defenderZs.Count >= 2)
                {
                    return defenderZs[1];
                }
                return -50f;
            }
        }

        // =========================================================
        // §6.2 테더링 시스템
        // =========================================================

        /// <summary>
        /// 선수가 포메이션 앵커에서 벗어난 정도에 따른 복귀 Steering Force를 반환합니다.
        /// AI 이동 로직에서 이 값을 현재 이동 방향에 더해서 사용합니다.
        /// </summary>
        /// <param name="playerPos">현재 선수 위치</param>
        /// <param name="formationPos">해당 선수의 포메이션 포지션</param>
        /// <param name="team">소속 팀</param>
        /// <returns>복귀 방향 * 강도 (Vector3). 테더 범위 내이면 Vector3.zero</returns>
        public Vector3 GetTetherForce(Vector3 playerPos, FormationPosition formationPos, Game.Scripts.Data.Team team)
        {
            Vector3 anchor = GetAnchorPosition(formationPos, team);
            Vector3 toAnchor = anchor - playerPos;
            toAnchor.y = 0f; // 수평면만

            float distance = toAnchor.magnitude;

            // 테더 범위 내: 복귀력 없음
            if (distance <= TetherRadius)
                return Vector3.zero;

            // 테더 범위 초과: 거리에 비례한 복귀력
            float overExtension = (distance - TetherRadius) / (TetherRadius * (TetherHardLimit - 1f));
            float forceRatio = Mathf.Clamp01(overExtension);

            // Ease-in 곡선: 부드러운 시작 → 강한 끌어당김
            float force = TetherForceMax * (forceRatio * forceRatio);

            return toAnchor.normalized * force;
        }

        /// <summary>
        /// 현재 Line Height 설정에 따른 Z축 오프셋 반환.
        /// Low Block: 수비수가 25m 라인까지만 전진
        /// Mid Block: 40m 라인
        /// High Line: 하프라인(0m)까지 전진
        /// </summary>
        public float GetLineHeightValue()
        {
            switch (CurrentLineHeight)
            {
                case LineHeight.LowBlock:  return 25f;
                case LineHeight.MidBlock:  return 40f;
                case LineHeight.HighLine:  return 50f;
                default: return 40f;
            }
        }

        /// <summary>
        /// 수비 라인 높이를 동적으로 변경합니다.
        /// 예: 리드 시 Low Block, 추격 시 High Line.
        /// </summary>
        public void SetLineHeight(LineHeight height)
        {
            CurrentLineHeight = height;
        }
    }

    /// <summary>수비 라인 높이 — §6.2</summary>
    public enum LineHeight
    {
        /// <summary>낮은 수비: 25m 라인. 페널티 박스 근처 밀집 수비</summary>
        LowBlock,

        /// <summary>중간 수비: 40m 라인. 일반적 수비</summary>
        MidBlock,

        /// <summary>높은 수비: 하프라인. 오프사이드 트랩, 전방 압박</summary>
        HighLine
    }
}
