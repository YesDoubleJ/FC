namespace Game.Scripts.Tactics.Data
{
    // --- Team Mentality ---
    public enum TeamMentality
    {
        ExtremeDefensive,
        Defensive,
        Balanced,
        Offensive,
        ExtremeOffensive
    }

    // --- In Possession (볼 소유 시) Enums ---
    
    // Base
    public enum PassStyle { Short, Normal, Direct }
    public enum Tempo { Slow, Normal, Fast }
    public enum AttackingTransition { Counter, Normal, HoldShape }
    public enum TimeWasting { Short, Normal, Long } // 짧게, 보통, 길게

    // Build-up Phase
    public enum RiskTaking { Low, Normal, High }

    // Progression Phase
    public enum AttackFocus { None, Center, Left, Right, BothFlanks }

    // Final Third Phase
    public enum PenetrationFrequency { Low, Normal, High }

    // --- Out of Possession (볼 미소유 시) Enums ---
    
    // Base
    public enum TackleIntensity { Weak, Normal, Strong }
    public enum OffsideTrap { Sometimes, Normal, Often }

    // Blocks
    public enum DefensiveLine { Low, Normal, High }
    public enum DefensiveTransition { CounterPress, Mixed, Regroup }
    public enum FlankSpace { Block, Normal, Allow }
    public enum DefendStyle { ManToMan, Mixed, Zonal }
    public enum DefensiveWidth { Narrow, Normal, Wide }
    
    // --- Personal Instructions Enums ---
    
    public enum DistributionTarget { CB, FB, Flank, ST }
    public enum DistributionSpeed { Slow, Normal, Fast }
    public enum Positioning { Low, Normal, High }
    public enum SweepingFrequency { Low, Normal, High }
    public enum ClearanceStyle { Catch, Normal, Punch }
    
    public enum OverlapPreference { Overlap, None, Underlap }
    public enum SpaceDribbleFrequency { Low, Normal, High }
    public enum Patience { Low, Normal, High }
    public enum AttackWidth { Narrow, Normal, Wide }
    public enum AttackHeight { Low, Normal, High }
    public enum ShootFrequency { Low, Normal, High }
    
    public enum DribbleFrequency { Low, Normal, High }
    public enum EarlyCrossFrequency { Low, Normal, High }
    public enum CrossStyle { Low, Normal, High }
    public enum CrossTarget { FarPost, Center, NearPost, Mixed }
    public enum CrossFrequency { Low, Normal, High }

    public enum MarkingStyle { ManToMan, Normal, Zonal }
    public enum ChallengeIntensity { Weak, Normal, Strong }
    public enum StoppingFrequency { Low, Normal, High }

    public enum TempoControl { Yes, No }
    public enum GKPress { Yes, No }
}
