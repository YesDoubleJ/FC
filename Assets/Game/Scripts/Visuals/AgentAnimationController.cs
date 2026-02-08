using UnityEngine;
using Game.Scripts.AI;

namespace Game.Scripts.Visuals
{
    [RequireComponent(typeof(Animator))]
    public class AgentAnimationController : MonoBehaviour
    {
        private Animator _animator;
        private HybridAgentController _agent;

        // Parameter Hashes for performance
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int ShootTriggerHash = Animator.StringToHash("Shoot");
        private static readonly int PassTriggerHash = Animator.StringToHash("Pass");

        private bool _hasSpeedParam;
        
        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _agent = GetComponentInParent<HybridAgentController>();
            
            // Validate Parameters to prevent Console Spam
            foreach(var param in _animator.parameters)
            {
                if (param.nameHash == SpeedHash && param.type == AnimatorControllerParameterType.Float)
                {
                    _hasSpeedParam = true;
                }
            }
            
            if (!_hasSpeedParam)
            {
                Debug.LogWarning($"[AgentAnimationController] 'Speed' (Float) parameter missing in Animator! Disabling Speed sync.");
            }
        }

        private void Update()
        {
            if (_agent != null && _hasSpeedParam)
            {
                // Sync Speed
                // Assuming NavMeshAgent or Rigidbody velocity
                float speed = _agent.GetComponent<Rigidbody>().linearVelocity.magnitude;
                _animator.SetFloat(SpeedHash, speed);
            }
        }

        public void PlayShoot()
        {
            _animator.SetTrigger(ShootTriggerHash);
        }

        public void PlayPass()
        {
            _animator.SetTrigger(PassTriggerHash);
        }

        /// <summary>
        /// Called via Animation Event at the exact moment the foot hits the ball.
        /// </summary>
        public void OnHitBall()
        {
            if (_agent != null)
            {
                _agent.ExecutePendingKick();
            }
        }
    }
}
