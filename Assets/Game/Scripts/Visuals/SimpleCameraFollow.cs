using UnityEngine;

namespace Game.Scripts.Visuals
{
    public class SimpleCameraFollow : MonoBehaviour
    {
        public Transform target;
        [SerializeField] private Vector3 offset; // Calculated on Start
        [SerializeField] private float smoothSpeed = 10f; // Snappier
        [SerializeField] private bool lockY = true; // Prevent camera from bobbing up and down

        private void Start()
        {
            if (target == null)
            {
                var ball = GameObject.Find("Ball");
                if (ball != null) target = ball.transform;
            }

            if (target != null)
            {
                // Calculate initial offset based on Scene View placement
                offset = transform.position - target.position;
                
                // If LockY is on, we might want to enforce a specific height or kept relative?
                // For now, simple diff is fine. 
                // If the ball is on ground (0) and camera is (10), offset.y is 10.
            }
        }

        private void LateUpdate() // LateUpdate for smooth camera
        {
            if (target == null)
            {
                 var ball = GameObject.Find("Ball");
                 if (ball != null) target = ball.transform;
                 if (target != null) offset = transform.position - target.position; // Re-calc
                 return;
            }

            Vector3 desiredPos = target.position + offset;

            if (lockY)
            {
                // Keep the Y position constant (based on initial offset or specific height)
                // If we want to strictly follow X/Z but keep Y absolute:
                desiredPos.y = transform.position.y; // Keep current Height? OR target.y + offset.y?
                
                // Better approach for Top-Down/isometric:
                // We want the CAMERA height to stay consistent, even if ball jumps?
                // Yes, usually.
                desiredPos.y = target.position.y + offset.y; 
            }

            Vector3 smoothedPos = Vector3.Lerp(transform.position, desiredPos, smoothSpeed * Time.deltaTime);
            transform.position = smoothedPos;
            
            // Optional: Look at target? 
            // If we are side-view, we might NOT want to LookAt constantly if we want a static angle.
            // But usually "Centered on ball" means looking at it.
            // transform.LookAt(target); 
        }
    }
}
