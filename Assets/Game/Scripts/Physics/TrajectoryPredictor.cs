using UnityEngine;

namespace Game.Scripts.Physics
{
    public static class TrajectoryPredictor
    {
        /// <summary>
        /// Predicts impact point considering drag (Air Resistance).
        /// Returns the position at targetZ.
        /// </summary>
        public static Vector3 PredictImpactPoint(Vector3 ballPos, Vector3 ballVel, float targetZ)
        {
            // Ball not moving towards target?
            if (Mathf.Abs(ballVel.z) < 0.1f) return ballPos;
            if ((targetZ - ballPos.z) * ballVel.z < 0) return ballPos; // Moving away

            // Iterative Simulation (More accurate than simple formula due to Drag)
            // Simulate 50 steps (approx 1 second ahead)
            Vector3 currentPos = ballPos;
            Vector3 currentVel = ballVel;
            float dt = 0.02f; // Simulation step
            float drag = 0.15f; // Hardcoded approx (or fetch from BallAerodynamics if possible)
            float gravity = UnityEngine.Physics.gravity.y; // -9.81

            for (int i = 0; i < 100; i++) // Max 2 seconds prediction
            {
                // 1. Move
                currentPos += currentVel * dt;

                // 2. Apply Gravity
                currentVel.y += gravity * dt;

                // 3. Apply Drag (Linear Damping approximation: v *= (1 - drag * dt))
                currentVel *= (1f - drag * dt);

                // 4. Check if we passed the target Z plane
                if ((ballVel.z > 0 && currentPos.z >= targetZ) || (ballVel.z < 0 && currentPos.z <= targetZ))
                {
                    // Linear Interpolation for precise impact point
                    Vector3 prevPos = currentPos - currentVel * dt; // Undo last step
                    float t = (targetZ - prevPos.z) / (currentPos.z - prevPos.z);
                    
                    Vector3 impact = Vector3.Lerp(prevPos, currentPos, t);
                    if (impact.y < 0) impact.y = 0; // Ground clamp
                    return impact;
                }
            }

            // If never reached (stopped before line), return last pos
            return currentPos;
        }
    }
}