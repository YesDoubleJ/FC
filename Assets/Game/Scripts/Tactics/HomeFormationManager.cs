using UnityEngine;
using System.Collections.Generic;

namespace Game.Scripts.Tactics
{
    public class HomeFormationManager : TeamFormationManager
    {
        protected override void Awake()
        {
            if (formationOffsets == null || formationOffsets.Count == 0)
            {
                InitializeDefaults();
            }
            base.Awake(); // Setup Dictionary
        }

        private void Reset()
        {
            InitializeDefaults();
        }

        private void InitializeDefaults()
        {
            // Default 6v6 Home Offsets
            formationOffsets = new List<FormationEntry>
            {
                new FormationEntry { position = FormationPosition.GK,       offset = new Vector3(0, 0, -46) },
                new FormationEntry { position = FormationPosition.CB_Left,  offset = new Vector3(-6, 0, -25) }, // Moved Up 5m
                new FormationEntry { position = FormationPosition.CB_Right, offset = new Vector3(6, 0, -25) },  // Moved Up 5m
                new FormationEntry { position = FormationPosition.CM_Left,  offset = new Vector3(-15, 0, 0) },
                new FormationEntry { position = FormationPosition.CM_Right, offset = new Vector3(15, 0, 0) },
                new FormationEntry { position = FormationPosition.ST_Center,offset = new Vector3(0, 0, 25) },
                
                // Alternatives
                new FormationEntry { position = FormationPosition.ST_Left,  offset = new Vector3(-5, 0, 20) },
                new FormationEntry { position = FormationPosition.ST_Right, offset = new Vector3(5, 0, 20) },
                new FormationEntry { position = FormationPosition.LB,       offset = new Vector3(-20, 0, -15) },
                new FormationEntry { position = FormationPosition.RB,       offset = new Vector3(20, 0, -15) },
                new FormationEntry { position = FormationPosition.LM,       offset = new Vector3(-20, 0, 0) },
                new FormationEntry { position = FormationPosition.RM,       offset = new Vector3(20, 0, 0) },
                new FormationEntry { position = FormationPosition.CDM,      offset = new Vector3(0, 0, -10) },
                new FormationEntry { position = FormationPosition.CAM,      offset = new Vector3(0, 0, 10) }
            };
        }
    }
}
