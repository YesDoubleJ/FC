using UnityEngine;
using System.Collections.Generic;
using Game.Scripts.Tactics;

namespace Game.Scripts.Tactics
{
    [System.Serializable]
    public struct FormationEntry
    {
        public FormationPosition position;
        public Vector3 offset;
    }

    public abstract class TeamFormationManager : MonoBehaviour
    {
        public List<FormationEntry> formationOffsets = new List<FormationEntry>();
        protected Dictionary<FormationPosition, Vector3> offsetMap = new Dictionary<FormationPosition, Vector3>();

        protected virtual void Awake()
        {
            InitializeMap();
        }

        protected void InitializeMap()
        {
            offsetMap.Clear();
            foreach (var entry in formationOffsets)
            {
                if (!offsetMap.ContainsKey(entry.position))
                {
                    offsetMap.Add(entry.position, entry.offset);
                }
            }
        }

        public Vector3 GetOffset(FormationPosition pos)
        {
            if (offsetMap.ContainsKey(pos))
            {
                return offsetMap[pos];
            }
            return Vector3.zero;
        }
        
        // Editor helper to sync Dictionary if needed at runtime
        private void OnValidate()
        {
            // Optional: validation logic
        }
    }
}
