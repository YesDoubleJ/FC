using UnityEngine;

namespace Game.Scripts.Data
{
    /// <summary>
    /// Holds the core stats for a player.
    /// These stats influence the outcome of actions via the Utility AI and Physics engine.
    /// Range: 0 to 100 (standard football game scale)
    /// </summary>
    public class PlayerStats : MonoBehaviour
    {
        [Header("Offensive")]
        [Range(0, 100)] public float shooting = 50f;
        [Range(0, 100)] public float passing = 50f;
        [Range(0, 100)] public float dribbling = 50f;

        [Header("Physical & Mental")]
        [Range(0, 100)] public float speed = 50f;
        [Range(0, 100)] public float physical = 50f; // Strength/Balance
        [Range(0, 100)] public float technique = 50f; // Ball control
        [Range(0, 100)] public float composure = 50f; // Mental stability under pressure

        /// <summary>
        /// Getting a stat might eventually involve modifiers (fatigue, morale, form).
        /// For now, returns the raw base value.
        /// </summary>
        public float GetStat(StatType type)
        {
            switch (type)
            {
                case StatType.Shooting: return shooting;
                case StatType.Passing: return passing;
                case StatType.Dribbling: return dribbling;
                case StatType.Speed: return speed;
                case StatType.Physical: return physical;
                case StatType.Technique: return technique;
                case StatType.Composure: return composure;
                default: return 50f;
            }
        }
    }

    public enum StatType
    {
        Shooting,
        Passing,
        Dribbling,
        Speed,
        Physical,
        Technique,
        Composure
    }
}
