using UnityEngine;

namespace Game.Scripts.AI
{
    [CreateAssetMenu(fileName = "GoalkeeperSettings", menuName = "AI/GoalkeeperSettings")]
    public class GoalkeeperSettings : ScriptableObject
    {
        [Header("Positioning")]
        public float goalLineZ = 47f;
        public float positioningFarDist = 5.0f;
        public float positioningMidDist = 12.0f;
        public float positioningCloseDist = 3.0f;

        [Header("Reaction")]
        public float dangerousShotSpeedThreshold = 8.0f;
        public float dangerousShotTimeThreshold = 2.5f;

        [Header("Movement Stats")]
        public float saveReactionSpeed = 7.5f;
        public float saveReactionAccel = 45f;
        public float normalPositionSpeed = 4.5f;
        public float normalPositionAccel = 10f;
    }
}
