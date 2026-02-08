using UnityEngine;
using Game.Scripts.Data;

namespace Game.Scripts.AI
{
    /// <summary>
    /// Handles movement, rotation, and physics synchronization for the HybridAgent.
    /// Extracted from HybridAgentController (Refactoring).
    /// </summary>
    [RequireComponent(typeof(UnityEngine.AI.NavMeshAgent))]
    [RequireComponent(typeof(Rigidbody))]
    public class AgentMover : MonoBehaviour
    {
        // References
        private UnityEngine.AI.NavMeshAgent _agent;
        private Rigidbody _rb;
        private HybridAgentController _controller;
        private PlayerStats _stats;

        // Config Accessors (Unifying to Settings)
        private float BaseMoveSpeed 
        {
            get 
            {
                if (_controller == null || _controller.config == null) return 8.0f;

                float baseVal = _controller.config.BaseMoveSpeed;
                if (_stats != null)
                {
                    // Formula: Stat 1 -> 75%, Stat 100 -> 125%
                    float t = Mathf.InverseLerp(1f, 100f, _stats.speed);
                    float multiplier = Mathf.Lerp(_controller.config.StatMinMultiplier, _controller.config.StatMaxMultiplier, t);
                    return baseVal * multiplier;
                }
                return baseVal;
            }
        } 
        private float RotationSpeed => (_controller && _controller.config) ? _controller.config.RotationSpeed : 12f;
        private float SprintSpeed => BaseMoveSpeed * ((_controller && _controller.config) ? _controller.config.SprintMultiplier : 1.4f);
        private float SeparationForce => (_controller && _controller.config) ? _controller.config.SeparationForce : 5f;

        // Recovery State (Trap & Turn)
        public bool IsRecoveringBall { get; set; } = false;
        private Vector3 _pendingDestination;
        public float LastTrapExitTime { get; private set; } = -999f; // Hysteresis Timer

        // Rotation Action
        private bool _isRotatingForAction = false;
        private Quaternion _delayedActionRotation;
        private float _rotationStartTime;
        private System.Action _pendingDelayedAction;

        public float Speed => (_agent != null) ? _agent.speed : 0f;
        public bool IsBusy => _isRotatingForAction; // Partial Busy check (Coordinator will combine)
        public Quaternion LastRotationDelta { get; private set; } = Quaternion.identity;

        // Debugging
        private float _debugTimer = 0f;

        public void Initialize(HybridAgentController controller)
        {
            _controller = controller;
            _agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
            _rb = GetComponent<Rigidbody>();
            _stats = GetComponent<PlayerStats>();
            
            // Configuration is now handled in HybridAgentController (Mover delegates to it)

            if (_agent == null)
            {
                Debug.LogError($"{gameObject.name}: NavMeshAgent component missing!");
                return;
            }

            if (_rb == null)
            {
                Debug.LogError($"{gameObject.name}: Rigidbody component missing!");
                return;
            }

            // CRITICAL: Enable NavMeshAgent first
            _agent.enabled = true;

            // Disable Agent's direct control over Transform
            _agent.updatePosition = false;
            _agent.updateRotation = false;
            
            // Force Physical Settings (User Req: Ensure no floating)
            _agent.baseOffset = 0f; 
            _agent.height = 1.8f; 
            
            // SPEED CONFIGURATION
            // User can now set 'baseMoveSpeed' in Inspector.
            _agent.speed = BaseMoveSpeed; 
            _agent.acceleration = (_controller.config) ? _controller.config.Acceleration : 50f; // Engine internal accel

            // RIGIDBODY CONFIGURATION
            // ... 
            _rb.linearDamping = (_controller.config) ? _controller.config.Friction : 5f; // High damping for instant stops
            _rb.isKinematic = false;
            _rb.useGravity = true; 
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            // CRITICAL: Warp agent to NavMesh to prevent floating
            if (!_agent.isOnNavMesh)
            {
                UnityEngine.AI.NavMeshHit hit;
                if (UnityEngine.AI.NavMesh.SamplePosition(transform.position, out hit, 10f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    _agent.Warp(hit.position);
                    Debug.Log($"{gameObject.name}: Warped to NavMesh at {hit.position}");
                }
                else
                {
                    Debug.LogWarning($"{gameObject.name}: Could not find NavMesh within 10m! Position: {transform.position}");
                    // SAFETY: Disable agent to prevent errors
                    this.enabled = false;
                    _agent.enabled = false;
                }
            }
            else
            {
                Debug.Log($"{gameObject.name}: Already on NavMesh at {transform.position}");
            }
        }

        public void Tick()
        {
            // Debugging: Check Movement status every 1s
            /*
            if (Time.time > _debugTimer + 1.0f)
            {
                _debugTimer = Time.time;
                Debug.Log($"[AgentMover] {gameObject.name}: OnNav={_agent.isOnNavMesh}, Pos={transform.position}, Vel={_rb.linearVelocity.magnitude}, DesVel={_agent.desiredVelocity.magnitude}, RemDist={_agent.remainingDistance}");
            }
            */

            // SAFETY: If falling below world (NavMesh is at Y=-1), teleport back
            if (transform.position.y < -3.0f)
            {
                if (_agent.isOnNavMesh)
                {
                   // _agent.Warp(new Vector3(transform.position.x, -1f, transform.position.z));
                }
                else
                {
                    UnityEngine.AI.NavMeshHit hit;
                    if (UnityEngine.AI.NavMesh.SamplePosition(new Vector3(transform.position.x, 0, transform.position.z), out hit, 20f, UnityEngine.AI.NavMesh.AllAreas))
                    {
                        _agent.Warp(hit.position);
                        _rb.position = hit.position;
                        _rb.linearVelocity = Vector3.zero;
                        Debug.LogWarning($"[AgentMover] {gameObject.name} fell! Rescuing to {hit.position}");
                    }
                }
            }

            if (_isRotatingForAction)
            {
                HandleActionRotation();
                return;
            }

            SyncAgentToBody();
            RotateCharacter();
        }

        public void FixedTick()
        {
            MoveCharacter();
            ApplySeparation();
        }

        // =========================================================
        // MOVEMENT LOGIC
        // =========================================================
        private void MoveCharacter()
        {
            if (_agent == null || !_agent.isOnNavMesh) return;

            // RECOVERY STATE LOGIC (Trap & Turn)
            if (IsRecoveringBall)
            {
                // Failsafe: If I lost the ball, exit immediately
                var mgr = Game.Scripts.Managers.MatchManager.Instance;
                if (mgr == null || mgr.CurrentBallOwner != _controller)
                {
                    IsRecoveringBall = false;
                    // Fall through to normal movement
                }
                else
                {
                    // 1. Stop Movement
                    if (_agent.hasPath) _agent.ResetPath();
                    
                    float stopForce = (_controller.config) ? _controller.config.RecoveryStopForce : 20f;
                    _rb.AddForce(-_rb.linearVelocity * stopForce, ForceMode.Acceleration); // Snappier Stop
                    
                    // 2. Check Alignment
                    if (_controller.BallRb != null)
                    {
                        Vector3 toBall = _controller.BallRb.position - transform.position;
                        toBall.y = 0;
                        float angle = Vector3.Angle(transform.forward, toBall);
                        
                        // Exit Condition: Facing Ball ONLY
                        float faceAngle = (_controller.config) ? _controller.config.RecoveryFaceAngle : 15f;
                        if (angle < faceAngle)
                        {
                            IsRecoveringBall = false;
                            LastTrapExitTime = Time.time; // Mark Exit for Grace Period
                            _agent.SetDestination(_pendingDestination); // Resume Intent
                        }
                    }
                    else
                    {
                        IsRecoveringBall = false;
                    }
                    
                    return; // Don't apply normal movement forces
                }
            }

            // Failsafe: Resume locked agent
            if (_agent.hasPath && _agent.isStopped && !_isRotatingForAction)
            {
                _agent.isStopped = false;
            }

            Vector3 desiredVelocity = _agent.desiredVelocity;
            Vector3 currentVelocity = _rb.linearVelocity;

            // Acceleration Force
            Vector3 forceDir = (desiredVelocity - currentVelocity);
            forceDir.y = 0;

            // DRIBBLE PIVOT LOGIC (User Req: Turn South -> Turn North with ball)
            var matchMgr = Game.Scripts.Managers.MatchManager.Instance;
            if (matchMgr != null && matchMgr.CurrentBallOwner == _controller)
            {
                 if (desiredVelocity.sqrMagnitude > 0.5f)
                 {
                     float angleToTarget = Vector3.Angle(transform.forward, desiredVelocity);
                     
                     // Threshold: Stricter Alignment to prevent Ball Loss
                     float turnThreshold = (_controller.config) ? _controller.config.DribbleTurnAngleThreshold : 30f;
                     if (angleToTarget > turnThreshold)
                     {
                         // Brake lateral movement (turn in place)
                         float brakeForce = (_controller.config) ? _controller.config.DribbleBrakeForce : 4.0f;
                         Vector3 brakeVec = -currentVelocity * brakeForce;
                         brakeVec.y = 0;
                         _rb.AddForce(brakeVec, ForceMode.Acceleration);
                         return; // Wait for alignment
                     }
                 }
            }

             // Use VelocityChange for instant response (snappy movement)
             float acceleration = (_controller.config) ? _controller.config.Acceleration : 50f;
             _rb.AddForce(forceDir * acceleration * Time.deltaTime, ForceMode.VelocityChange);
        }

        private void RotateCharacter()
        {
            // 1. Identify Context
            var matchMgr = Game.Scripts.Managers.MatchManager.Instance;
            bool hasBall = (matchMgr != null && matchMgr.CurrentBallOwner == _controller);
            Vector3 desiredVelocity = _agent.desiredVelocity;
            bool isStationary = (desiredVelocity.sqrMagnitude <= 0.1f);
            
            Vector3 ballPos = Vector3.zero;
            bool ballExists = (_controller.BallRb != null);
            if (ballExists) ballPos = _controller.BallRb.position;

            // 2. Determine Target Direction
            Vector3 lookDir = transform.forward; 
            bool foundTarget = false;

            // PRIORITY 1: Trap/Recovery (Always face ball)
            if (IsRecoveringBall && ballExists)
            {
                lookDir = ballPos - transform.position;
                foundTarget = true;
            }
            // PRIORITY 2: Stationary (Always face ball)
            else if (isStationary)
            {
                if (ballExists)
                {
                    lookDir = ballPos - transform.position;
                    foundTarget = true;
                }
                else if (_agent.hasPath)
                {
                    lookDir = _agent.steeringTarget - transform.position;
                    foundTarget = true;
                }
            }
            // PRIORITY 3: Moving
            else
            {
                if (hasBall)
                {
                    // ON-BALL LOGIC: Look at Ball (User Req)
                    // If ball is extremely close/under, look at movement
                    Vector3 toBall = ballPos - transform.position;
                    if (toBall.sqrMagnitude > 0.01f)
                    {
                        lookDir = toBall;
                    }
                    else
                    {
                        lookDir = desiredVelocity;
                    }
                    foundTarget = true;
                }
                else
                {
                    // OFF-BALL LOGIC
                    // High Speed (Sprint/Chase) -> Look where you are going
                    // Low Speed (Jockey/Wait) -> Look at ball
                    float lookThreshold = (_controller.config) ? _controller.config.LookAtMoveDirectionThreshold : 5.5f;

                    if (desiredVelocity.magnitude > lookThreshold)
                    {
                        lookDir = desiredVelocity;
                        foundTarget = true;
                    }
                    else if (ballExists)
                    {
                        Vector3 toBall = ballPos - transform.position;
                        float dot = Vector3.Dot(desiredVelocity.normalized, toBall.normalized);
                        
                        // Strict alignment check: Only look at ball if vaguely within front 60 degrees of movement
                        if (dot > 0.5f) 
                        {
                            lookDir = toBall;
                        }
                        else
                        {
                            lookDir = desiredVelocity;
                        }
                        foundTarget = true;
                    }
                    else
                    {
                        lookDir = desiredVelocity;
                        foundTarget = true;
                    }
                }
            }

            // 3. Apply Rotation (Planar)
            lookDir.y = 0;
            LastRotationDelta = Quaternion.identity;

            if (lookDir.sqrMagnitude > 0.001f)
            {
                // Calculate Speed (Degrees/Sec)
                float rotationSpeed = (_controller.config) ? _controller.config.RotationSpeed : 360f;
                // float rotationSpeed = rotSpeedVal * 60f; // [REMOVED] Use direct value 

                // Tuning Modifiers
                if (hasBall && !isStationary) 
                {
                    // Boost Rotation for sharp turns
                    float angleError = Vector3.Angle(transform.forward, lookDir);
                    if (angleError > 20f) rotationSpeed *= 1.5f; 
                }
                if (isStationary) rotationSpeed *= 0.8f; // Idle turn

                Quaternion targetRotation = Quaternion.LookRotation(lookDir);
                Quaternion previousRotation = _rb.rotation;
                
                // Perform Rotation
                _rb.rotation = Quaternion.RotateTowards(_rb.rotation, targetRotation, rotationSpeed * Time.deltaTime);
                
                // Capture Delta (For BallHandler Orbit Lock)
                LastRotationDelta = _rb.rotation * Quaternion.Inverse(previousRotation);
            }
        }

        private void SyncAgentToBody()
        {
            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.nextPosition = _rb.position;
                _agent.velocity = _rb.linearVelocity;
            }
        }

        private Collider[] _separationCache = new Collider[10];
        
        private void ApplySeparation()
        {
            float separationDist = (_controller.config) ? _controller.config.SeparationDistance : 0.9f; 
            
            // Optimization: Use NonAlloc
            int count = UnityEngine.Physics.OverlapSphereNonAlloc(transform.position, separationDist, _separationCache);
            
            for (int i = 0; i < count; i++)
            {
                var col = _separationCache[i];
                if (col.transform == transform) continue; // Skip self

                HybridAgentController other = col.GetComponent<HybridAgentController>();
                if (other != null)
                {
                    Vector3 toOther = other.transform.position - transform.position;
                    float dist = toOther.magnitude;

                    bool ignoreSeparation = (!_controller.IsGoalkeeper && other.IsGoalkeeper);
                    
                    if (!ignoreSeparation && dist < separationDist)
                    {
                        Vector3 pushDir = - toOther.normalized; 
                        pushDir.y = 0;
                        float strength = 1.0f - (dist / separationDist);
                        
                        float sepForce = (_controller.config) ? _controller.config.SeparationForce : 5.0f;
                        _rb.AddForce(pushDir * sepForce * strength, ForceMode.VelocityChange);
                    }
                }
            }
        }

        private void HandleActionRotation()
        {
            bool isTimedOut = (Time.time - _rotationStartTime > 1.5f);
            float rotActionSpeed = (_controller.config) ? _controller.config.RotationActionSpeed : 720f;
            float step = rotActionSpeed * Time.deltaTime;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, _delayedActionRotation, step);
            
            if (Quaternion.Angle(transform.rotation, _delayedActionRotation) < 5f || isTimedOut)
            {
                if (isTimedOut) 
                {
                    // Debug.LogWarning($"<color=orange>{name} Action Rotation Timed Out! Forcing action...</color>");
                }
                
                _isRotatingForAction = false;
                _pendingDelayedAction?.Invoke();
                _pendingDelayedAction = null;
            }
        }

        // =========================================================
        // PUBLIC API
        // =========================================================
        public void RotateToAction(Vector3 targetDir, System.Action onComplete)
        {
            targetDir.y = 0;
            if (targetDir == Vector3.zero) 
            {
                onComplete?.Invoke();
                return;
            }

            _delayedActionRotation = Quaternion.LookRotation(targetDir);
            _pendingDelayedAction = onComplete;
            _isRotatingForAction = true;
            _rotationStartTime = Time.time;
        }

        public void Stop()
        {
            if (_agent.isOnNavMesh) _agent.ResetPath();
            _agent.velocity = Vector3.zero;
            _rb.linearVelocity = Vector3.zero;
        }

        public void MoveTo(Vector3 dest)
        {
            if (_agent.isOnNavMesh) 
            {
                // Inspect-driven Speed Control
                _agent.speed = BaseMoveSpeed;
                _agent.SetDestination(dest);
            }
        }

        public void SprintTo(Vector3 target)
        {
            MoveTo(target);
            // Sprint is 1.4x the base speed
            float sprintMult = (_controller.config) ? _controller.config.SprintMultiplier : 1.4f;
            if (_agent != null) _agent.speed = BaseMoveSpeed * sprintMult; 
        }

        public void EnterTrapMode(Vector3 pendingDest)
        {
            IsRecoveringBall = true;
            _pendingDestination = pendingDest;
            Stop();
        }

        public void EnterNormalMode()
        {
             IsRecoveringBall = false;
             if (_agent != null && _agent.isOnNavMesh) _agent.isStopped = false;
        }
    }
}
