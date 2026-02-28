using UnityEngine;
using Game.Scripts.Tactics.Data;

namespace Game.Scripts.Tactics
{
    /// <summary>
    /// Attached to each HybridAgentController to hold its specific tactical role data.
    /// Evaluates if the agent's assigned role is authorized by the manager's license.
    /// </summary>
    public class TacticalRoleData : MonoBehaviour
    {
        [Tooltip("The tactical role assigned to this player.")]
        public PositionRoleConfig AssignedRole;

        private PositionRoleConfig _activeRole;

        /// <summary>
        /// Gets the active role for the player.
        /// Returns the assigned role if authorized by the manager's license.
        /// If unauthorized, logs a warning and returns null (forcing default behavior).
        /// </summary>
        public PositionRoleConfig Role
        {
            get
            {
                if (_activeRole != null) return _activeRole;
                
                // Fallback: If no license manager exists or no role is assigned
                if (AssignedRole == null) return null;

                if (PlayerLicenseManager.Instance != null)
                {
                    if (AssignedRole.IsUnlocked(PlayerLicenseManager.Instance.CurrentLicense))
                    {
                        _activeRole = AssignedRole;
                    }
                    else
                    {
                        Debug.LogWarning($"[TacticalRoleData] Agent {gameObject.name} assigned role '{AssignedRole.name}' requires {AssignedRole.RequiredLicense} license, but manager has {PlayerLicenseManager.Instance.CurrentLicense}. Falling back to default role.");
                        _activeRole = null; // Basic role behavior
                    }
                }
                else
                {
                    // If no LicenseManager is in the scene, assume unlocked for testing
                    _activeRole = AssignedRole;
                }

                return _activeRole;
            }
        }

        /// <summary>
        /// Assigns a new tactical role to the player and forces re-evaluation of authorization.
        /// </summary>
        /// <param name="newRole">The new PositionRoleConfig to assign.</param>
        public void AssignRole(PositionRoleConfig newRole)
        {
            if (newRole == null)
            {
                Debug.LogWarning($"[TacticalRoleData] Agent {gameObject.name} was assigned a null role.");
            }
            AssignedRole = newRole;
            _activeRole = null; // Force re-evaluation on next get
        }
    }
}
