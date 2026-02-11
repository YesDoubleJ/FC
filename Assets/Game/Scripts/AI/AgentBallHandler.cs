using UnityEngine;
using Game.Scripts.Physics;
using Game.Scripts.Managers;
using System.Collections.Generic;

namespace Game.Scripts.AI
{
    /// <summary>
    /// Handles ball possession, dribbling physics, and kick execution.
    /// Extracted from HybridAgentController (Refactoring).
    /// </summary>
    [RequireComponent(typeof(AgentMover))]
    public class AgentBallHandler : MonoBehaviour
    {
        private struct PendingKick
        {
            public Rigidbody rb;
            public Vector3 force;
            public Vector3 torque;
        }

        [Header("Magnus Effect")]
        [Tooltip("Strength of the Magnus force.")]
        public float magnusCoefficient = 0.03f; // Preserved Tuning

        [Header("Settings")]
        public AgentBallHandlerSettings settings; // Assigned in Inspector or Loaded in Initialize

        // References
        private HybridAgentController _controller;
        private AgentMover _mover;
        private Rigidbody _cachedBallRb;
        public Rigidbody BallRb => _cachedBallRb;

        // Possession State
        private float lastClaimTime = 0f;
        private float _lastClaimLogTime = 0f; // [ADDED] Throtte Log
        private float _ignoreBallInteractionUntil = 0f;
        
        // Dribble State
        private bool _isDribbleActive = false;

        // kick State
        private Queue<PendingKick> _pendingKicks = new Queue<PendingKick>(); 
        private bool _executePendingKickInFixedUpdate;
        private Coroutine _kickTimeoutCoroutine;

        // Kick Preparation State (FixedUpdate Replacement for Coroutine)
        private bool _isPreparingKick = false;
        private float _preparingKickTimer = 0f;
        private Vector3 _kickTargetForce;
        private Vector3 _kickTargetPos;
        private GameObject _kickRecipient;

        // Optimization
        private static Rigidbody _sharedBallRb;
        private static float _lastBallSearchTime = 0f;

        public bool HasPendingKick => _pendingKicks.Count > 0;
        public bool IsInPocket { get; private set; } // Exposed state for RootState decision



        private float _lastDribbleTime;
        private float _lastDebugLogTime;

        public void Initialize(HybridAgentController controller)
        {
            _controller = controller;
            _mover = GetComponent<AgentMover>();
            
            // Auto-Load Default Settings if not assigned
            if (settings == null)
            {
                settings = Resources.Load<AgentBallHandlerSettings>("DefaultAgentBallHandlerSettings");
                if (settings == null)
                {
                    // Fallback: Create runtime instance
                    settings = ScriptableObject.CreateInstance<AgentBallHandlerSettings>();
                    Debug.LogWarning($"[AgentBallHandler] Settings not found, using Defaults.");
                }
            }
        }
        
        // ... (Skipping to ApplyDribbleConstraint replacement) ...

        private void ApplyDribbleConstraint()
        {
            var matchMgr = MatchManager.Instance;
            if (matchMgr == null || matchMgr.CurrentBallOwner != _controller) return;
            if (_cachedBallRb == null) return;
            
            // 쿨타임 및 예외 상황 체크
            if (Time.time < _ignoreBallInteractionUntil) return;
            if (HasPendingKick) return;
            
            // [FIX] Relax constraints during movement for natural physics dribble
            // (Only skip if moving REALLY fast, otherwise we need Soft Guide)
            bool isMovingFast = (_mover != null && _mover.NavAgent != null && _mover.NavAgent.velocity.magnitude > 3.0f);
            if (isMovingFast) return; 

            // High Velocity Escape (Shooting/Passing)
            if (_cachedBallRb.linearVelocity.magnitude > 8.0f) return;

            Vector3 toBall = _cachedBallRb.position - transform.position;
            toBall.y = 0; 
            float distToBall = toBall.magnitude;

            // 1. [FAIL-SAFE] Hard Reset Conditions
            // - Ball is Behind (Dot < 0)
            // - Ball is Too Far (Dist > maxTetherRadius)
            bool isBehind = Vector3.Dot(transform.forward, toBall) < 0;
            bool isTooFar = distToBall > settings.maxTetherRadius;

            if (isBehind || isTooFar)
            {
                // FORCE TELEPORT (Emergency Recovery)
                float configDist = (_controller.config) ? _controller.config.DribbleSweetSpotDist : 0.65f;
                float safeDist = Mathf.Max(configDist, 0.7f);
                
                Vector3 rescuePos = transform.position + (transform.forward * safeDist);
                rescuePos.y = Mathf.Max(_cachedBallRb.position.y, transform.position.y + 0.15f);
                
                _cachedBallRb.position = rescuePos;
                _cachedBallRb.linearVelocity = Vector3.zero; 
                _cachedBallRb.angularVelocity = Vector3.zero;
                
                // Debug.Log($"[{Time.time:F2}] [CONSTRAINT] Hard Reset (Behind/Far)");
                return;
            }

            // 2. [SOFT GUIDE] Physics Correction
            // Pull ball towards Sweet Spot without killing momentum
            float sweetDist = (_controller.config) ? _controller.config.DribbleSweetSpotDist : 0.65f;
            Vector3 sweetSpot = transform.position + (transform.forward * sweetDist);
            sweetSpot.y = _cachedBallRb.position.y;

            Vector3 errorVec = sweetSpot - _cachedBallRb.position;
            
            // Apply Spring Force
            // F = k * x (Hooke's Law approximation)
            _cachedBallRb.AddForce(errorVec * settings.softGuideStrength, ForceMode.Acceleration);
            
            // Optional: Mild Damping to prevent oscillation (only perpendicular to motion?)
            // For now, let's trust friction.
        }


        public void Tick()
        {
             // [USER REQUEST] Unified Update Loop Order
             UpdatePossessionLogic();
             
             if (_isPreparingKick)
             {
                 UpdateKickLogic();
             }
             
             // New Dribble Logic (Kick & Run)
             UpdateDribbleLogic();

             // Constraint Logic (skipped if moving fast)
             ApplyDribbleConstraint();
             
             UpdateOrbitLock(); 
             
             DebugOnBallBehavior();
        }

        private void Start()
        {
            if (_controller == null)
            {
                _controller = GetComponent<HybridAgentController>();
                Initialize(_controller); 
            }
        }

        private void LateUpdate()
        {
            // [Moved to Tick as requested]
        }

         // [NEW] Dribble Kick Logic
        private void UpdateDribbleLogic()
        {
            var matchMgr = MatchManager.Instance;
            if (matchMgr == null || matchMgr.CurrentBallOwner != _controller) return;
            if (_cachedBallRb == null) return;

            // 1. Basic Checks
            if (MatchManager.Instance.CurrentBallOwner != _controller) return;
            if (Time.time < _lastDribbleTime + settings.dribbleInterval) return;
            if (_isPreparingKick) return; // Don't dribble if kicking

            // 2. Get Speed
            float currentSpeed = (_mover != null && _mover.NavAgent != null) ? _mover.NavAgent.velocity.magnitude : 0f;
            
            // Allow dribble even at low speeds (Physics Dribble)
            if (currentSpeed < 0.1f) return;

            // 3. Check Sweet Spot (Front Cone)
            Vector3 toBall = _cachedBallRb.position - transform.position;
            float angle = Vector3.Angle(transform.forward, toBall);
            float dist = toBall.magnitude;

            // [TUNING] Anti-Collision: Kick earlier (1.3m derived from experimentation)
            // Sweet Spot: Distance < 1.3m, Angle < 45 deg
            if (angle > 45f || dist > 1.3f) return; 

            // 4. Calculate Variable Force
            // Scale force with speed ratio
            float baseSpeed = (_controller.config != null) ? _controller.config.BaseMoveSpeed : 3.5f;
            float speedRatio = Mathf.Clamp01(currentSpeed / baseSpeed);
            
            // Base Force Calculation
            float baseForce = Mathf.Lerp(settings.dribbleMinForce, settings.dribbleMaxForce, speedRatio);
            
            // Apply Global Scale
            float targetForce = baseForce * settings.DribbleForceScale;

            // [DRIBBLE-DEBUG] Detailed Calculation Log
            // Filter log to avoid spam (show every 1.0s or on significant change?) -> For now show all for debugging as requested
            // Format: [DRIBBLE-DEBUG] Speed: 9.0 | Formula: (Base:1.0 + SpeedFactor:8.0) * Scale:1.0 = 9.0 | DribbleInterval: 0.2s
            Debug.Log($"[DRIBBLE-DEBUG] Speed: {currentSpeed:F1} | Formula: (Lerp({settings.dribbleMinForce:F1}~{settings.dribbleMaxForce:F1}, {speedRatio:F2})={baseForce:F1}) * Scale:{settings.DribbleForceScale:F1} = {targetForce:F1} | Interval: {settings.dribbleInterval:F2}s");

            // 5. Execute Dribble (Kick & Loss)
            _lastDribbleTime = Time.time;
            
            _cachedBallRb.isKinematic = false;
            // Inherit velocity to keep momentum natural
            if (_mover != null) _cachedBallRb.linearVelocity = _mover.NavAgent.velocity; 
            
            _cachedBallRb.AddForce(transform.forward * targetForce, ForceMode.Impulse);
            
            // [DRIBBLE-EXEC] Execution Log
            Debug.Log($"[DRIBBLE-EXEC] Force: {targetForce:F1} | BallDist: {dist:F2}m");

            // [CRITICAL] Release Possession
            // Ball leaves foot -> Loss of Control -> Physics takes over
            MatchManager.Instance.LosePossession(_controller);
            _ignoreBallInteractionUntil = Time.time + 0.15f; 
        }

        public void FixedTick()
        {
            if (_executePendingKickInFixedUpdate)
            {
                _executePendingKickInFixedUpdate = false;
                ExecuteKickPhysics();
            }
            // Logic moved to Tick()
        }


        
        // =========================================================
        // ORBIT LOCK (Moved from AgentMover)
        // =========================================================
        private void UpdateOrbitLock()
        {
            var matchMgr = MatchManager.Instance;
            if (matchMgr == null || matchMgr.CurrentBallOwner != _controller) return;
            if (_cachedBallRb == null) return;
            
            // [추가된 안전장치] 킥이 예약되어 있거나(Pending), 킥 직후 쿨타임 중이면 오빗 로직 중단
            if (HasPendingKick || Time.time < _ignoreBallInteractionUntil) return;
            
            // Sync Ball Position with Player Rotation
            // Only apply if there was significant rotation
            if (_mover.LastRotationDelta != Quaternion.identity)
            {
                // Get ball's offset from player
                Vector3 ballOffset = _cachedBallRb.position - transform.position;
                
                // Minimum radius protection (prevent ball clipping into player)
                float minRadius = 0.5f;
                if (ballOffset.magnitude < minRadius)
                {
                     ballOffset = ballOffset.normalized * minRadius;
                }
                
                // Rotate the offset by the same amount we rotated
                Vector3 rotatedOffset = _mover.LastRotationDelta * ballOffset;
                
                // Apply the new position to ball
                Vector3 newBallPos = transform.position + rotatedOffset;
                newBallPos.y = _cachedBallRb.position.y; // Preserve Y
                
                // Use MovePosition for smooth physics update
                _cachedBallRb.MovePosition(newBallPos);
            }
        }



        // =========================================================
        // POSSESSION LOGIC
        // =========================================================
        private void UpdatePossessionLogic()
        {
            // 1. 공 찾기 (최적화된 캐싱)
            if (_sharedBallRb == null)
            {
                if (Time.time - _lastBallSearchTime > 1.0f) 
                {
                    _lastBallSearchTime = Time.time;
                    GameObject ballObj = MatchManager.Instance?.Ball;
                    if (ballObj == null) ballObj = GameObject.FindGameObjectWithTag("Ball");
                    if (ballObj == null) ballObj = GameObject.Find("Ball"); 
                    
                    if (ballObj != null) _sharedBallRb = ballObj.GetComponent<Rigidbody>();
                    else _sharedBallRb = UnityEngine.Object.FindFirstObjectByType<Game.Scripts.Physics.BallAerodynamics>()?.GetComponent<Rigidbody>();
                }
            }
            
            if (_cachedBallRb != _sharedBallRb) _cachedBallRb = _sharedBallRb;
            if (_cachedBallRb == null) return;
            
            // ▼▼▼ [핵심 수정] 킥 직후 절대 방어막 (속도 0이어도 무조건 리턴) ▼▼▼
            // 1. 쿨타임 중이면 무조건 리턴
            if (Time.time < _ignoreBallInteractionUntil) return;

            // 2. 킥이 예약된 상태(차려고 발을 드는 중)면 리턴
            if (HasPendingKick) return;

            // 3. (보조) 공이 이미 날아가고 있다면 리턴 (안전장치)
            if (_cachedBallRb.linearVelocity.magnitude > 5.0f) return;
            // ▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲

            float dist = Vector3.Distance(transform.position, _cachedBallRb.position);
            var matchMgr = MatchManager.Instance;
            if (matchMgr == null) return;

            // CLAIM POSSESSION
            if (matchMgr.CurrentBallOwner == null)
            {
                if (dist < settings.claimDistance) 
                {
                    Vector3 toBall = _cachedBallRb.position - transform.position;
                    float angle = Vector3.Angle(transform.forward, toBall);

                    if (angle < settings.inPocketAngleStationary)
                    {
                        matchMgr.SetBallOwner(_controller);
                        lastClaimTime = Time.time;
                        
                        // 로그 스팸 방지
                        if (Time.time - _lastClaimLogTime > 1.0f)
                        {
                            _lastClaimLogTime = Time.time;
                            Debug.Log($"[AgentBallHandler] {name} CLAIMED BALL. Dist: {dist:F2}, Angle: {angle:F2}");
                        }
                    }
                }
            }
            
            // LOSE POSSESSION
            if (matchMgr.CurrentBallOwner == _controller)
            {
                float gracePeriodRemaining = (lastClaimTime + settings.possessionGraceTime) - Time.time;
                if (gracePeriodRemaining > 0) return;

                if (dist > settings.losePossessionDistance)
                {
                    matchMgr.LosePossession(_controller);
                    Debug.Log($"[AgentBallHandler] {name} lost ball. Dist: {dist:F2}");
                    _cachedBallRb = null; 
                    _isDribbleActive = false;
                }
            }
        }

        // =========================================================
        // DRIBBLE LOGIC
        // =========================================================
        private void DribbleAssist()
        {
            if (_cachedBallRb == null) return;
            
            if (HasPendingKick) return; // Lockout during kick execution
            if (Time.time < _ignoreBallInteractionUntil) return; // Cooldown Lockout

            // [FIX] ORBIT DRIBBLE: Stick ball to feet during Action Rotation
            if (_mover != null && _mover.IsBusy)
            {
                PerformOrbitDribble();
                return;
            }

            var matchMgr = MatchManager.Instance; 
            if (matchMgr != null && matchMgr.CurrentBallOwner == _controller)
            {
                // Unified Velocity PID
                Vector3 agentVel = _controller.NavAgent.velocity; 
                
                // Blend in a bit of desired velocity for responsiveness
                if (_controller.NavAgent.desiredVelocity.magnitude > agentVel.magnitude)
                {
                        agentVel = Vector3.Lerp(agentVel, _controller.NavAgent.desiredVelocity, 0.3f);
                }

                // FIX: Lower threshold to ensure Dribble Logic engages immediately when moving
                bool isRotating = _mover.IsBusy;
                if (agentVel.sqrMagnitude > 0.001f || isRotating)
                {
                    ApplyMovingDribble(agentVel, isRotating);
                }
                else
                {
                    ApplyStationaryControl();
                }
            }
        }

        private bool UpdateDribbleActivation()
        {
            Vector3 forwardDir = transform.forward;
            Vector3 ballOffset = _cachedBallRb.position - transform.position;
            ballOffset.y = 0;
            float dist = ballOffset.magnitude;
            float angle = Vector3.Angle(forwardDir, ballOffset);
            
            IsInPocket = (dist < settings.inPocketDistance && angle < settings.inPocketAngleStationary);
            
            if (_isDribbleActive)
            {
                // EXIT CONDITION: Dynamic
                float currentSpeed = _mover.Speed;
                float dynamicExitDist = settings.stickyDynamicBaseDist + (currentSpeed * settings.stickyDynamicSpeedFactor);
                if (dist > dynamicExitDist) 
                {
                    _isDribbleActive = false;
                    return false;
                }
            }
            else
            {
                // ENTRY CONDITION: Ownership Dist
                if (dist < settings.dribbleEntryMaxDist)
                {
                    if (_cachedBallRb.linearVelocity.magnitude <= settings.dribbleEntryMaxBallSpeed) _isDribbleActive = true;
                    else return false;
                }
                else return false;
            }
            return true;
        }
        // [AgentBallHandler.cs] 의 ApplyMovingDribble 메서드를 통째로 교체

    private void ApplyMovingDribble(Vector3 agentVel, bool isRotating)
    {
        if (_cachedBallRb == null) return; // 안전장치

        Vector3 currentBallVel = _cachedBallRb.linearVelocity;
        Vector3 forwardDir = transform.forward;
        
        // CONFIG 로드
        float sweetSpotDist = (_controller.config) ? _controller.config.DribbleSweetSpotDist : 0.65f;
        float closeThreshold = (_controller.config) ? _controller.config.DribbleCloseThreshold : 0.45f;
        
        Vector3 toBall = _cachedBallRb.position - transform.position;
        toBall.y = 0;
        float distToBall = toBall.magnitude;
        float angleToBall = Vector3.Angle(forwardDir, toBall);

        // 1. 공이 너무 멀면 포기 (2m)
        if (distToBall > 2.0f)
        {
                _isDribbleActive = false;
                return;
        }

        // 2. [끼임 방지] 공이 너무 가까우면(다리 사이) 앞으로 밀어내기
        if (distToBall < closeThreshold)
        {
            Vector3 pushDir = forwardDir;
            // 공이 약간 옆에 있으면 그 방향으로 밀어서 자연스럽게
            if (angleToBall < 90f && distToBall > 0.1f) pushDir = toBall.normalized; 
            
            Vector3 pushForce = pushDir * settings.dribbleStaticForceMultiplier * 3.0f; // 강하게 밀기
            _cachedBallRb.AddForce(pushForce, ForceMode.Acceleration);
            return; 
        }

        // 3. [회전 보조] 선수가 돌 때 공을 옆으로 툭 쳐주기 (Tap)
        if (_mover.LastRotationDelta != Quaternion.identity)
        {
            Vector3 cross = Vector3.Cross(forwardDir, _mover.LastRotationDelta * forwardDir);
            float turnDir = (cross.y > 0) ? 1f : -1f; 
            
            float turnMag = Quaternion.Angle(Quaternion.identity, _mover.LastRotationDelta);
            if (turnMag > 1.0f)
            {
                    Vector3 tapForce = transform.right * turnDir * (turnMag * 0.8f); 
                    _cachedBallRb.AddForce(tapForce, ForceMode.Acceleration);
            }
        }

        // 4. [핵심 수정] 스윗 스팟으로 유도 (갈고리 제거됨!)
        Vector3 sweetSpot = transform.position + (forwardDir * sweetSpotDist);
        sweetSpot.y = _cachedBallRb.position.y;

        Vector3 posError = sweetSpot - _cachedBallRb.position;
        
        // [중요] 공이 등 뒤(100도 이상)에 있으면 당기는 힘을 0으로 만듦
        // 몸을 뚫고 당겨오는 현상 원천 차단
        float correctionGain = settings.correctionGainMin;
        if (angleToBall > 100f) 
        {
            correctionGain = 0f; // 뒤에 있으면 당기지 마! (Constraint나 회전에 맡김)
        }

        Vector3 correctionVel = posError * correctionGain;
        correctionVel = Vector3.ClampMagnitude(correctionVel, settings.maxCorrectionVelocity);
        
        // 목표 속도 계산
        Vector3 targetBallVel = agentVel * settings.dribbleMoveSpeedScale;
        targetBallVel += correctionVel;
        targetBallVel.y = 0;
        
        Vector3 velError = targetBallVel - currentBallVel;
        velError.y = 0;
        
        _cachedBallRb.AddForce(velError * settings.velocityGainMin, ForceMode.Acceleration);
    }
        // private void ApplyMovingDribble(Vector3 agentVel, bool isRotating)
        // {
        //     Vector3 currentBallVel = _cachedBallRb.linearVelocity;
        //     Vector3 forwardDir = transform.forward;
            
        //     // CONFIG
        //     float sweetSpotDist = (_controller.config) ? _controller.config.DribbleSweetSpotDist : 0.65f;
        //     float closeThreshold = (_controller.config) ? _controller.config.DribbleCloseThreshold : 0.45f;
            
        //     Vector3 toBall = _cachedBallRb.position - transform.position;
        //     toBall.y = 0;
        //     float distToBall = toBall.magnitude;
        //     float angleToBall = Vector3.Angle(forwardDir, toBall);

        //     // SAFETY: If ball is too far, we lost it. Don't teleport it back!
        //     if (distToBall > 2.0f)
        //     {
        //             _isDribbleActive = false;
        //             return;
        //     }

        //     // [FIX] 1. PUSH LOGIC (Prevent Clipping / Stuck)
        //     // If ball is too close (inside legs), PUSH it forward strongly.
        //     if (distToBall < closeThreshold)
        //     {
        //         // Push direction: Forward, but respecting current ball offset slightly to avoid snapping
        //         Vector3 pushDir = forwardDir;
        //         if (angleToBall < 90f && distToBall > 0.1f) pushDir = toBall.normalized; 
                
        //         // Strong Push
        //         Vector3 pushForce = pushDir * settings.dribbleStaticForceMultiplier * 2.5f; // Stronger push
        //         _cachedBallRb.AddForce(pushForce, ForceMode.Acceleration);
        //         return; // Prioritize pushing out
        //     }

        //     // [FIX] 2. TURN TAP LOGIC
        //     // If turning significantly, add tangential force to keep ball in front
        //     if (_mover.LastRotationDelta != Quaternion.identity)
        //     {
        //         // Calculate turn direction (Left or Right)
        //         Vector3 cross = Vector3.Cross(forwardDir, _mover.LastRotationDelta * forwardDir);
        //         float turnDir = (cross.y > 0) ? 1f : -1f; // Right = 1, Left = -1
                
        //         // Add tangential force (Right vector * turnDir)
        //         // Magnitude depends on turn speed
        //         float turnMag = Quaternion.Angle(Quaternion.identity, _mover.LastRotationDelta);
        //         if (turnMag > 2.0f)
        //         {
        //              Vector3 tapForce = transform.right * turnDir * (turnMag * 0.5f); 
        //              _cachedBallRb.AddForce(tapForce, ForceMode.Acceleration);
        //         }
        //     }

        //     // [FIX] 3. SWEET SPOT GUIDANCE
        //     // Target Position: Always IN FRONT (Forward * SweetSpot)
        //     Vector3 sweetSpot = transform.position + (forwardDir * sweetSpotDist);
        //     sweetSpot.y = _cachedBallRb.position.y;

        //     Vector3 posError = sweetSpot - _cachedBallRb.position;
            
        //     // Calculate Correction Force
        //     // If ball is Behind (Angle > 100), apply stronger "Hook" force
        //     float angleFactor = Mathf.Clamp01(angleToBall / 90f); 
        //     float correctionGain = Mathf.Lerp(settings.correctionGainMin, settings.correctionGainMax, angleFactor);
            
        //     if (angleToBall > 100f) correctionGain *= 2.0f; // Hook effect

        //     Vector3 correctionVel = posError * correctionGain;
        //     correctionVel = Vector3.ClampMagnitude(correctionVel, settings.maxCorrectionVelocity);
            
        //     // Base Velocity Match (Agent Speed)
        //     Vector3 targetBallVel = agentVel * settings.dribbleMoveSpeedScale;
            
        //     // Blend Correction
        //     targetBallVel += correctionVel;
        //     targetBallVel.y = 0;
            
        //     Vector3 velError = targetBallVel - currentBallVel;
        //     velError.y = 0;
            
        //     float velocityGain = Mathf.Lerp(settings.velocityGainMin, settings.velocityGainMax, angleFactor);
        //     _cachedBallRb.AddForce(velError * velocityGain, ForceMode.Acceleration);
        // }

            

        private void PerformOrbitDribble()
        {
             // 1. Distance Safety Check
             float distToBall = Vector3.Distance(transform.position, _cachedBallRb.position);
             if (distToBall > 2.0f)
             {
                 var matchMgr = MatchManager.Instance;
                 if (matchMgr != null) matchMgr.LosePossession(_controller);
                 return;
             }

             float sweetSpotDist = (_controller.config) ? _controller.config.DribbleSweetSpotDist : 0.65f;
             Vector3 targetPos = transform.position + (transform.forward * sweetSpotDist);
             targetPos.y = _cachedBallRb.position.y;

             // [FIX] Hard Lock: Synchronize ball position with rotation (Zero Lag)
             // User Request: Direct Transform Set if aiming/rotating
             _cachedBallRb.position = targetPos; // Hard Set
             _cachedBallRb.rotation = Quaternion.identity; // Optional clean
             
             // 3. Reset Physics
             _cachedBallRb.linearVelocity = Vector3.zero;
             _cachedBallRb.angularVelocity = Vector3.zero;
        }

        private void ApplyStationaryControl()
        {
            // [FIX] STATIONARY KEEP-IN-FRONT
            // Always guide ball to sweet spot even when standing still
            float sweetSpotDist = (_controller.config) ? _controller.config.DribbleSweetSpotDist : 0.65f;
            
            Vector3 targetPos = transform.position + (transform.forward * sweetSpotDist);
            Vector3 toBall = _cachedBallRb.position - transform.position;
            
            // 1. Angular Correction (If ball is behind/side, pull it front)
            // 2. Distance Correction (If ball is too far/close, nudge it)

            Vector3 posError = targetPos - _cachedBallRb.position;
            posError.y = 0;
            
            // Damping
            Vector3 ballVel = _cachedBallRb.linearVelocity;
            ballVel.y = 0;
            _cachedBallRb.AddForce(-ballVel * settings.stationaryDamp, ForceMode.Acceleration);

            // Guidance
            if (posError.magnitude > 0.05f)
            {
               _cachedBallRb.AddForce(posError * settings.stationaryNudgeForce, ForceMode.Acceleration);
            }
        }

        // =========================================================
        // PASS LOGIC
        // =========================================================
        public void Pass(GameObject targetTeammate)
        {
             // Optimization: Use Cached Ball RB
            if (_cachedBallRb != null && targetTeammate != null)
            {
                var targetAgent = targetTeammate.GetComponent<HybridAgentController>();
                if (targetAgent != null)
                {
                    targetAgent.receivedBallFrom = _controller;
                    targetAgent.ballReceivedTime = Time.time;
                }

                // PREDICTIVE PASS
                Vector3 targetPos = targetTeammate.transform.position;
                if (targetAgent != null && targetAgent.VelocityRb.linearVelocity.sqrMagnitude > 1.0f)
                {
                    float distance = Vector3.Distance(transform.position, targetPos);
                    float estimatedBallSpeed = settings.predictedBallSpeed; 
                    float travelTime = distance / estimatedBallSpeed;
                    travelTime = Mathf.Clamp(travelTime, 0f, settings.maxPredictionTime);
                    
                    Vector3 leadOffset = targetAgent.VelocityRb.linearVelocity * travelTime;
                    targetPos += leadOffset;
                    
                    targetPos.x = Mathf.Clamp(targetPos.x, -settings.fieldHalfWidth, settings.fieldHalfWidth);
                    targetPos.z = Mathf.Clamp(targetPos.z, -settings.fieldHalfLength, settings.fieldHalfLength);
                }
                
                PassToPosition(targetPos, targetTeammate);
            }
        }

        public void PassToPosition(Vector3 targetPos, GameObject recipient = null)
        {
            if (_cachedBallRb == null) return;

            float passingStat = _controller.Stats.GetStat(Data.StatType.Passing);
            
            // 1. Calculate Error & Power
            Vector3 direction = (targetPos - transform.position).normalized;
            
            // [FIX] Kick-Off Logic: Absolute accuracy & Allow Backpass
            bool isKickOff = MatchManager.Instance && MatchManager.Instance.IsKickOffFirstPass;
            
            float errorAngle = isKickOff ? 0f : Mathf.Lerp(settings.passErrorBaseAngle, 0f, passingStat / 100f);
            Quaternion errorRot = Quaternion.Euler(UnityEngine.Random.Range(-errorAngle, errorAngle), UnityEngine.Random.Range(-errorAngle, errorAngle), 0);
            Vector3 finalDirection = errorRot * direction;
            
            float dist = Vector3.Distance(transform.position, targetPos);
            float basePower = settings.passPowerBase + (dist * settings.passPowerDistFactor) + (passingStat * 0.05f);
            float clampedPower = Mathf.Clamp(basePower, settings.passPowerMin, settings.passPowerMax);
            float finalPower = clampedPower * settings.globalKickPowerScale;
            
            // [KICK-DEBUG] Pass Force Breakdown
            // Format: [KICK-DEBUG] PASS | Dist: 10.5m | Formula: (Base:8 + Dist:10.5*0.5 + Stat:2) * Scale:1.0 = 15.25
            Debug.Log($"[KICK-DEBUG] PASS | Dist: {dist:F1}m | Formula: ({settings.passPowerBase:F1} + {dist:F1}*{settings.passPowerDistFactor:F1} + {passingStat}*0.05) [Clamp: {clampedPower:F1}] * Scale:{settings.globalKickPowerScale:F1} = {finalPower:F1}");

            Vector3 force = finalDirection * finalPower;

            // [FIX] Lift ball slightly to avoid friction on grass/feet
            if (isKickOff) force += Vector3.up * 0.5f;

            // 3. Start Kick Sequence (Non-blocking)
            _isPreparingKick = true;
            _preparingKickTimer = 0f;
            _kickTargetForce = force;
            _kickTargetPos = targetPos;
            _kickRecipient = recipient;
            _isDribbleActive = false;
        }

        public void Shoot(Vector3 targetPos)
        {
            if (_cachedBallRb == null) return;

            float shootingStat = _controller.Stats.GetStat(Data.StatType.Shooting);
            
            // 1. Calculate Error & Power (Using SHOOTING Settings)
            Vector3 direction = (targetPos - transform.position).normalized;
            
            // Error based on Shooting Stat
            float errorAngle = Mathf.Lerp(settings.shootErrorBaseAngle, 0f, shootingStat / 100f);
            Quaternion errorRot = Quaternion.Euler(UnityEngine.Random.Range(-errorAngle, errorAngle), UnityEngine.Random.Range(-errorAngle, errorAngle), 0);
            Vector3 finalDirection = errorRot * direction;
            
            float dist = Vector3.Distance(transform.position, targetPos);
            
            // Power Calculation
            float basePower = settings.shootPowerBase + (dist * settings.shootPowerDistFactor) + (shootingStat * 0.1f);
            float clampedPower = Mathf.Clamp(basePower, settings.shootPowerMin, settings.shootPowerMax);
            float finalPower = clampedPower * settings.globalKickPowerScale;

            // [KICK-DEBUG] Shoot Force Breakdown
            Debug.Log($"[KICK-DEBUG] SHOOT | Dist: {dist:F1}m | Formula: ({settings.shootPowerBase:F1} + {dist:F1}*{settings.shootPowerDistFactor:F1} + {shootingStat}*0.1) [Clamp: {clampedPower:F1}] * Scale:{settings.globalKickPowerScale:F1} = {finalPower:F1}");

            Vector3 force = finalDirection * finalPower;

            // 2. Start Kick Sequence (Non-blocking)
            _isPreparingKick = true;
            _preparingKickTimer = 0f;
            _kickTargetForce = force;
            _kickTargetPos = targetPos;
            _kickRecipient = null; 
            _isDribbleActive = false;
        }

        private void UpdateKickLogic()
        {
            if (_cachedBallRb == null) 
            {
                _isPreparingKick = false;
                return;
            }

            // [NEW] Active Rotation towards Target (High Speed)
            if (_mover != null)
            {
                 _mover.SetTargetRotation(_kickTargetPos, 2.0f);
            }

            // [NEW] Tangential Force for Rotation (Help ball follow turn)
            if (_mover != null && _mover.LastRotationDelta != Quaternion.identity)
            {
                Vector3 cross = Vector3.Cross(transform.forward, _mover.LastRotationDelta * transform.forward);
                float turnDir = (cross.y > 0) ? 1f : -1f;
                float turnMag = Quaternion.Angle(Quaternion.identity, _mover.LastRotationDelta);
                
                if (turnMag > 0.5f)
                {
                     Vector3 tanForce = transform.right * turnDir * (turnMag * 0.5f); 
                     _cachedBallRb.AddForce(tanForce, ForceMode.Acceleration);
                }
            }

            float timeout = settings.passAlignTimeout;
            
            // Check Ball Alignment
            Vector3 toBall = _cachedBallRb.position - transform.position;
            float angleToBall = Vector3.Angle(transform.forward, toBall);
            float distToBall = toBall.magnitude;

            // SAFETY: Abort if ball lost
            if (distToBall > settings.kickAbortDistance) 
            {
                Debug.Log($"<color=red>{name} Aborting Kick: Ball Lost ({distToBall:F2}m)!</color>");
                _isPreparingKick = false;
                return;
            }

            // Optimal Kick Position: Front (< 20deg) AND Close (< 0.7m)
            bool isAligned = (angleToBall < settings.kickAlignmentAngle && distToBall < settings.kickAlignmentDist);
            bool isTimedOut = (_preparingKickTimer > timeout);

            if (isAligned || isTimedOut)
            {
                if (isTimedOut)
                {
                     Debug.LogWarning($"{name} Kick Preparation TIMEOUT. Forcing Kick despite existing alignment error.");
                }

                // EXECUTE KICK
                if (_kickRecipient != null)
                {
                     var recipientAgent = _kickRecipient.GetComponent<HybridAgentController>();
                     recipientAgent?.NotifyIncomingPass(_kickTargetPos);
                }
                
                _controller.NotifyActionExecution(); 

                // Queue the Kick
                _pendingKicks.Enqueue(new PendingKick { rb = _cachedBallRb, force = _kickTargetForce, torque = Vector3.zero });
                _isDribbleActive = false;
                _isPreparingKick = false; // Reset State

                if (_controller.AnimController != null)
                {
                     _controller.AnimController.PlayPass();
                     string recipientName = _kickRecipient != null ? _kickRecipient.name : $"Space ({_kickTargetPos})";
                     Debug.Log($"{name} PASS to {recipientName}!");
                     Game.Scripts.UI.ActionLogDisplay.AddLog($"PASS: {name} -> {recipientName}");
                }
                
                ExecutePendingKick();
                return;
            }

            // FORCEFUL ALIGNMENT (Orbit/Pull)
            // "Drag" the ball to the front sweet spot
            Vector3 sweetSpot = transform.position + transform.forward * settings.passAlignSweetSpot;
            sweetSpot.y = _cachedBallRb.position.y;
            
            // [FIX] Sync Velocity with Agent (Prevent Heavy Feeling)
            Vector3 agentVel = (_mover != null && _mover.NavAgent != null) ? _mover.NavAgent.velocity : Vector3.zero;
            
            // maintain slight pull towards sweet spot
            Vector3 diff = sweetSpot - _cachedBallRb.position;
            Vector3 pullVel = diff * 5.0f; 

            _cachedBallRb.linearVelocity = agentVel + pullVel;
            _cachedBallRb.angularVelocity = Vector3.zero;

            _preparingKickTimer += Time.fixedDeltaTime;
        }

        public void HighClearanceKick(Vector3 dir)
        {
            var ballRb = FindFirstObjectByType<BallAerodynamics>()?.GetComponent<Rigidbody>();
            if (ballRb == null) return;
            
            // High Arc: 45 degrees up
            Vector3 forceDir = (dir.normalized + Vector3.up).normalized; 
            float power = settings.highClearancePower; 
            
            _pendingKicks.Enqueue(new PendingKick { rb = ballRb, force = forceDir * power, torque = Vector3.zero });
            _isDribbleActive = false;

            ExecutePendingKick();
            _controller.NotifyActionExecution();
            
            _controller.AnimController?.PlayPass(); 
        }

        // =========================================================
        // KICK LOGIC
        // =========================================================
        public void CurvedKick(Vector3 force, Vector3 torque)
        {
            if (_cachedBallRb != null)
            {
                _pendingKicks.Enqueue(new PendingKick { rb = _cachedBallRb, force = force, torque = torque });
                _isDribbleActive = false;
                
                // Timeout Failsafe
                if (_kickTimeoutCoroutine != null) StopCoroutine(_kickTimeoutCoroutine);
                _kickTimeoutCoroutine = StartCoroutine(KickTimeoutRoutine());
            }
        }

        public void ExecutePendingKick()
        {
            if (_kickTimeoutCoroutine != null) StopCoroutine(_kickTimeoutCoroutine);
            
            if (_pendingKicks.Count > 0)
            {
                _executePendingKickInFixedUpdate = true;
            }
        }
        
        private void ExecuteKickPhysics()
        {
             // Process Queue
             while (_pendingKicks.Count > 0)
             {
                 var kick = _pendingKicks.Dequeue();
                 if (kick.rb != null)
                 {
                     float distToBall = Vector3.Distance(transform.position, kick.rb.position);
                     
                     // [거리 체크 완화] 공이 조금 멀어져도 킥 허용 (기존 2.5m -> 3.0m)
                     if (distToBall > settings.kickMissDistance + 0.5f) 
                     {
                         Debug.Log($"<color=red>{name} Missed Kick! Ball too far ({distToBall:F2}m).</color>");
                         continue; 
                     }
                     
                     // 1. [핵심] 발 앞 정렬 (공 꺼내기)
                     // 공이 몸 안쪽(0.6m 이내)에 있다면 발 앞(0.7m)으로 강제 이동
                     if (distToBall < 0.6f)
                     {
                         Vector3 safeKickPos = transform.position + (transform.forward * 0.7f);
                         safeKickPos.y = kick.rb.position.y; // 높이 유지 (또는 0.15f로 살짝 띄우기)
                         kick.rb.position = safeKickPos;
                         Debug.Log($"[KICK-FIX] Ball clipped inside player. Teleported to {safeKickPos}");
                     }

                     // 2. [핵심] 자가 충돌 무시 (발에 걸림 방지)
                     Collider myCol = GetComponent<Collider>();
                     Collider ballCol = kick.rb.GetComponent<Collider>();
                     if (myCol != null && ballCol != null)
                     {
                         UnityEngine.Physics.IgnoreCollision(myCol, ballCol, true);
                         // 0.2초 뒤에 다시 충돌 켜기
                         StartCoroutine(ResetCollisionRoutine(myCol, ballCol, 0.2f));
                     }

                     kick.rb.isKinematic = false; // 이게 없어서 공이 안 나갔던 겁니다!

                     // 3. 물리력 초기화 및 적용
                     kick.rb.linearVelocity = Vector3.zero;
                     kick.rb.angularVelocity = Vector3.zero;
                     
                     // [디버그]
                     Debug.Log($"[KICK-EXEC] {name} Kicked! Force: {kick.force.magnitude:F1}, Mode: VelocityChange | Scale: {settings.globalKickPowerScale:F1}");
                     
                     kick.rb.AddForce(kick.force, ForceMode.VelocityChange); // 속도 모드 사용
                     if (kick.torque != Vector3.zero) kick.rb.AddTorque(kick.torque, ForceMode.VelocityChange);
                     
                     // 4. 마무리
                     // 4. 마무리 (Possession Release)
                     // [핵심 추가] 킥을 했으면 더 이상 내 공이 아님!
                     
                     // 1. 소유권 해제 알림
                     MatchManager.Instance.LosePossession(_controller);
                     
                     // 2. 드리블 모드 해제
                     _isDribbleActive = false;
                     
                     // 3. 재소유 쿨타임 (공이 발을 떠나는 동안 다시 잡지 않도록)
                     // (settings.kickCooldown is 0.5f by default)
                     _ignoreBallInteractionUntil = Time.time + settings.kickCooldown; 

                     // 4. 로그 명시
                     Debug.Log($"[KICK-EXEC] {name} Kicked! Relinquishing Possession.");
                     
                     BallAerodynamics aero = kick.rb.GetComponent<BallAerodynamics>();
                     if (aero != null) aero.ResetAerodynamics(); 
                     
                     if (MatchManager.Instance.IsKickOffFirstPass)
                     {
                         MatchManager.Instance.IsKickOffFirstPass = false;
                     }
                 }
             }
        }

        // [새로 추가] 충돌 복구 코루틴
        private System.Collections.IEnumerator ResetCollisionRoutine(Collider c1, Collider c2, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (c1 != null && c2 != null)
            {
                UnityEngine.Physics.IgnoreCollision(c1, c2, false);
            }
        }

        private System.Collections.IEnumerator KickTimeoutRoutine()
        {
            yield return new WaitForSeconds(1.0f);
            if (_pendingKicks.Count > 0)
            {
                _pendingKicks.Clear();
                Debug.LogWarning($"{name}: Kick execution TIMED OUT");
            }
        }
        
        public bool CanKick()
        {
            if (_pendingKicks.Count > 0 || _cachedBallRb == null) return false;
            
            Vector3 toBall = _cachedBallRb.position - transform.position;
            float angle = Vector3.Angle(transform.forward, toBall);
            
            if (angle > settings.canKickAngle) return false;
            if (toBall.magnitude > settings.canKickDist) return false;

            return true;
        }
        
        public void OnTackled(Vector3 tacklerPos, float power)
        {
            if (_cachedBallRb == null) return;

            // 1. Lose Possession Immediately
            var matchMgr = MatchManager.Instance;
            if (matchMgr != null) matchMgr.LosePossession(_controller);
            
            _isDribbleActive = false;
            
            // 2. Stun: 1.5s Look Lockout
            _ignoreBallInteractionUntil = Time.time + 1.5f; 

            // 3. Fumble Physics (Pop Up & Away)
            Vector3 fumbleDir = (transform.position - tacklerPos).normalized;
            fumbleDir += Vector3.up * 0.4f; // Lift it up
            fumbleDir.Normalize();

            _cachedBallRb.isKinematic = false;
            _cachedBallRb.linearVelocity = Vector3.zero; // Reset velocity
            _cachedBallRb.AddForce(fumbleDir * power, ForceMode.Impulse); 

            Debug.Log($"[TACKLE-IMPACT] {name} fumbled ball! Stunned for 1.5s. Power: {power}");
        }

        public void ResetState()
        {
            _isDribbleActive = false;
            _isPreparingKick = false;
            _preparingKickTimer = 0f;
            _pendingKicks.Clear();
             if (_kickTimeoutCoroutine != null) StopCoroutine(_kickTimeoutCoroutine);
        }
        // [AgentBallHandler.cs] 맨 아래 부분 교체

        private void OnDrawGizmos()
        {
            if (settings == null) return;

            // 1. 스윗 스팟 (초록색 공) - 크기를 0.1 -> 0.3으로 키우고 Solid로 변경
            float sweetSpotDist = (_controller && _controller.config) ? _controller.config.DribbleSweetSpotDist : 0.65f;
            Vector3 sweetSpot = transform.position + (transform.forward * sweetSpotDist);
            
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(sweetSpot, 0.3f); // WireSphere -> Sphere (잘 보이게)
            Gizmos.DrawLine(transform.position, sweetSpot); // 내 몸에서 공 위치까지 선 긋기

            // 2. 45도 제한 구역 (빨간 선)
            Vector3 leftBoundary = Quaternion.Euler(0, -45, 0) * transform.forward;
            Vector3 rightBoundary = Quaternion.Euler(0, 45, 0) * transform.forward;
            Gizmos.color = Color.red;
            Gizmos.DrawRay(transform.position, leftBoundary * 2.0f);
            Gizmos.DrawRay(transform.position, rightBoundary * 2.0f);

            // 3. 실제 공 위치 (파란 선)
            if (_cachedBallRb != null)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawLine(transform.position, _cachedBallRb.position);
                Gizmos.DrawWireSphere(_cachedBallRb.position, 0.3f); // 공 위치에도 표시
            }
        }

        private void DebugOnBallBehavior()
        {
             // 1. Check Owner
             var matchMgr = MatchManager.Instance;
             if (matchMgr == null || matchMgr.CurrentBallOwner != _controller) return;
             if (_cachedBallRb == null) return;

             // 2. Throttle (0.2s)
             if (Time.time < _lastDebugLogTime + 0.2f) return;
             _lastDebugLogTime = Time.time;

             // 3. Gather Data
             float speed = (_mover != null && _mover.NavAgent != null) ? _mover.NavAgent.velocity.magnitude : 0f;
             float ballDist = Vector3.Distance(transform.position, _cachedBallRb.position);
             bool isMovingFast = speed > 2.0f;
             
             // Constraint Active Condition (Inverse of Relax Condition)
             bool constraintActive = !isMovingFast && !HasPendingKick && Time.time >= _ignoreBallInteractionUntil;
             
             string modeStr = isMovingFast ? "RUNNING (Constraint: OFF)" : "CONTROL (Constraint: ON)";
             if (HasPendingKick) modeStr = "KICKING (Constraint: OFF)";
             else if (Time.time < _ignoreBallInteractionUntil) modeStr = "STUN/COOLDOWN (Constraint: OFF)";

             // 4. Log
             Debug.Log($"[ON-BALL] {name} | Speed: {speed:F1} | BallDist: {ballDist:F2}m | Mode: {modeStr}");
        }
    }
}
