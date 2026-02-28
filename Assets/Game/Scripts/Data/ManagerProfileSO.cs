using UnityEngine;
using Game.Scripts.Tactics.Data;

namespace Game.Scripts.Data
{
    /// <summary>
    /// Persistent data asset for the Player's Manager Profile.
    /// Stores the current license level and can be easily serialized for Save/Load.
    /// </summary>
    [CreateAssetMenu(fileName = "ManagerProfile", menuName = "Tactics/Manager Profile", order = 0)]
    public class ManagerProfileSO : ScriptableObject
    {
        [Header("License Requirements")]
        [Tooltip("The current license level unlocked by the player.")]
        public LicenseLevel CurrentLicense = LicenseLevel.None_1;

        // Future expansions:
        // public string ManagerName;
        // public int TotalMatchesPlayed;
        // public int TotalWins;
    }
}
