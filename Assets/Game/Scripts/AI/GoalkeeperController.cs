using UnityEngine;
using Game.Scripts.AI.HFSM;

namespace Game.Scripts.AI
{
    public class GoalkeeperController : HybridAgentController
    {
        [Header("Goalkeeper Settings")]
        public GoalkeeperSettings gkSettings;
        public Transform goalCenter;   // The point the GK protects
        
        // Runtime Cache
        public float CurrentGoalLineZ { get; private set; }

        // [수정 1] Backing Field 추가 (실제 데이터를 담을 변수)
        private State _gkStateGuarding;

        // [수정 2] 프로퍼티 변경 (Lazy Initialization: 필요할 때 없으면 생성)
        public State GKState_Guarding 
        { 
            get 
            {
                // 아직 생성되지 않았다면?
                if (_gkStateGuarding == null)
                {
                    // StateMachine이 준비되지 않았다면 null 반환 (안전장치)
                    if (StateMachine == null) return null;

                    // 여기서 생성! (이제 null일 수가 없음)
                    _gkStateGuarding = new GKState_Guarding(this, StateMachine);
                }
                return _gkStateGuarding;
            }
        }

        protected override void Start()
        {
            base.Start(); 
            
            if (gkSettings == null)
            {
                gkSettings = Resources.Load<GoalkeeperSettings>("DefaultGoalkeeperSettings");
                if (gkSettings == null)
                    Debug.LogWarning($"{name}: GoalkeeperSettings missing!");
            }

            // TEAM MIRROR FIX:
            float baseZ = gkSettings ? gkSettings.goalLineZ : 47f;
            CurrentGoalLineZ = (TeamID == Game.Scripts.Data.Team.Away) ? baseZ : -baseZ;
            
            // [참고] Start에서는 별도로 초기화 코드를 넣지 않아도 됩니다.
            // 위 프로퍼티(GKState_Guarding)를 사용하는 순간 자동으로 생성되기 때문입니다.
        }

        protected override void Update()
        {
            base.Update(); // Runs StateMachine.Update()
        }

        // 1. Z-Limit Check
        public override void SetDestination(Vector3 target)
        {
            base.SetDestination(target);
        }

        // 2. Logic Override
        protected override void UpdateStateBasedOnPossession()
        {
            var matchMgr = Game.Scripts.Managers.MatchManager.Instance;
            if (matchMgr == null) return;
            
            // Only play during 'Playing'
            if (matchMgr.CurrentState != Game.Scripts.Managers.MatchState.Playing) return;

            var owner = matchMgr.CurrentBallOwner;
            
            if (owner == this)
            {
                // I Have Ball -> Act like Field Player (Dribble/Pass)
                if (StateMachine.CurrentState != AttackingState)
                {
                    StateMachine.ChangeState(AttackingState);
                }
            }
            else
            {
                // No Ball or Opponent Has Ball -> GUARD GOAL
                
                // [수정 3] null 체크 및 상태 변경
                // 프로퍼티 접근(GKState_Guarding) 시점에 자동으로 객체가 생성됩니다.
                if (GKState_Guarding != null && StateMachine.CurrentState != GKState_Guarding)
                {
                    StateMachine.ChangeState(GKState_Guarding);
                }
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.gameObject.name.Contains("Ball") || collision.gameObject.CompareTag("Ball"))
            {
                if (Game.Scripts.Managers.MatchManager.Instance.CurrentState == Game.Scripts.Managers.MatchState.Playing)
                {
                    // If guarding, this is a save
                    Game.Scripts.Managers.MatchManager.Instance.OnSave();
                }
            }
        }
    }

    // --- Custom State Classes ---

    public class GKState_Guarding : State
    {
        private GoalkeeperController _gk;
        private Transform _ball;
        private Rigidbody _ballRb; // Cached RB

        public GKState_Guarding(GoalkeeperController gk, StateMachine stateMachine) : base(gk, stateMachine)
        {
            _gk = gk;
        }

        public override void Enter()
        {
            // Find Ball
            var ballObj = Game.Scripts.Managers.MatchManager.Instance?.Ball;
            if (ballObj == null) ballObj = GameObject.FindGameObjectWithTag("Ball");
            
            if (ballObj != null) 
            {
                _ball = ballObj.transform;
                _ballRb = ballObj.GetComponent<Rigidbody>();
            }
            
            _gk.ResetPath();
        }

        public override void Execute()
        {
            if (_ball == null) return;

            float distToBall = Vector3.Distance(_gk.transform.position, _ball.position);
            float ballSpeed = _ballRb != null ? _ballRb.linearVelocity.magnitude : 0f;

            // Settings
            var settings = _gk.gkSettings;
            float dangerSpeed = settings ? settings.dangerousShotSpeedThreshold : 8.0f;
            float dangerTime = settings ? settings.dangerousShotTimeThreshold : 2.5f;

            // SMART SAVE LOGIC (User Req: Don't be a scarecrow)
            bool isDangerousShot = false;
            Vector3 interceptPos = Vector3.zero;

            // 1. Check Trajectory (Is it heading to goal?)
            if (ballSpeed > dangerSpeed) // Fast ball
            {
                // Simple prediction: Is velocity pointing to goal?
                Vector3 ballVel = _ballRb.linearVelocity;
                Vector3 toGoal = (_gk.goalCenter.position - _ball.position).normalized;
                float dot = Vector3.Dot(ballVel.normalized, toGoal);
                
                if (dot > 0.8f) // Heading roughly towards goal
                {
                    // Calculate Time to Impact
                    float timeToGoal = distToBall / ballSpeed;
                    if (timeToGoal < dangerTime) // Arriving soon -> DANGER
                    {
                        isDangerousShot = true;
                        // Predict position at GK's Z-line
                        // Z_gk = Z_ball + Vz * t
                        // t = (Z_gk - Z_ball) / Vz
                        float vz = ballVel.z;
                        if (Mathf.Abs(vz) > 0.1f)
                        {
                            // Derived Goal Line Z
                            float myGoalLineZ = _gk.CurrentGoalLineZ;

                            float t_intercept = (myGoalLineZ - _ball.position.z) / vz;
                            if (t_intercept > 0 && t_intercept < 2.5f)
                            {
                                interceptPos = _ball.position + ballVel * t_intercept;
                                interceptPos.y = _gk.transform.position.y; // Keep grounded (Jump logic separate)
                            }
                            else isDangerousShot = false; // Going away or too slow
                        }
                    }
                }
            }

            // 2. Fallback: Close Range Emergency (< 5m & Moving)
            if (!isDangerousShot && distToBall < 5.0f && ballSpeed > 3.0f)
            {
                 isDangerousShot = true;
                 interceptPos = _ball.position + (_ballRb.linearVelocity * 0.2f);
            }
            
            if (isDangerousShot)
            {
                // BOOST Stats for Reaction
                if (_gk.NavAgent != null)
                {
                    _gk.NavAgent.speed = settings ? settings.saveReactionSpeed : 7.5f; 
                    _gk.NavAgent.acceleration = settings ? settings.saveReactionAccel : 45f; 
                    _gk.NavAgent.angularSpeed = 540f; 
                }
                
                // Safety Clamp Intercept (Don't run out of stadium)
                if (interceptPos == Vector3.zero) interceptPos = _ball.position;
                
                _gk.SetDestination(interceptPos);
                _gk.transform.LookAt(new Vector3(_ball.position.x, _gk.transform.position.y, _ball.position.z));
                return; // Override normal positioning
            }
            else
            {
                // Normal Mode: Reset Stats
                if (_gk.NavAgent != null)
                {
                    // Slightly faster base speed to reposition (User Req: Better positioning)
                    _gk.NavAgent.speed = settings ? settings.normalPositionSpeed : 4.5f; 
                    _gk.NavAgent.acceleration = settings ? settings.normalPositionAccel : 10f;
                }
            }

            // Positioning Logic:
            // Stay on the line between Ball and GoalCenter.
            // But don't go too far out. Max distance from GoalCenter?
            
            // NEW POSITIONING LOGIC: BISECT ANGLE & COVER ZONES
            
            Vector3 goalPos = _gk.goalCenter != null ? _gk.goalCenter.position : new Vector3(0, 0, _gk.CurrentGoalLineZ);
            Vector3 ballPos = _ball.position;

            // 1. Calculate base vector from Goal to Ball
            Vector3 goalToBall = ballPos - goalPos;
            Vector3 goalToBallDir = goalToBall.normalized;
            
            // 2. Determine optimal distance from goal lines
            // INCREASED RANGE: 2m to 8m to be clearly visible
            float standOutDist = 3.0f; 
            
            // Dynamic Rushing:
            // If ball is far (>30m), stay at 5m (Was 4m)
            // If ball is mid (15-30m), come out to 12m (Was 7m) - Sweeper Keeperish!
            // If ball is close (<12m), retreat to 3m (Reaction Save)
            
            // Dynamic Rushing:
            float farDist = settings ? settings.positioningFarDist : 5.0f;
            float midDist = settings ? settings.positioningMidDist : 12.0f;
            float closeDist = settings ? settings.positioningCloseDist : 3.0f;

            if (distToBall > 30f) standOutDist = farDist;
            else if (distToBall > 12f) standOutDist = midDist; 
            else standOutDist = closeDist;
            
            // 3. Bisect logic
            Vector3 targetPos = goalPos + (goalToBallDir * standOutDist);
            
            // 4. Near-Post Bias
            float angleFactor = Mathf.Abs(goalToBallDir.x); 
            if (angleFactor > 0.4f)
            {
                float shift = Mathf.Sign(ballPos.x) * 1.5f; // Increased shit
                targetPos.x += shift;
            }

            // 5. Clamp X to Goal Width (Generous)
            targetPos.x = Mathf.Clamp(targetPos.x, -7.0f, 7.0f);
            
            // 6. RELAXED Z Clamp
            float lineZ = _gk.CurrentGoalLineZ;
            // CurrentGoalLineZ is signed (-47 or 47).
            // We want to limit Z depending on side.
            if (_gk.TeamID == Game.Scripts.Data.Team.Home)
            {
                // Defending -47. Don't go below it.
                if (targetPos.z < lineZ) targetPos.z = lineZ;
            }
            else
            {
                // Defending +47. Don't go above it.
                if (targetPos.z > lineZ) targetPos.z = lineZ;
            }

            _gk.SetDestination(targetPos);
            
            // Face Ball
            _gk.transform.LookAt(new Vector3(ballPos.x, _gk.transform.position.y, ballPos.z));
            
            // Look for Save/Catch (Collision handles logic, but maybe specific animation trigger?)
        }

    }
}
