using UnityEngine;
using Game.Scripts.Data;

namespace Game.Scripts.Physics
{
    /// <summary>
    /// 물리적 경합 시스템 — 지침서 §3.3
    /// 태클, 몸싸움, 어깨 충돌 등의 1:1 경합을 Sigmoid 확률 모델로 판정합니다.
    /// 
    /// P(win) = σ(w1·Strength + w2·Balance + w3·Speed - w4·OpponentPhysical)
    /// 결과: AddForce(밀려남), 볼 뺏기, 파울 판정 등
    /// </summary>
    public static class PhysicalDuelSystem
    {
        // =========================================================
        // 가중치 상수
        // =========================================================
        private const float W_STRENGTH = 0.035f;    // 피지컬 스탯 가중치
        private const float W_BALANCE  = 0.025f;    // 균형감각 (Composure로 대체)
        private const float W_SPEED    = 0.015f;    // 속도 우위 (달려오는 힘)
        private const float W_OPPONENT = 0.035f;    // 상대방 피지컬 감산

        private const float FOUL_THRESHOLD = 0.3f;  // 승률이 이 미만이면 파울 리스크 증가
        private const float PUSH_FORCE = 5.0f;      // 기본 밀어내기 힘 (N)

        // =========================================================
        // 공식 API
        // =========================================================

        /// <summary>
        /// 1:1 경합을 평가합니다.
        /// </summary>
        /// <param name="attacker">공을 빼앗으려는 선수 (태클러/프레서)</param>
        /// <param name="defender">공을 보유한 선수 (드리블러)</param>
        /// <param name="attackerSpeed">태클러의 현재 이동 속도 (m/s)</param>
        /// <param name="defenderSpeed">드리블러의 현재 이동 속도 (m/s)</param>
        /// <returns>경합 결과</returns>
        public static DuelResult EvaluateDuel(
            PlayerStats attacker,
            PlayerStats defender,
            float attackerSpeed = 0f,
            float defenderSpeed = 0f)
        {
            var result = new DuelResult();

            // 1. 각 선수의 경합 점수 계산 (실시간 능력치 사용)
            float attackScore = CalculateDuelScore(attacker, attackerSpeed);
            float defendScore = CalculateDuelScore(defender, defenderSpeed);

            // 2. 차이값 → Sigmoid로 승리 확률 산출
            float delta = attackScore - defendScore;
            result.AttackerWinProbability = Sigmoid(delta);

            // 3. 확률적 판정 (주사위 굴림)
            float roll = Random.Range(0f, 1f);
            result.AttackerWins = roll < result.AttackerWinProbability;

            // 4. 파울 판정 — 승률이 낮을수록 거친 플레이 → 파울 확률 증가
            float foulRisk = CalculateFoulRisk(result.AttackerWinProbability, attacker);
            result.IsFoul = Random.Range(0f, 1f) < foulRisk;

            // 5. 밀려나는 힘 계산
            result.PushForceMagnitude = CalculatePushForce(attackScore, defendScore);

            // 6. 부상 체크 (Glass Body 등 히든 특성 반영)
            float impactForce = Mathf.Abs(attackScore - defendScore) * 10f;
            result.AttackerInjuryRisk = attacker.RollInjuryRisk(impactForce * 0.5f); // 공격자는 절반 위험
            result.DefenderInjuryRisk = defender.RollInjuryRisk(impactForce);

            return result;
        }

        /// <summary>
        /// 경합 결과를 물리적으로 적용합니다 (Rigidbody에 힘 추가).
        /// </summary>
        /// <param name="result">EvaluateDuel의 결과</param>
        /// <param name="attackerRb">태클러 Rigidbody</param>
        /// <param name="defenderRb">드리블러 Rigidbody</param>
        /// <param name="contactDirection">충돌 방향 (태클러 → 드리블러)</param>
        public static void ApplyDuelPhysics(
            DuelResult result,
            Rigidbody attackerRb,
            Rigidbody defenderRb,
            Vector3 contactDirection)
        {
            Vector3 pushDir = contactDirection.normalized;
            float force = result.PushForceMagnitude;

            if (result.AttackerWins)
            {
                // 드리블러가 밀려남
                if (defenderRb != null)
                    defenderRb.AddForce(pushDir * force, ForceMode.Impulse);

                // 태클러는 약한 반작용
                if (attackerRb != null)
                    attackerRb.AddForce(-pushDir * force * 0.3f, ForceMode.Impulse);
            }
            else
            {
                // 태클러가 밀려남 (탈압)
                if (attackerRb != null)
                    attackerRb.AddForce(-pushDir * force, ForceMode.Impulse);

                // 드리블러 약간 밀림
                if (defenderRb != null)
                    defenderRb.AddForce(pushDir * force * 0.2f, ForceMode.Impulse);
            }
        }

        // =========================================================
        // 내부 계산
        // =========================================================

        /// <summary>
        /// 개인 경합 점수 계산.
        /// Score = w1·Physical + w2·Composure + w3·SpeedBonus
        /// </summary>
        private static float CalculateDuelScore(PlayerStats stats, float currentSpeed)
        {
            float physical  = stats.GetEffectiveStat(StatType.Physical);
            float composure = stats.GetEffectiveStat(StatType.Composure);
            float speed     = stats.GetEffectiveStat(StatType.Speed);

            // 속도 보너스: 달려오는 힘 = 현재 속도 / 최대 속도 × Speed 스탯
            float speedBonus = currentSpeed * (speed / 100f);

            return W_STRENGTH * physical + W_BALANCE * composure + W_SPEED * speedBonus * 100f;
        }

        /// <summary>
        /// Sigmoid 활성화 함수. 차이값을 0~1 확률로 변환.
        /// σ(x) = 1 / (1 + e^(-x))
        /// </summary>
        private static float Sigmoid(float x)
        {
            return 1f / (1f + Mathf.Exp(-x));
        }

        /// <summary>
        /// 파울 리스크 계산.
        /// 승률이 낮을수록, Composure(침착성)가 낮을수록 파울 확률 증가.
        /// </summary>
        private static float CalculateFoulRisk(float winProbability, PlayerStats attacker)
        {
            float composure = attacker.GetEffectiveStat(StatType.Composure) / 100f;

            // 기본 파울 확률: 승률이 FOUL_THRESHOLD 미만이면 증가
            float baseFoulChance = 0.05f; // 기본 5%

            if (winProbability < FOUL_THRESHOLD)
            {
                // 이길 가능성 낮을수록 파울 확률 급증
                float desperationFactor = 1f - (winProbability / FOUL_THRESHOLD);
                baseFoulChance += desperationFactor * 0.3f; // 최대 +30%
            }

            // 침착한 선수는 파울 확률 감소
            baseFoulChance *= (1.5f - composure); // Composure 100 → ×0.5, Composure 0 → ×1.5

            return Mathf.Clamp01(baseFoulChance);
        }

        /// <summary>
        /// 밀어내기 힘 계산.
        /// 두 선수의 스탯 차이에 비례.
        /// </summary>
        private static float CalculatePushForce(float attackScore, float defendScore)
        {
            float diff = Mathf.Abs(attackScore - defendScore);
            return PUSH_FORCE * (1f + diff * 2f);
        }
    }

    /// <summary>경합 결과 데이터</summary>
    public struct DuelResult
    {
        /// <summary>태클러의 승리 확률 (0~1)</summary>
        public float AttackerWinProbability;

        /// <summary>태클러가 이겼는지</summary>
        public bool AttackerWins;

        /// <summary>파울이 발생했는지</summary>
        public bool IsFoul;

        /// <summary>밀어내기 힘 (뉴턴)</summary>
        public float PushForceMagnitude;

        /// <summary>공격자 부상 위험도 (0~1)</summary>
        public float AttackerInjuryRisk;

        /// <summary>수비자 부상 위험도 (0~1)</summary>
        public float DefenderInjuryRisk;
    }
}
