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
        private float _defenseBurstCooldownTimer = 0f;
        private float _attackBurstCooldownTimer = 0f;
        private float _breakthroughCooldownTimer = 0f;
        private float _tackleCooldownTimer = 0f;
        private float _bodyCheckCooldownTimer = 0f;

        // Skill States
        public bool IsBreakthroughActive { get; private set; }

        public bool CanUseBreakthrough => _breakthroughCooldownTimer <= 0f;
        public bool CanUseAttackBurst => _attackBurstCooldownTimer <= 0f;
        public bool CanUseDefenseBurst => _defenseBurstCooldownTimer <= 0f;
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
            IsBreakthroughActive = false;
            
            // Restore Speed/Accel if modified
            if (_agent != null && _preSkillSpeed > 0)
            {
                 _agent.speed = _preSkillSpeed;
                 _agent.acceleration = _preSkillAccel;
            }
            _preSkillSpeed = -1f;
            _preSkillAccel = -1f;

            // Optional: Reset cooldowns to ready state?
            _defenseBurstCooldownTimer = 0f;
            _attackBurstCooldownTimer = 0f;
            _breakthroughCooldownTimer = 0f;
            _tackleCooldownTimer = 0f;
            _bodyCheckCooldownTimer = 0f;
        }

        public void Tick()
        {
            float dt = Time.deltaTime;
            if (_defenseBurstCooldownTimer > 0) _defenseBurstCooldownTimer -= dt;
            if (_attackBurstCooldownTimer > 0) _attackBurstCooldownTimer -= dt;
            if (_breakthroughCooldownTimer > 0) _breakthroughCooldownTimer -= dt;
            if (_tackleCooldownTimer > 0) _tackleCooldownTimer -= dt;
            if (_bodyCheckCooldownTimer > 0) _bodyCheckCooldownTimer -= dt;
        }

        // =========================================================
        // SKILLS
        // =========================================================

        public void ActivateDefenseBurst()
        {
            if (!CanUseDefenseBurst) return;
            
            float cooldown = (_controller.config) ? _controller.config.CooldownDefenseBurst : 5.0f;
            _defenseBurstCooldownTimer = cooldown; 
            LogSkill("DEFENSE BURST");
            
            // Physics Boost (Instant Stop & Face Ball)
            if (_agent.isOnNavMesh) _agent.ResetPath();
            _rb.linearVelocity = Vector3.zero;
            
            // Visual feedback handled by state usually, but valid here too.
        }

        public void ActivateAttackBurst()
        {
             if (!CanUseAttackBurst) return;
             
             StartCoroutine(AttackBurstRoutine());
        }

        private System.Collections.IEnumerator AttackBurstRoutine()
        {
            float cooldown = (_controller.config) ? _controller.config.CooldownAttackBurst : 5.0f;
            _attackBurstCooldownTimer = cooldown;
            LogSkill("ATTACK BURST");

            if (_agent.isOnNavMesh)
            {
                // Capture original state
                _preSkillSpeed = _agent.speed;
                _preSkillAccel = _agent.acceleration;
                
                // Pure Speed Boost
                float multiplier = (_controller.config) ? _controller.config.BurstSpeedMultiplier : 1.6f;
                _agent.speed = _preSkillSpeed * multiplier;
                _agent.acceleration = _preSkillAccel * multiplier;
                
                yield return new WaitForSeconds(3.0f);
                
                // Restore
                _agent.speed = _preSkillSpeed;
                _agent.acceleration = _preSkillAccel;
                
                _preSkillSpeed = -1f;
            }
        }

        public void ActivateBreakthrough()
        {
             if (!CanUseBreakthrough) return;
             
             StartCoroutine(BreakthroughRoutine());
        }

        private System.Collections.IEnumerator BreakthroughRoutine()
        {
            IsBreakthroughActive = true;
            float cooldown = (_controller.config) ? _controller.config.CooldownBreakthrough : 5.0f;
            _breakthroughCooldownTimer = cooldown;
            
            // 1. PUSH NEARBY OPPONENTS (Optimized)
            int hitCount = UnityEngine.Physics.OverlapSphereNonAlloc(transform.position, 3f, _nearbyColliders);
            for (int i = 0; i < hitCount; i++)
            {
                var collider = _nearbyColliders[i];
                var opponent = collider.GetComponent<HybridAgentController>();
                if (opponent != null && opponent.TeamID != _controller.TeamID)
                {
                    Vector3 pushDir = (opponent.transform.position - transform.position).normalized;
                    Rigidbody opponentRb = opponent.GetComponent<Rigidbody>();
                    if (opponentRb != null)
                    {
                        float pushForce = (_controller.config) ? _controller.config.BreakthroughPushForce : 10f;
                        opponentRb.AddForce(pushDir * pushForce, ForceMode.Impulse);
                    }
                }
            }
            
            // 2. CHARGE FORWARD
            float goalDirection = (_controller.TeamID == Data.Team.Home) ? 1f : -1f;
            float targetZ = (MatchManager.Instance != null) 
                          ? MatchManager.Instance.GetAttackGoalPosition(_controller.TeamID).z 
                          : 52.0f * goalDirection;

            Vector3 chargeDir = new Vector3(0, 0, goalDirection);
            Vector3 targetPos = new Vector3(transform.position.x, 0, targetZ);
            
            if (_agent.isOnNavMesh)
            {
                // Capture original state
                _preSkillSpeed = _agent.speed;
                _preSkillAccel = _agent.acceleration;
                
                // Boost speed
                float multiplier = (_controller.config) ? _controller.config.BreakthroughSpeedMultiplier : 1.5f;
                _agent.speed = _preSkillSpeed * multiplier;
                _agent.acceleration = _preSkillAccel * multiplier;
                
                // FIX: Set Destination so AgentMover doesn't fight the movement!
                _agent.SetDestination(targetPos);
                _agent.isStopped = false;
                
                // Impulse for immediate "Pop"
                float impulse = (_controller.config) ? _controller.config.BreakthroughImpulseForce : 15f;
                _rb.AddForce(chargeDir * impulse, ForceMode.Impulse);
                
                LogSkill("BREAKTHROUGH!");
                
                yield return new WaitForSeconds(1.0f);
                
                // Restore
                _agent.speed = _preSkillSpeed;
                _agent.acceleration = _preSkillAccel;
                _preSkillSpeed = -1f;
            }
            
            IsBreakthroughActive = false;
        }

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
    }
}
