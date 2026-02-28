using UnityEngine;

namespace Game.Scripts.Data
{
    [CreateAssetMenu(fileName = "MatchEngineConfig", menuName = "FC/Match Engine Config", order = 1)]
    public class MatchEngineConfig : ScriptableObject
    {
        // =================================================================================================
        // 1. AI DECISION MAKING (from HybridAgentSettings & DecisionSettings & MatchEngineConfig)
        // =================================================================================================
        [Header("AI Decision Making")]
        [Tooltip("Time interval between decision making updates (seconds).")]
        [Range(0.01f, 1.0f)] public float DecisionInterval = 0.1f;

        [Tooltip("Minimum time between actions to prevent spamming (seconds).")]
        [Range(0.1f, 2.0f)] public float ActionLockoutTime = 0.5f;

        [Tooltip("Minimum score required to attempt a shot.")]
        [Range(0f, 1f)] public float ShootThreshold = 0.5f;

        [Tooltip("Minimum score required to attempt a pass.\n후방 동료에게도 패스하려면 0.25 이하 권장 (EV 계산 기준: pPass × posValue)")]
        [Range(0f, 1f)] public float PassThreshold = 0.25f;

        [Tooltip("Minimum score required to attempt a dribble.")]
        [Range(0f, 1f)] public float DribbleThreshold = 0.3f;

        [Tooltip("Base scores for dribbling decision.")]
        [Range(0f, 1f)] public float BaseDribbleScore = 0.4f;
        public float DribbleScoreBias => BaseDribbleScore; // Alias

        [Tooltip("Time before a waiting receiver gives up (seconds).")]
        [Range(1.0f, 5.0f)] public float ReceiverTimeout = 3.0f;

        // Weights
        [Header("Decision Weights")]
        [Range(0f, 1f)] public float DistanceWeight = 0.85f;
        [Range(0f, 1f)] public float AngleWeight = 0.15f;
        [Range(0f, 1f)] public float StatWeight = 0.8f;

        // =================================================================================================
        // 2. DISTANCES & RANGES (from DecisionSettings & MatchEngineConfig)
        // =================================================================================================
        [Header("Distances & Ranges")]
        [Tooltip("Maximum effective pass range (meters).")]
        [Range(10f, 100f)] public float MaxPassRange = 40.0f;
        
        [Tooltip("Minimum pass range.")]
        [Range(1f, 10f)] public float MinPassDist = 5f;

        [Tooltip("Ideal range for short passes (meters).")]
        [Range(5f, 30f)] public float ShortPassRange = 15.0f;

        [Tooltip("Minimum distance to attempt a long shot (meters).")]
        [Range(15f, 50f)] public float LongShotDistance = 25.0f;
        
        [Tooltip("Sweet spot range for shooting.")]
        [Range(10f, 40f)] public float SweetSpotRange = 22f;

        [Tooltip("Distance considered as a breakaway chance.")]
        [Range(5f, 50f)] public float BreakawayDistance = 14.0f;

        [Tooltip("Distance from goal where GK will attempt emergency clearance.")]
        [Range(5f, 50f)] public float GKDangerDistance = 25.0f;

        [Tooltip("Distance for support positioning buffer.")]
        [Range(0f, 10f)] public float SupportDistance = 2.0f;
        
        [Tooltip("Radius to check for nearby enemies.")]
        [Range(1f, 10f)] public float DangerRadius = 4f;

        // =================================================================================================
        // 3. MOVEMENT & PHYSICS (from AgentMoverSettings & MatchEngineConfig)
        // =================================================================================================
        [Header("Movement & Physics")]
        [Tooltip("Base movement speed for agents.")]
        [Range(1f, 20f)] public float BaseMoveSpeed = 9.0f;

        [Tooltip("Acceleration (Responsiveness). Higher = reaches top speed faster.")]
        [Range(10f, 200f)] public float Acceleration = 50.0f;

        [Tooltip("Friction/Braking force.")]
        [Range(0f, 20f)] public float Friction = 5.0f;

        [Tooltip("Rotation speed for agents (Speed of turning).")]
        [Range(1f, 720f)] public float RotationSpeed = 360f;
        
        [Tooltip("Rotation speed for action aiming (Degrees/sec).")]
        [Range(90f, 1080f)] public float RotationActionSpeed = 720f;

        [Tooltip("Multiplier for sprint speed.")]
        [Range(1.0f, 3.0f)] public float SprintMultiplier = 1.4f;

        [Tooltip("Force applied to recover/trap ball.")]
        [Range(0f, 50f)] public float RecoveryStopForce = 20.0f;
        
        [Tooltip("Angle threshold to consider facing the ball for recovery.")]
        [Range(1f, 90f)] public float RecoveryFaceAngle = 15f;

        [Tooltip("Force applied to separate overlapping agents.")]
        [Range(0f, 100f)] public float SeparationForce = 5.0f;
        
        [Tooltip("Distance at which separation force applies.")]
        [Range(0f, 5f)] public float SeparationDistance = 0.9f;

        [Tooltip("Dribble turn angle threshold.")]
        [Range(10f, 90f)] public float DribbleTurnAngleThreshold = 30f;
        
        [Tooltip("Brake force when turning while dribbling.")]
        [Range(0f, 20f)] public float DribbleBrakeForce = 4.0f;

        [Range(1f, 10f)] public float DribbleDistance = 5.0f; // Kept from original MatchEngineConfig
        
        [Tooltip("The ideal distance (meters) to keep the ball *in front* of the player while dribbling.")]
        [Range(0.4f, 1.0f)] public float DribbleSweetSpotDist = 0.65f;

        [Tooltip("Distance threshold (meters) where the ball is considered 'too close' and needs pushing.")]
        [Range(0.2f, 0.5f)] public float DribbleCloseThreshold = 0.45f;

        [Header("Stats Influence")]
        [Range(0.1f, 1f)] public float StatMinMultiplier = 0.75f;
        [Range(1f, 2f)] public float StatMaxMultiplier = 1.25f;

        // =================================================================================================
        // 4. SKILLS & COOLDOWNS (from AgentSkillSettings)
        // =================================================================================================
        [Header("Skills & Cooldowns")]
        public float CooldownTackle = 3.0f;
        public float CooldownBodyCheck = 3.0f;

        [Header("Skill Parameters")]
        public float TackleStealForce = 12f;
        public float TackleImpactForce = 5f;
        public float BodyCheckForce = 15f;

        // =================================================================================================
        // 5. TACTICS (from DecisionSettings)
        // =================================================================================================
        [Header("Tactical Constraints")]
        public float MaxPassAngle = 45f;
        public float MaxDribbleAngle = 45f;
        public float LookAtMoveDirectionThreshold = 5.5f;

        [Header("Global Tactical Tuning")]
        public AI.Settings.TacticsTuningSettings TacticsTuning;

        [Header("Tactical Settings")]
        [Tooltip("Pressing intensity multiplier for the defending team.")]
        [Range(0.5f, 2.0f)] public float PressingIntensity = 1.0f;
        [Tooltip("Defensive line height scalar (0 = deep, 1 = high line).")]
        [Range(0f, 1f)] public float DefensiveLineHeight = 0.5f;

        // Defending
        public float TackleRange = 2.0f;
        public float BodyCheckRange = 1.5f;

        [Header("Defensive Tactics")]
        public float RedZoneDistance = 25f;
        public float ContainmentDistNormal = 2.5f;
        public float ContainmentDistClose = 1.2f;
        public float ContainmentDistTight = 0.4f;
        public float RecoverySprintDist = 8.0f;

        [Header("Positioning & Support")]
        public float SupportSprintThreshold = 8.0f;
        public float StrikerDepth = 16f;
        public float StrikerChannelWidth = 12f;
        public float MidfieldSupportDist = 13f;
        public float CutbackDeepLine = 45f;
        public float CutbackTargetZ = 38f;
        public float DefenderBackDist = 18f;
        public float DefenderWidth = 20f;
        
        // Debugging
        [Header("Debugging")]
        public float DebugColorLerpSpeed = 10f;

        // =================================================================================================
        // 6. PHYSICS CONSTANTS — 지침서 부록 핵심 물리 상수 테이블
        // =================================================================================================
        [Header("Physics Constants — §부록")]
        [Tooltip("공기 밀도 (kg/m³). 표준 대기압 1.225. 날씨/고도에 따라 변동 가능")]
        public float AirDensity = 1.225f;

        [Tooltip("공 질량 (kg). FIFA 공인구 표준 0.43kg")]
        public float BallMass = 0.43f;

        [Tooltip("마그누스 계수 (양력 계수). 회전에 따른 커브 강도")]
        public float MagnusCoeff = 0.0004f;

        [Tooltip("공기 저항 계수. 구체 기준 0.2")]
        public float DragCoeffAir = 0.2f;

        [Tooltip("마른 잔디 마찰 계수. 공이 덜 구름")]
        public float GroundFrictionDry = 0.8f;

        [Tooltip("젖은 잔디 마찰 계수. 스키드 현상")]
        public float GroundFrictionWet = 0.4f;

        [Tooltip("현재 경기장 상태")]
        public GroundCondition CurrentGroundCondition = GroundCondition.Dry;

        /// <summary>현재 환경에 따른 지면 마찰 계수 반환</summary>
        public float CurrentGroundFriction =>
            CurrentGroundCondition == GroundCondition.Wet ? GroundFrictionWet : GroundFrictionDry;

        // =================================================================================================
        // 7. AI TIMING — 지침서 §5.3 의사결정 주기
        // =================================================================================================
        [Header("AI Timing — §5.3")]
        [Tooltip("AI 판단 주기 기본값 (초). Mental 스탯에 따라 가감")]
        public float BaseDecisionTick = 0.2f;

        [Tooltip("피치 컨트롤 모델 인지 반응 속도 상수 (초)")]
        public float ReactionTimeHuman = 0.7f;

        // =================================================================================================
        // 8. PLAYER PHYSICS — 지침서 §부록
        // =================================================================================================
        [Header("Player Physics — §부록")]
        [Tooltip("Speed 99 기준 최대 스프린트 속도 (m/s). 약 35km/h")]
        public float MaxSprintSpeed = 9.8f;

        [Tooltip("슈팅 오차 범위 (도). Finishing 최하 기준 ±15도")]
        public float ShotErrorNoise = 15f;

        [Tooltip("패스 오차 범위 (도). Passing 최하 기준")]
        public float PassErrorNoise = 10f;
    }

    /// <summary>경기장 환경 상태</summary>
    public enum GroundCondition
    {
        Dry,
        Wet
    }
}
