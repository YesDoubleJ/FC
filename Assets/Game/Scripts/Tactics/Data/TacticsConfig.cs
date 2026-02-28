using UnityEngine;

namespace Game.Scripts.Tactics.Data
{
    [System.Serializable]

    public class InPossessionInstructions
    {
        [Header("Base")]
        public PassStyle PassingStyle = PassStyle.Normal;
        public Tempo Tempo = Tempo.Normal;
        public AttackingTransition Transition = AttackingTransition.Normal;
        public TimeWasting TimeWasting = TimeWasting.Normal;
        public AttackWidth AttackingWidth = AttackWidth.Normal;

        [Header("Build-Up Phase")]
        public RiskTaking BuildUpRiskTaking = RiskTaking.Normal;

        [Header("Progression Phase")]
        public AttackFocus AttackFocus = AttackFocus.None;

        [Header("Final Third Phase")]
        public PenetrationFrequency PenetrationFrequency = PenetrationFrequency.Normal;
    }

    [System.Serializable]
    public class OutOfPossessionInstructions
    {
        [Header("Base")]
        public TackleIntensity TackleIntensity = TackleIntensity.Normal;
        public OffsideTrap OffsideTrap = OffsideTrap.Normal;
        public DefensiveWidth DefensiveWidth = DefensiveWidth.Normal;

        [Header("Low Block")]
        public DefensiveLine LowBlockLine = DefensiveLine.Low;
        public DefensiveTransition LowBlockTransition = DefensiveTransition.Regroup;
        public FlankSpace FlankSpace = FlankSpace.Block;

        [Header("Mid Block")]
        public DefensiveLine MidBlockLine = DefensiveLine.Normal;
        public DefensiveTransition MidBlockTransition = DefensiveTransition.Mixed;

        [Header("High Block")]
        public DefensiveLine HighBlockLine = DefensiveLine.High;
        public GKPress HighBlockGKPress = GKPress.No;
        public DefendStyle HighBlockDefendStyle = DefendStyle.Mixed;
        public DefensiveTransition HighBlockTransition = DefensiveTransition.CounterPress;
    }

    /// <summary>
    /// Holds the global tactical instructions for a team, separated into phases.
    /// Restricted by the manager's license level.
    /// </summary>
    [CreateAssetMenu(fileName = "NewTacticsConfig", menuName = "Tactics/Tactics Config")]
    public class TacticsConfig : ScriptableObject, ITacticalUnlockable
    {
        [Header("License Requirements")]
        [SerializeField] private LicenseLevel _requiredLicense = LicenseLevel.C_1;
        public LicenseLevel RequiredLicense => _requiredLicense;

        public bool IsUnlocked(LicenseLevel currentManagerLicense)
        {
            return currentManagerLicense >= _requiredLicense;
        }

        [Header("Global Settings")]
        public TeamMentality Mentality = TeamMentality.Balanced;
        public FormationData SelectedFormation;

        [Header("Phase Instructions")]
        public InPossessionInstructions InPossession = new InPossessionInstructions();
        public OutOfPossessionInstructions OutOfPossession = new OutOfPossessionInstructions();
        
        // Note: The specific features (e.g., Attack Focus) inside these classes may be individually 
        // locked by license levels in future logic, but the base config itself determines if the 
        // overall strategy can be equipped.
    }
}
