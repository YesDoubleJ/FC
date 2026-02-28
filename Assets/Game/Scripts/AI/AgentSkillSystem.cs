using UnityEngine;
using Game.Scripts.Managers;
using Game.Scripts.Physics;

namespace Game.Scripts.AI
{
    /// <summary>
    /// Handles special skills and cooldowns for the HybridAgent.
    /// Extracted from HybridAgentController (Refactoring).
    /// </summary>
    [RequireComponent(typeof(AgentMover))]
    public class AgentSkillSystem : MonoBehaviour
    {
        // References
        private HybridAgentController _controller;
        private AgentMover _mover;
        private UnityEngine.AI.NavMeshAgent _agent;
        private Rigidbody _rb;

        // Settings
        // public AgentSkillSettings settings; // REMOVED

        // Optimization
        private readonly Collider[] _nearbyColliders = new Collider[10];
        
        // State Backup
        private float _preSkillSpeed = -1f;
        private float _preSkillAccel = -1f;

        // Timers
        private float _tackleCooldownTimer = 0f;
        private float _bodyCheckCooldownTimer = 0f;

        // Skill States
        public bool CanTackle => _tackleCooldownTimer <= 0f;
        public bool CanBodyCheck => _bodyCheckCooldownTimer <= 0f;

        public void Initialize(HybridAgentController controller)
        {
            _controller = controller;
            _mover = GetComponent<AgentMover>();
            _agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
            _rb = GetComponent<Rigidbody>();
            
            // Configuration is now handled in HybridAgentController

            if (_controller.config == null)
            {
                // Fallback attempt to load if controller failed? Usually controller handles it.
                 Debug.LogWarning($"{name}: MatchEngineConfig not found on Controller!");
            }
        }

        private void OnDisable()
        {
            ResetSkills();
        }

        public void ResetSkills()
        {
            StopAllCoroutines();
            
            // Restore Speed/Accel if modified
            if (_agent != null && _preSkillSpeed > 0)
            {
                 _agent.speed = _preSkillSpeed;
                 _agent.acceleration = _preSkillAccel;
            }
            _preSkillSpeed = -1f;
            _preSkillAccel = -1f;

            // Optional: Reset cooldowns to ready state?
            _tackleCooldownTimer = 0f;
            _bodyCheckCooldownTimer = 0f;
        }

        public void Tick()
        {
            float dt = Time.deltaTime;
            if (_tackleCooldownTimer > 0) _tackleCooldownTimer -= dt;
            if (_bodyCheckCooldownTimer > 0) _bodyCheckCooldownTimer -= dt;
        }

        // =========================================================
        // SKILLS
        // =========================================================

        public void AttemptTackle(HybridAgentController target)
        {
            if (_tackleCooldownTimer > 0) return;
            
            // TACKLE LOGIC: Targeting the BALL
            var matchMgr = MatchManager.Instance;
            bool targetHasBall = (matchMgr != null && matchMgr.CurrentBallOwner == target);
            
            if (!targetHasBall) 
            {
                // Can't tackle without ball.
                return;
            }

            float cooldown = (_controller.config) ? _controller.config.CooldownTackle : 3.0f;
            _tackleCooldownTimer = cooldown;
            LogSkill($"TACKLE on {target.name}");

            // SUCCESSFUL TACKLE (Ball Steal / Dislodge)
            LogSkill("<color=cyan>TACKLE SUCCESS!</color>");
            
            // [FIX] Lunge Forward (Visual/Physical)
            Vector3 lungeDir = (target.transform.position - transform.position).normalized;
            _rb.AddForce(lungeDir * 8.0f, ForceMode.Impulse);
            
            // 1. Trigger Loose Ball (Fumble & Stun)
            var targetHandler = target.GetComponent<AgentBallHandler>();
            if (targetHandler != null) 
            {
                targetHandler.OnTackled(transform.position, 10.0f);
            }
            else
            {
                // Fallback
                MatchManager.Instance.LosePossession(target);
            }

            // 3. No Possession Grace Period
            MatchManager.Instance.SetNoPossessionTime(0.5f);
            
            // 2. Physical Impact (Disorient Opponent)
            float impact = (_controller.config) ? _controller.config.TackleImpactForce : 5f;
            target.VelocityRb.AddForce((target.transform.position - transform.position).normalized * impact, ForceMode.Impulse);
            
            // [FIX] Apply Stun/Knockback state to Target
            var targetMover = target.GetComponent<AgentMover>();
            if (targetMover != null) targetMover.ApplyStun(1.0f);
        }

        public void AttemptBodyCheck(HybridAgentController target)
        {
            if (_bodyCheckCooldownTimer > 0) return;
            float cooldown = (_controller.config) ? _controller.config.CooldownBodyCheck : 3.0f;
            _bodyCheckCooldownTimer = cooldown;

            LogSkill($"BODY CHECK on {target.name}");

            Vector3 pushDir = (target.transform.position - transform.position).normalized;
            // Strong Push
            float force = (_controller.config) ? _controller.config.BodyCheckForce : 15f;
            target.VelocityRb.AddForce(pushDir * force, ForceMode.Impulse); 
        }

        public bool IsFrontalBlocked()
        {
            // Simple Raycast Check
            if (UnityEngine.Physics.Raycast(transform.position + Vector3.up * 0.5f, transform.forward, out RaycastHit hit, 2.0f))
            {
                var opponent = hit.collider.GetComponent<HybridAgentController>();
                if (opponent != null && opponent.TeamID != _controller.TeamID)
                {
                    return true;
                }
            }
            return false;
        }

        private void LogSkill(string skillName)
        {
             if (Game.Scripts.UI.MatchViewController.Instance != null)
                Game.Scripts.UI.MatchViewController.Instance.LogSkill($"{name} {skillName}", _controller.TeamID);
             Debug.Log($"<color=green>{name} SKILL: {skillName}</color>");
        }

        public enum SkillType
        {
            Tackle,
            BodyCheck
        }

    }
}
