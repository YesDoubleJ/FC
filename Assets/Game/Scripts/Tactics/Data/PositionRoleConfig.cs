using UnityEngine;

namespace Game.Scripts.Tactics.Data
{
    public enum BasePosition
    {
        GK, CB, FB, WB, DM, CM, LM, RM, AM, LW, RW, ST
    }

    public enum InPossessionRole
    {
        // GK
        Goalkeeper, SweeperKeeper, BuildUpKeeper,
        // CB
        CenterBack, SafeCenterBack, RiskTakingCenterBack, AdvancedCenterBack, WideCenterBack, 
        BackCenterBack, CompleteCenterBack, HalfSpaceCenterBack, OverlappingCenterBack,
        // FB/WB
        FullBack, WingBack, InvertedFullBack, InvertedWingBack, CompleteWingBack,
        // DM/CM
        DefensiveMidfielder, HalfBack, Sub6, Sub8, Playmaker6, Playmaker8, HoldingPlaymaker, AdvancedPlaymaker,
        CentralMidfielder, DeepLyingMidfielder, HighMidfielder, CentralPlaymaker, PocketMidfielder, PocketPlaymaker,
        HalfSpaceMidfielder, ChannelMidfielder,
        // LM/RM/LW/RW
        WideMidfielder, Winger, InvertedWinger, TrackerMidfielder, CounterTargetMidfielder,
        // AM
        AttackingMidfielder, DeepLyingAttackingMidfielder, SecondBallAttackingMidfielder, HalfSpaceAttackingMidfielder, 
        ShadowStriker, ChannelAttackingMidfielder,
        // ST
        Striker, DeepLyingStriker, WideStriker, SecondStriker
    }

    public enum OutOfPossessionRole
    {
        // GK
        Goalkeeper, SafeKeeper, SweeperKeeper,
        // CB
        CenterBack, StoppingCenterBack, CoveringCenterBack, SideCoverCenterBack, HoldingCenterBack, TrapCenterBack, DroppingCenterBack,
        // FB/WB
        FullBack, WingBack, PatientFullBack, StoppingFullBack, TrapFullBack, DroppingFullBack,
        // DM/CM
        DefensiveMidfielder, CoverMidfielder, SideCoverMidfielder, StoppingMidfielder, CounterStoppingMidfielder, DroppingMidfielder,
        ChallengingMidfielder, CentralMidfielder, PressingMidfielder, DeepLyingMidfielder, LineFormingMidfielder,
        // LM/RM/LW/RW
        WideMidfielder, Winger, TrackingMidfielder, CounterTargetMidfielder, LineFormingWideMidfielder, PressingWideMidfielder, CounterTargetWinger, PressingWinger,
        // AM
        AttackingMidfielder, CounterTargetAttackingMidfielder, PressingAttackingMidfielder,
        // ST
        Striker, PressingStriker, HoldingStriker, CounterTargetStriker
    }

    [System.Serializable]
    public class PersonalInstructions
    {
        // Example personal instructions shared across positions
        public AttackWidth AttackWidth = AttackWidth.Normal;
        public AttackHeight AttackHeight = AttackHeight.Normal;
        public Patience Patience = Patience.Normal;
        public RiskTaking PassRisk = RiskTaking.Normal;
        public ShootFrequency ShootFrequency = ShootFrequency.Normal;
        public CrossFrequency CrossFrequency = CrossFrequency.Normal;
        public DribbleFrequency DribbleFrequency = DribbleFrequency.Normal;
        public SpaceDribbleFrequency SpaceDribbleFrequency = SpaceDribbleFrequency.Normal;
        public MarkingStyle MarkingStyle = MarkingStyle.Normal;
        public ChallengeIntensity ChallengeIntensity = ChallengeIntensity.Normal;
    }

    /// <summary>
    /// Represents a highly granular position role configuration per the Lotto FC game design.
    /// Roles and personal instructions change based on the phase of play.
    /// </summary>
    [CreateAssetMenu(fileName = "NewPositionRoleConfig", menuName = "Tactics/Position Role Config")]
    public class PositionRoleConfig : ScriptableObject, ITacticalUnlockable
    {
        [Header("License Requirements")]
        [SerializeField] private LicenseLevel _requiredLicense = LicenseLevel.None_1;
        public LicenseLevel RequiredLicense => _requiredLicense;

        public bool IsUnlocked(LicenseLevel currentManagerLicense)
        {
            return currentManagerLicense >= _requiredLicense;
        }

        [Header("Base Position")]
        public BasePosition BasePosition;
        
        [Header("In Possession Roles")]
        public InPossessionRole BuildUpRole;
        public InPossessionRole ProgressionRole;
        public InPossessionRole FinalThirdRole;

        [Header("Out of Possession Roles")]
        public OutOfPossessionRole LowBlockRole;
        public OutOfPossessionRole MidBlockRole;
        public OutOfPossessionRole HighBlockRole;

        [Header("Personal Instructions")]
        public PersonalInstructions BuildUpInstructions;
        public PersonalInstructions ProgressionInstructions;
        public PersonalInstructions FinalThirdInstructions;
        public PersonalInstructions LowBlockInstructions;
        public PersonalInstructions MidBlockInstructions;
        public PersonalInstructions HighBlockInstructions;

        [Header("Utility Multipliers (AI Eval)")]
        public float PassModifier = 1.0f;
        public float ShootModifier = 1.0f;
        public float DribbleModifier = 1.0f;
    }
}
