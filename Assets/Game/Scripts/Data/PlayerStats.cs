using UnityEngine;
using System.Collections.Generic;

namespace Game.Scripts.Data
{
    /// <summary>
    /// 선수의 기본 능력치와 런타임 상태를 관리합니다.
    /// 지침서 §4.1: 실시간 능력치 = BaseStat × StateIndex × MoraleMod
    /// 지침서 §4.3: 히든 특성 (Glass Body, Iron Man, Consistency)
    /// </summary>
    public class PlayerStats : MonoBehaviour
    {
        // =========================================================
        // 기본 능력치 (Base Stats) — 0 ~ 100
        // =========================================================
        [Header("Offensive")]
        [Range(0, 100)] public float shooting = 50f;
        [Range(0, 100)] public float passing = 50f;
        [Range(0, 100)] public float dribbling = 50f;

        [Header("Physical & Mental")]
        [Range(0, 100)] public float speed = 50f;
        [Range(0, 100)] public float physical = 50f;    // Strength/Balance
        [Range(0, 100)] public float technique = 50f;   // Ball control
        [Range(0, 100)] public float composure = 50f;   // Mental stability under pressure

        [Header("Mental — §5.3 의사결정 주기에 사용")]
        [Tooltip("판단력/지능. 높을수록 AI 의사결정 주기가 빨라짐 (ThinkTick 감소)")]
        [Range(0, 100)] public float mental = 50f;

        [Header("Defensive")]
        [Tooltip("수비 위치 선정 및 태클 능력")]
        [Range(0, 100)] public float defending = 50f;

        [Tooltip("골키퍼 전용 능력치")]
        [Range(0, 100)] public float goalkeeping = 10f;

        // =========================================================
        // 런타임 상태 (Runtime State) — §4.1
        // =========================================================
        [Header("Runtime State (변동)")]
        [Tooltip("체력. 0이면 능력치 50% 감소")]
        [Range(0, 100)] public float Stamina = 100f;

        [Tooltip("호흡/숨. 스프린트 후 급격히 감소, 회복 속도는 체력에 비례")]
        [Range(0, 100)] public float Breath = 100f;

        [Tooltip("사기. 득점/실점 등 이벤트에 따라 변동. -10~+10 범위")]
        [Range(-10f, 10f)] public float Morale = 0f;

        [Tooltip("경기 시작 시 부여되는 컨디션 (0.8~1.2). Consistency 특성이 편차 축소")]
        [Range(0.8f, 1.2f)] public float Condition = 1.0f;

        // =========================================================
        // 히든 특성 (Hidden Traits) — §4.3
        // =========================================================
        [Header("Hidden Traits — §4.3")]
        [Tooltip("선수에게 부여된 히든 특성 목록")]
        public List<PlayerTrait> Traits = new List<PlayerTrait>();

        // =========================================================
        // 체력/호흡 소모 설정
        // =========================================================
        [Header("Stamina Drain Config")]
        [Tooltip("초당 기본 체력 소모량")]
        public float BaseStaminaDrainPerSecond = 0.15f;

        [Tooltip("스프린트 시 체력 소모 배율")]
        public float SprintStaminaMultiplier = 3.0f;

        [Tooltip("호흡 회복 속도 (초당)")]
        public float BreathRecoveryPerSecond = 5.0f;

        // =========================================================
        // 기본 스탯 접근 (Backward Compatible)
        // =========================================================

        /// <summary>
        /// 원시 기본 능력치를 반환합니다 (런타임 보정 없음).
        /// 기존 코드 호환용. 새 코드에서는 GetEffectiveStat() 사용 권장.
        /// </summary>
        public float GetStat(StatType type)
        {
            switch (type)
            {
                case StatType.Shooting:   return shooting;
                case StatType.Passing:    return passing;
                case StatType.Dribbling:  return dribbling;
                case StatType.Speed:      return speed;
                case StatType.Physical:   return physical;
                case StatType.Technique:  return technique;
                case StatType.Composure:  return composure;
                case StatType.Mental:     return mental;
                case StatType.Defending:  return defending;
                case StatType.Goalkeeping: return goalkeeping;
                default: return 50f;
            }
        }

        // =========================================================
        // 실시간 능력치 (Effective Stats) — §4.1
        // =========================================================

        /// <summary>
        /// 실시간 보정된 능력치를 반환합니다.
        /// 공식: BaseStat × StateIndex × MoraleMod × Condition
        /// StateIndex = 0.5 + 0.5 × (Stamina/100) × (Breath/100)
        /// MoraleMod  = 1.0 + Morale/100 (범위: 0.9 ~ 1.1)
        /// </summary>
        public float GetEffectiveStat(StatType type)
        {
            float baseStat = GetStat(type);
            float effective = baseStat * GetStateIndex() * GetMoraleMod() * Condition;
            return Mathf.Clamp(effective, 0f, 100f);
        }

        /// <summary>
        /// 상태 지수: 체력과 호흡이 바닥나면 능력치가 50%로 급감.
        /// StateIndex = 0.5 + 0.5 × (Stamina/100) × (Breath/100)
        /// </summary>
        public float GetStateIndex()
        {
            float staminaNorm = Mathf.Clamp01(Stamina / 100f);
            float breathNorm = Mathf.Clamp01(Breath / 100f);
            return 0.5f + 0.5f * staminaNorm * breathNorm;
        }

        /// <summary>
        /// 사기 보정: 득점/실점 직후 ±10% 범위의 모멘텀.
        /// MoraleMod = 1.0 + Morale/100, 클램프 0.9~1.1
        /// </summary>
        public float GetMoraleMod()
        {
            return Mathf.Clamp(1.0f + Morale / 100f, 0.9f, 1.1f);
        }

        // =========================================================
        // 런타임 업데이트
        // =========================================================

        /// <summary>
        /// 매 프레임 호출. 체력 소모, 호흡 회복 등을 처리합니다.
        /// </summary>
        public void UpdateRuntime(float deltaTime, bool isSprinting)
        {
            // Iron Man 특성: 체력 소모 0.8배
            float drainMod = HasTrait(PlayerTrait.IronMan) ? 0.8f : 1.0f;

            // 체력 소모
            float drain = BaseStaminaDrainPerSecond * drainMod;
            if (isSprinting)
                drain *= SprintStaminaMultiplier;
            Stamina = Mathf.Clamp(Stamina - drain * deltaTime, 0f, 100f);

            // 호흡: 스프린트 시 감소, 아닐 때 회복
            if (isSprinting)
            {
                Breath = Mathf.Clamp(Breath - drain * SprintStaminaMultiplier * deltaTime, 0f, 100f);
            }
            else
            {
                float recovery = BreathRecoveryPerSecond * (Stamina / 100f); // 체력 비례 회복
                Breath = Mathf.Clamp(Breath + recovery * deltaTime, 0f, 100f);
            }
        }

        /// <summary>
        /// 사기 변동 이벤트 (득점, 실점 등)
        /// </summary>
        public void ApplyMoraleChange(float delta)
        {
            Morale = Mathf.Clamp(Morale + delta, -10f, 10f);
        }

        // =========================================================
        // 히든 특성 헬퍼 — §4.3
        // =========================================================

        /// <summary>특정 특성을 보유하고 있는지 확인</summary>
        public bool HasTrait(PlayerTrait trait)
        {
            return Traits.Contains(trait);
        }

        /// <summary>
        /// 경기 시작 시 컨디션 초기화.
        /// Consistency 특성이 있으면 편차가 축소됨.
        /// </summary>
        public void InitializeMatchCondition()
        {
            float deviation = HasTrait(PlayerTrait.Consistency) ? 0.05f : 0.15f;
            Condition = Random.Range(1f - deviation, 1f + deviation);

            Stamina = 100f;
            Breath = 100f;
            Morale = 0f;
        }

        /// <summary>
        /// Glass Body: 부상 확률 체크 (Disadvantage Roll — 2회 중 높은 값 사용).
        /// 반환: 0~1 범위의 부상 위험도 (높을수록 부상 위험)
        /// </summary>
        public float RollInjuryRisk(float impactForce)
        {
            float baseRisk = Mathf.Clamp01(impactForce / 100f);

            if (HasTrait(PlayerTrait.GlassBody))
            {
                // Disadvantage Roll: 2회 판정 중 높은(나쁜) 값 사용
                float roll1 = Random.Range(0f, 1f);
                float roll2 = Random.Range(0f, 1f);
                return baseRisk * Mathf.Max(roll1, roll2);
            }

            if (HasTrait(PlayerTrait.IronMan))
            {
                // 부상 저항 극대화
                return baseRisk * 0.3f * Random.Range(0f, 1f);
            }

            return baseRisk * Random.Range(0f, 1f);
        }
    }

    // =========================================================
    // 스탯 타입 열거형
    // =========================================================
    public enum StatType
    {
        Shooting,
        Passing,
        Dribbling,
        Speed,
        Physical,
        Technique,
        Composure,
        Mental,
        Defending,
        Goalkeeping
    }

    // =========================================================
    // 히든 특성 열거형 — §4.3
    // =========================================================
    public enum PlayerTrait
    {
        /// <summary>유리몸: 충돌 시 부상 판정 2회 수행 (Disadvantage Roll)</summary>
        GlassBody,

        /// <summary>철인: 체력 소모 0.8배, 부상 저항 극대화</summary>
        IronMan,

        /// <summary>일관성: 매 경기 Condition 난수 편차 축소</summary>
        Consistency,

        /// <summary>중거리 슛: 슈팅 시 거리 임계값 확장</summary>
        LongShotSpecialist,

        /// <summary>플레이메이커: 패스 성공률 보너스</summary>
        Playmaker,

        /// <summary>스피드스터: 스프린트 체력 소모 감소</summary>
        Speedster
    }
}

