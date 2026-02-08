using UnityEngine;

namespace Game.Scripts.Managers
{
    public class FieldManager : MonoBehaviour
    {
        public static FieldManager Instance { get; private set; }

        [Header("Field Dimensions")]
        public float Length = 100f; // Z: -50 to 50
        public float Width = 60f;   // X: -30 to 30
        public float GoalWidth = 7.32f;
        public float PenaltyAreaLength = 16.5f;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        // FAST PLAY MODE: Cleanup
        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }
    }
}
