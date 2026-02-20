using UnityEngine;
using System.Collections.Generic;
using Game.Scripts.Data;

namespace Game.Scripts.AI.Tactics
{
    /// <summary>
    /// 마킹 시스템 — 지침서 §6.3
    /// 대인 마크(Man Marking), 지역 방어(Zonal Marking), 하이브리드 모드를 지원합니다.
    /// 
    /// - Man: 특정 OpponentID를 1:1 추적
    /// - Zonal: FormationAnchor 주변 Zone 방어, 적 진입 시 추적/퇴출 시 인계
    /// - Hybrid: 기본 지역 방어 + 세트피스/Box 내 대인 마크 전환
    /// </summary>
    public class MarkingSystem : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("기본 마킹 모드")]
        public MarkingMode DefaultMode = MarkingMode.Hybrid;

        [Tooltip("지역 방어 Zone 반경 (미터)")]
        public float ZoneRadius = 10f;

        [Tooltip("대인 마크 최대 추적 거리 (미터). 초과 시 인계")]
        public float ManMarkMaxDistance = 25f;

        [Tooltip("페널티 박스 내에서는 항상 대인 마크로 전환")]
        public bool ForceManMarkInBox = true;

        // =========================================================
        // 마킹 배정
        // =========================================================
        private Dictionary<Game.Scripts.AI.HybridAgentController, MarkingAssignment> _assignments
            = new Dictionary<Game.Scripts.AI.HybridAgentController, MarkingAssignment>();

        public static MarkingSystem Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        // =========================================================
        // Public API
        // =========================================================

        /// <summary>
        /// 특정 수비 선수의 마킹 대상을 가져옵니다.
        /// 반환: 마킹 대상 Transform (없으면 null → Zone 방어)
        /// </summary>
        public MarkingAssignment GetAssignment(Game.Scripts.AI.HybridAgentController defender)
        {
            if (_assignments.TryGetValue(defender, out var assignment))
                return assignment;
            return new MarkingAssignment { Mode = MarkingMode.Zonal };
        }

        /// <summary>
        /// 팀 전체 마킹 배정을 갱신합니다.
        /// 수비 전환 시 또는 주기적으로 호출.
        /// </summary>
        public void UpdateMarkingAssignments(
            List<Game.Scripts.AI.HybridAgentController> defenders,
            List<Game.Scripts.AI.HybridAgentController> attackers)
        {
            _assignments.Clear();

            foreach (var defender in defenders)
            {
                var assignment = new MarkingAssignment();
                MarkingMode activeMode = DefaultMode;

                // Hybrid: 페널티 박스 내에서는 대인 마크 강제
                if (activeMode == MarkingMode.Hybrid && ForceManMarkInBox)
                {
                    if (IsInDefensivePenaltyArea(defender))
                    {
                        activeMode = MarkingMode.ManToMan;
                    }
                }

                if (activeMode == MarkingMode.ManToMan)
                {
                    // 가장 가까운 미배정 공격자 찾기
                    var target = FindNearestUnassignedAttacker(defender, attackers);
                    if (target != null)
                    {
                        assignment.Mode = MarkingMode.ManToMan;
                        assignment.Target = target.transform;
                        assignment.TargetAgent = target;
                    }
                    else
                    {
                        assignment.Mode = MarkingMode.Zonal;
                    }
                }
                else
                {
                    assignment.Mode = MarkingMode.Zonal;
                    assignment.ZoneCenter = GetDefenderZoneCenter(defender);
                    assignment.ZoneRadius = ZoneRadius;
                }

                _assignments[defender] = assignment;
            }
        }

        /// <summary>
        /// 지역 방어 시 Zone 내 적 진입 여부 확인.
        /// 진입하면 추적 모드로 전환할 트리거.
        /// </summary>
        public Game.Scripts.AI.HybridAgentController CheckZoneIntrusion(
            Game.Scripts.AI.HybridAgentController defender,
            List<Game.Scripts.AI.HybridAgentController> attackers)
        {
            if (!_assignments.TryGetValue(defender, out var assignment)) return null;
            if (assignment.Mode != MarkingMode.Zonal) return null;

            Vector3 zoneCenter = assignment.ZoneCenter;
            float radius = assignment.ZoneRadius;

            Game.Scripts.AI.HybridAgentController closest = null;
            float closestDist = float.MaxValue;

            foreach (var attacker in attackers)
            {
                float dist = Vector3.Distance(attacker.transform.position, zoneCenter);
                if (dist < radius && dist < closestDist)
                {
                    closestDist = dist;
                    closest = attacker;
                }
            }

            return closest;
        }

        /// <summary>
        /// 대인 마크 대상이 너무 멀어지면 인계 트리거.
        /// </summary>
        public bool ShouldHandoff(Game.Scripts.AI.HybridAgentController defender)
        {
            if (!_assignments.TryGetValue(defender, out var assignment)) return false;
            if (assignment.Mode != MarkingMode.ManToMan || assignment.Target == null) return false;

            float dist = Vector3.Distance(defender.transform.position, assignment.Target.position);
            return dist > ManMarkMaxDistance;
        }

        // =========================================================
        // 내부 헬퍼
        // =========================================================

        private Game.Scripts.AI.HybridAgentController FindNearestUnassignedAttacker(
            Game.Scripts.AI.HybridAgentController defender,
            List<Game.Scripts.AI.HybridAgentController> attackers)
        {
            // 이미 배정된 공격자 목록
            HashSet<Game.Scripts.AI.HybridAgentController> assigned = new HashSet<Game.Scripts.AI.HybridAgentController>();
            foreach (var kvp in _assignments)
            {
                if (kvp.Value.TargetAgent != null)
                    assigned.Add(kvp.Value.TargetAgent);
            }

            Game.Scripts.AI.HybridAgentController nearest = null;
            float nearestDist = float.MaxValue;

            foreach (var attacker in attackers)
            {
                if (assigned.Contains(attacker)) continue;

                float dist = Vector3.Distance(defender.transform.position, attacker.transform.position);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = attacker;
                }
            }

            return nearest;
        }

        private Vector3 GetDefenderZoneCenter(Game.Scripts.AI.HybridAgentController defender)
        {
            // 기본: 현재 포메이션 앵커 포지션을 Zone 중심으로 사용
            // FormationManager를 통해 가져오는 게 이상적이나, 현재 위치도 사용 가능
            return defender.transform.position;
        }

        private bool IsInDefensivePenaltyArea(Game.Scripts.AI.HybridAgentController defender)
        {
            if (Managers.FieldManager.Instance == null) return false;
            return Managers.FieldManager.Instance.IsInsidePenaltyArea(
                defender.transform.position, defender.TeamID == Team.Home);
        }
    }

    // =========================================================
    // 데이터 구조
    // =========================================================

    /// <summary>마킹 모드</summary>
    public enum MarkingMode
    {
        /// <summary>대인 마크: 특정 공격수를 1:1 추적</summary>
        ManToMan,

        /// <summary>지역 방어: 할당된 Zone 내 적을 견제</summary>
        Zonal,

        /// <summary>하이브리드: 기본 지역 + Box 내 대인 전환</summary>
        Hybrid
    }

    /// <summary>마킹 배정 데이터</summary>
    public struct MarkingAssignment
    {
        public MarkingMode Mode;

        /// <summary>대인 마크 대상 Transform</summary>
        public Transform Target;

        /// <summary>대인 마크 대상 Agent (null 체크용)</summary>
        public Game.Scripts.AI.HybridAgentController TargetAgent;

        /// <summary>지역 방어 Zone 중심</summary>
        public Vector3 ZoneCenter;

        /// <summary>지역 방어 Zone 반경</summary>
        public float ZoneRadius;
    }
}
