using UnityEngine;
using System.Collections.Generic;

namespace Game.Scripts.Tactics.Data
{
    [System.Serializable]
    public class PositionSlot
    {
        public FormationPosition Position;
        
        [Tooltip("Relative position (-1 to 1 range). X: Left-Right, Y: Back-Front")]
        public Vector2 BasePosition;
        
        [Tooltip("Offset to compress the formation when defending.")]
        public Vector2 DefensiveOffset;
        
        [Tooltip("Relative position (-1 to 1 range) used when OUR team is taking the kickoff.")]
        public Vector2 OffensiveKickoff;

        [Tooltip("Relative position (-1 to 1 range) used when the OPPOSING team is taking the kickoff.")]
        public Vector2 DefensiveKickoff;
        
        [Tooltip("Allowed roles for this position slot.")]
        public List<PositionRoleConfig> AllowedRoles = new List<PositionRoleConfig>();
    }

    [CreateAssetMenu(fileName = "NewFormation", menuName = "Tactics/Formation Data")]
    public class FormationData : ScriptableObject, ITacticalUnlockable
    {
        [Header("License Requirements")]
        [SerializeField] private LicenseLevel _requiredLicense = LicenseLevel.None_1;
        public LicenseLevel RequiredLicense => _requiredLicense;

        public bool IsUnlocked(LicenseLevel currentManagerLicense)
        {
            return currentManagerLicense >= _requiredLicense;
        }
        
        public string FormationName = "4-2-3-1";

        [Tooltip("Define the 11 specific positions used in this formation.")]
        public List<PositionSlot> Slots = new List<PositionSlot>();

        public PositionSlot GetSlot(FormationPosition position)
        {
            return Slots.Find(s => s.Position == position);
        }

        public Vector2 GetPosition(FormationPosition position)
        {
            var slot = GetSlot(position);
            return slot != null ? slot.BasePosition : Vector2.zero;
        }

        public Vector2 GetKickoffPosition(FormationPosition position, bool isOffensive)
        {
            var slot = GetSlot(position);
            if (slot == null) return Vector2.zero;
            return isOffensive ? slot.OffensiveKickoff : slot.DefensiveKickoff;
        }

        public Vector2 GetDefensivePosition(FormationPosition position)
        {
            var slot = GetSlot(position);
            return slot != null ? slot.BasePosition + slot.DefensiveOffset : Vector2.zero;
        }
    }
}
