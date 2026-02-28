using UnityEngine;

namespace Game.Scripts.AI.Settings
{
    [CreateAssetMenu(fileName = "TacticsTuningSettings", menuName = "AI/TacticsTuningSettings")]
    public class TacticsTuningSettings : ScriptableObject
    {
        [Header("--- IN POSSESSION (볼 소유 시) ---")]
        
        [Header("Attacking Width Multipliers")]
        public float AttackingWidthNarrow = 0.8f;
        public float AttackingWidthNormal = 1.0f;
        public float AttackingWidthWide = 1.25f;

        [Header("Passing Style (Distance Multiplier)")]
        [Tooltip("Multiplies MaxPassRange and ShortPassRange")]
        public float PassStyleShort = 0.75f;
        public float PassStyleNormal = 1.0f;
        public float PassStyleDirect = 1.5f;

        [Header("Tempo (Decision Tick Multiplier)")]
        [Tooltip("Multiplies DecisionInterval (Lower = Faster decisions)")]
        public float TempoSlow = 1.25f;
        public float TempoNormal = 1.0f;
        public float TempoFast = 0.75f;

        [Header("Risk Taking (Threshold Multiplier)")]
        [Tooltip("Multiplies Pass/Shoot Evaluation Thresholds")]
        public float RiskTakingLow = 1.2f;    // Requires higher safety
        public float RiskTakingNormal = 1.0f;
        public float RiskTakingHigh = 0.8f;   // Accepts lower safety passes

        [Header("Time Wasting (Action Delay)")]
        [Tooltip("Adds static delay before actions")]
        public float TimeWastingShort = 0.0f;
        public float TimeWastingNormal = 1.0f;
        public float TimeWastingLong = 3.0f;

        [Header("Penetration Frequency (Forward Bias)")]
        [Tooltip("Bonus to forward passes/runs")]
        public float PenetrationLow = 0.8f;
        public float PenetrationNormal = 1.0f;
        public float PenetrationHigh = 1.3f;


        [Header("--- OUT OF POSSESSION (볼 상대 소유 시) ---")]

        [Header("Defensive Width Multipliers")]
        public float DefensiveWidthNarrow = 0.75f;
        public float DefensiveWidthNormal = 1.0f;
        public float DefensiveWidthWide = 1.25f;

        [Header("Defensive Line Height (Z-Coordinate)")]
        [Tooltip("Base Z coordinate limit for the defensive line")]
        public float DefLineLowBlock = 25f;
        public float DefLineMidBlock = 40f;
        public float DefLineHighLine = 50f;

        [Header("Tackle Intensity (Force Multiplier)")]
        [Tooltip("Multiplies Tackle/BodyCheck force and cooldowns")]
        public float TackleIntensityWeak = 0.7f;
        public float TackleIntensityNormal = 1.0f;
        public float TackleIntensityStrong = 1.4f;

        [Header("Offside Trap Frequency / Probability")]
        public float OffsideTrapSometimes = 0.2f;
        public float OffsideTrapNormal = 0.5f;
        public float OffsideTrapOften = 0.8f;


        [Header("--- TRANSITIONS (공수 교대 시) ---")]
        
        [Header("Attacking Transition (Counter Attack Speed Bonus)")]
        [Tooltip("Sprint/Decision modifier during Counter Attack")]
        public float CounterAttackSpeedBonus = 1.2f;
        public float HoldShapeSpeedPenalty = 0.9f;

        [Header("Defensive Transition (Counter Press)")]
        [Tooltip("Pressing intensity/range modifier right after losing ball")]
        public float CounterPressIntensity = 1.5f;
        public float RegroupDropSpeedBonus = 1.2f;
        
    }
}
