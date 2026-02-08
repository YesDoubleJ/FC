using UnityEngine;

namespace Game.Scripts.AI
{
    [CreateAssetMenu(fileName = "AgentBallHandlerSettings", menuName = "AI/AgentBallHandlerSettings")]
    public class AgentBallHandlerSettings : ScriptableObject
    {
        [Header("Possession")]
        public float claimDistance = 1.5f;
        public float losePossessionDistance = 1.75f;
        public float possessionGraceTime = 2.0f;

        [Header("Dribble Logic")]
        [Tooltip("Distance to consider ball 'in pocket'")]
        public float inPocketDistance = 1.2f;
        [Tooltip("Angle to consider ball 'in pocket' when stationary (Currently unused in rollback)")]
        public float inPocketAngleStationary = 45f;
        [Tooltip("Angle to consider ball 'in pocket' when moving (Currently unused in rollback)")]
        public float inPocketAngleMoving = 60f;
        
        [Header("Dribble Dynamic Exit")]
        public float stickyDynamicBaseDist = 1.7f;
        public float stickyDynamicSpeedFactor = 0.25f;
        
        [Header("Dribble Entry")]
        public float dribbleEntryMaxDist = 1.5f;
        public float dribbleEntryMaxBallSpeed = 20.0f;
        
        [Header("Dribble Physics - Moving")]
        public float dribbleMoveForceMultiplier = 10.0f;
        public float dribbleMoveDamp = 6.0f;
        public float dribbleMoveBallOffset = 0.8f;
        
        [Header("Dribble Physics - Static")]
        public float dribbleStaticForceMultiplier = 8.0f;
        public float dribbleStaticDamp = 8.0f;
        public float dribbleStaticBallOffset = 0.5f;

        [Header("Dribble Correction")]
        public float dribbleBaseDist = 0.8f;
        public float dribbleDistSpeedFactor = 0.15f;
        public float dribbleMinDist = 0.8f;
        public float dribbleMaxDist = 1.4f;
        public float correctionGainMin = 4.0f;
        public float correctionGainMax = 20.0f;
        public float velocityGainMin = 6.0f;
        public float velocityGainMax = 25.0f;
        public float maxCorrectionVelocity = 10.0f;

        [Header("Passing")]
        public float passPowerBase = 8.0f;
        public float passPowerDistFactor = 0.5f;
        public float passPowerMin = 5.0f;
        public float passPowerMax = 30.0f;
        public float passErrorBaseAngle = 15.0f;
        public float passAlignTimeout = 1.0f;
        public float passAlignSweetSpot = 0.45f;
        public float passAlignPullStrengthSimple = 12.0f;
        public float passAlignPullStrengthHard = 20.0f;

        [Header("Shooting")]
        public float shootPowerBase = 15.0f; // Stronger than pass
        public float shootPowerDistFactor = 0.8f;
        public float shootPowerMin = 10.0f;
        public float shootPowerMax = 40.0f;
        public float shootErrorBaseAngle = 10.0f; // More precise than pass

        [Header("Kicking")]
        public float highClearancePower = 25.0f;
        public float kickMissDistance = 2.5f;
        public float kickCooldown = 0.5f;
        
        [Header("Kick Check")]
        public float canKickAngle = 30.0f;
        public float canKickDist = 2.0f;

        [Header("Field Bounds")]
        public float fieldHalfWidth = 32f;
        public float fieldHalfLength = 48f;

        [Header("Pass & Kick Alignment")]
        public float passAlignDamp = 5.0f;
        public float kickAbortDistance = 1.8f;
        public float dribbleMoveSpeedScale = 1.05f;
        public float kickAlignmentAngle = 20.0f;
        public float kickAlignmentDist = 0.7f;

        [Header("Stationary Control")]
        public float stationaryDamp = 5.0f;
        public float stationaryNudgeForce = 3.0f;

        [Header("Pass Prediction")]
        public float predictedBallSpeed = 15f;
        public float maxPredictionTime = 1.5f;
    }
}
