using UnityEngine;

namespace Game.Scripts.Tactics
{
    /// <summary>
    /// Defines how a player moves relative to their formation anchor.
    /// Example: An 'Inverted Wingback' might move towards the center when attacking.
    /// </summary>
    [CreateAssetMenu(fileName = "NewRole", menuName = "LottoSoccer/TacticalRole")]
    public class TacticalRole : ScriptableObject
    {
        public string roleName;
        
        /// <summary>
        /// Returns the desired position for the player, given the base anchor.
        /// </summary>
        public virtual Vector3 GetTargetPosition(Vector3 baseAnchor, Vector3 ballPosition, bool isAttacking)
        {
            // Default behavior: Stick to the anchor
            return baseAnchor;
        }
    }
}
