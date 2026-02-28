using UnityEngine;
using Game.Scripts.Data;
using Game.Scripts.Tactics;
using Game.Scripts.Managers;

namespace Game.Scripts.AI.Tactics
{
    /// <summary>
    /// Handles off-the-ball positioning and support logic for attacking players.
    /// Extracted from RootState_Attacking to separate positioning concerns.
    /// </summary>
    public class AttackingTactics
    {
        private Vector3 _goalPosition;

        public AttackingTactics(Vector3 goalPosition)
        {
            _goalPosition = goalPosition;
        }

        // =========================================================
        // PUBLIC API
        // =========================================================
        public Vector3 GetSupportPosition(HybridAgentController agent, Vector3 ballPos)
        {
            Vector3 targetPos = GetGeometricSupportSpot(agent, ballPos);

            // [NEW] OPEN SPACE FINDING (Raycast Check)
            // If the direct line from Ball to TargetPos is blocked by an enemy, shift laterally.
            Vector3 toTarget = targetPos - ballPos;
            if (UnityEngine.Physics.Raycast(ballPos + Vector3.up * 0.5f, toTarget.normalized, out RaycastHit hit, toTarget.magnitude, LayerMask.GetMask("Player")))
            {
                 var blocker = hit.collider.GetComponent<HybridAgentController>();
                 if (blocker != null && blocker.TeamID != agent.TeamID)
                 {
                     // BLOCKED! Shift position.
                     // Try moving 3m Left or Right relative to the passing lane
                     Vector3 right = Vector3.Cross(Vector3.up, toTarget.normalized);
                     
                     // Heuristic: Shift towards open field (or just try random side)
                     // Simple: Shift 3m Right
                     targetPos += right * 3.0f;
                 }
            }

            // [추가된 로직] 공(동료)과의 최소 안전 거리 확보 (Personal Space)
            targetPos = ApplySeparation(agent, targetPos, ballPos);

            return ClampToFieldBounds(targetPos, agent);
        }

        public Vector3 GetOptimalSupportSpot(HybridAgentController agent)
        {
            var matchMgr = MatchManager.Instance;
            if (matchMgr == null || matchMgr.Ball == null) return agent.transform.position;

            Vector3 ballPos = matchMgr.Ball.transform.position;
            
            // 1. Get Base Geometric Target (Traditional logic, now with Phase integration)
            Vector3 baseTarget = GetSupportPosition(agent, ballPos);

            // 2. Generate Local Candidate Points
            // Pitch Control Sampling (grid-less)
            int sampleCount = 8;
            float sampleRadius = 5.0f;
            
            // [MODIFIED] Phase 4: Center search around Formation Anchor instead of completely tactical target
            // This prevents players from wandering too far from their designated structure.
            Vector3 searchCenter = baseTarget;
            if (agent.formationManager != null)
            {
                searchCenter = agent.formationManager.GetAnchorPosition(agent.assignedPosition, agent.TeamID);
                
                // Maintain the line shift with the ball
                float shiftZ = (ballPos.z - searchCenter.z) * 0.45f; 
                searchCenter.z += shiftZ;
                float shiftX = (ballPos.x - searchCenter.x) * 0.2f;
                searchCenter.x += shiftX;

                // Adjust sample radius based on AttackingWidth config
                if (agent.TacticsConfig != null)
                {
                    if (agent.TacticsConfig.InPossession.AttackingWidth == Game.Scripts.Tactics.Data.AttackWidth.Wide) sampleRadius = 7.0f;
                    else if (agent.TacticsConfig.InPossession.AttackingWidth == Game.Scripts.Tactics.Data.AttackWidth.Narrow) sampleRadius = 3.0f;
                }
            }

            Vector3 bestPoint = baseTarget;
            float bestScore = -9999f;

            for (int i = 0; i < sampleCount; i++)
            {
                // Generate point in a circle around the search center
                float angle = i * Mathf.PI * 2f / sampleCount;
                Vector3 candidate = searchCenter + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * sampleRadius;

                // Clamp to field bounds
                candidate = ClampToFieldBounds(candidate, agent);

                // 3. Evaluate Point
                float score = 0f;

                // A. Base Proximity Score (Prefer staying closer to tactical baseline)
                float distToBase = Vector3.Distance(candidate, baseTarget);
                score -= distToBase * 0.1f; // Small penalty for moving away from tactical anchor

                // B. Pitch Control Score (Space Safety)
                score += PitchControlEvaluator.GetControlScore(candidate, agent.TeamID);

                // C. Pass Lane Score (Visibility to ball)
                score += PitchControlEvaluator.GetPassLaneScore(candidate, agent.TeamID) * 2.0f; // High weight for clear pass lane

                // D. Apply Phase-Based TacticsConfig Multipliers
                if (agent.TacticsConfig != null)
                {
                    float attackGoalZ = matchMgr.GetAttackGoalPosition(agent.TeamID).z;
                    float defendGoalZ = matchMgr.GetDefendGoalPosition(agent.TeamID).z;
                    float fieldLength = Mathf.Abs(attackGoalZ - defendGoalZ);
                    
                    // Determine Phase based on ball position
                    float ballDistFromDefendGoal = Mathf.Abs(ballPos.z - defendGoalZ);
                    float phaseRatio = ballDistFromDefendGoal / fieldLength;

                    float forwardBonus = 0f;
                    float spaceBonus = 0f;

                    // Build-up Phase (< 33%)
                    if (phaseRatio < 0.33f)
                    {
                        // Higher Risk Taking -> rewarding positions further up the pitch
                        float riskVal = agent.TacticsConfig.InPossession.BuildUpRiskTaking == Game.Scripts.Tactics.Data.RiskTaking.High ? 1.5f : (agent.TacticsConfig.InPossession.BuildUpRiskTaking == Game.Scripts.Tactics.Data.RiskTaking.Low ? 0.5f : 1.0f);
                        forwardBonus = riskVal * 2.0f; 
                    }
                    // Progression Phase
                    else if (phaseRatio < 0.66f)
                    {
                        // Higher PenetrationFrequency -> heavily rewarding forward runs into space
                        float penVal = agent.TacticsConfig.InPossession.PenetrationFrequency == Game.Scripts.Tactics.Data.PenetrationFrequency.High ? 1.5f : (agent.TacticsConfig.InPossession.PenetrationFrequency == Game.Scripts.Tactics.Data.PenetrationFrequency.Low ? 0.5f : 1.0f);
                        forwardBonus = penVal * 3.0f;
                        spaceBonus = penVal * 1.5f;
                    }
                    // Final Third Phase
                    else
                    {
                        // Higher PenetrationFrequency -> reward pitch control in dangerous areas
                        float penVal = agent.TacticsConfig.InPossession.PenetrationFrequency == Game.Scripts.Tactics.Data.PenetrationFrequency.High ? 1.5f : (agent.TacticsConfig.InPossession.PenetrationFrequency == Game.Scripts.Tactics.Data.PenetrationFrequency.Low ? 0.5f : 1.0f);
                        forwardBonus = penVal * 2.5f;
                    }

                    // Apply Forward Bonus (Is candidate closer to attack goal than baseTarget?)
                    float candidateForwardDiff = Mathf.Abs(candidate.z - defendGoalZ) - Mathf.Abs(baseTarget.z - defendGoalZ);
                    if (candidateForwardDiff > 0)
                    {
                        score += candidateForwardDiff * forwardBonus;
                    }

                    // Apply Space Bonus (SupportSpacing makes players spread out more)
                    if (spaceBonus > 0f)
                    {
                        score += (distToBase * 0.2f * spaceBonus); // Negate the base proximity penalty slightly
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestPoint = candidate;
                }
            }

            // Teammate repulsion is already handled partially by PitchControlEvaluator and GetSupportPosition.
            return ClampToFieldBounds(bestPoint, agent);
        }
        
        private Vector3 ApplySeparation(HybridAgentController agent, Vector3 targetPos, Vector3 ballPos)
        {
            // 1. 공 소유자와의 거리 체크
            float distToBall = Vector3.Distance(agent.transform.position, ballPos); // [FIX] Use my position, not target
            float minSeparation = 5.0f; // [FIX] 5m Personal Space

            // If I am too close to ball owner, move AWAY.
            if (distToBall < minSeparation)
            {
                Vector3 awayFromBall = (agent.transform.position - ballPos).normalized;
                if (awayFromBall == Vector3.zero) awayFromBall = -agent.transform.forward;
                
                // Set target to a point away from ball
                targetPos = ballPos + (awayFromBall * (minSeparation + 1.0f));
            }
            // If target is too close to ball owner, push it out
            else if (Vector3.Distance(targetPos, ballPos) < minSeparation)
            {
                 Vector3 pushDir = (targetPos - ballPos).normalized;
                 targetPos = ballPos + (pushDir * minSeparation);
            }

            // 2. 다른 동료들과의 겹침 방지 (척력)
            var teammates = agent.GetTeammates();
            Vector3 repulsion = Vector3.zero;
            int count = 0;
            foreach (var tm in teammates)
            {
                if (tm == agent || tm.IsGoalkeeper) continue; // Ignore self and GK
                float distToTm = Vector3.Distance(targetPos, tm.transform.position);
                if (distToTm < 4.5f && distToTm > 0.1f)
                {
                    Vector3 away = (targetPos - tm.transform.position).normalized;
                    repulsion += away * (4.5f - distToTm) / 4.5f;
                    count++;
                }
            }
            if (count > 0)
            {
                repulsion = (repulsion / count).normalized * 3.5f; // Push out by max 3.5m
                repulsion.y = 0;
                targetPos += repulsion;
            }

            return targetPos;
        }

        public void MoveToSafePosition(HybridAgentController agent, Vector3 pos)
        {
            Vector3 safePos = ClampToFieldBounds(pos, agent);
            
            // [NEW] DEADZONE CHECK (Prevent Jitter)
            // If already at destination (within 1.5m), do nothing
            float dist = Vector3.Distance(agent.transform.position, safePos);
            if (dist < 1.5f)
            {
                agent.Mover.Stop();
                agent.Mover.RotateToAction((MatchManager.Instance.Ball.transform.position - agent.transform.position), null);
                return;
            }

            // SMART SPRINT LOGIC (Attack Support & Stamina Conservation)
            // If we are far from position (> 8m), we MIGHT sprint.
            // But if we are far from the ball (> 25m), we jog to conserve stamina unless we are way out of position.
            float threshold = (agent.config) ? agent.config.SupportSprintThreshold : 8.0f;
            
            bool shouldSprint = false;
            if (dist > threshold)
            {
                float distToBall = Vector3.Distance(safePos, MatchManager.Instance.Ball.transform.position);
                if (distToBall < 25f || dist > 20f) 
                {
                    shouldSprint = true; // Sprint if play is nearby, or if critically out of position
                }
            }

            if (shouldSprint)
            {
                agent.Mover.SprintTo(safePos);
            }
            else
            {
                agent.Mover.MoveTo(safePos);
            }
        }

        // =========================================================
        // GEOMETRIC SUPPORT CALCULATION
        // =========================================================
        private Vector3 GetGeometricSupportSpot(HybridAgentController agent, Vector3 ballPos)
        {
            Vector3 target = Vector3.zero;
            string posName = agent.assignedPosition.ToString();
            
            bool isStriker = posName.StartsWith("ST") || posName.StartsWith("LW") || posName.StartsWith("RW");
            bool isDefender = posName.StartsWith("CB") || posName.EndsWith("B") /* LB, RB */;

            if (isStriker)
            {
                target = GetSupportTarget_Striker(agent, ballPos);
            }
            else if (isDefender)
            {
                target = GetSupportTarget_Defender(agent, ballPos);
            }
            else
            {
                target = GetSupportTarget_Midfielder(agent, ballPos);
            }

            // [NEW] FORMATION GRAVITY (포지션 복원력)
            // Prevent swarm ball by pulling players back to their actual formation slots
            if (agent.formationManager != null)
            {
                Vector3 basePos = agent.formationManager.GetAnchorPosition(agent.assignedPosition, agent.TeamID);
                
                // Shift basePos vertically based on ball position to maintain attacking lines collectively
                // e.g., line pushes up as ball goes forward
                float shiftZ = (ballPos.z - basePos.z) * 0.45f; // Follow the ball 45% of the distance
                basePos.z += shiftZ;
                
                // Shift slightly laterally towards ball
                float shiftX = (ballPos.x - basePos.x) * 0.2f;
                basePos.x += shiftX;
                
                // Blend: Strategic Target vs Formation Anchor
                // Striker = 60% Target, 40% Formation
                // Midfielder = 50% Target, 50% Formation
                // Defender = 30% Target, 70% Formation
                float blendWeight = isStriker ? 0.4f : (isDefender ? 0.7f : 0.5f);

                if (agent.TacticsConfig != null)
                {
                    // If high penetration frequency in progression, allow strikers to abandon formation anchor
                    float penVal = agent.TacticsConfig.InPossession.PenetrationFrequency == Game.Scripts.Tactics.Data.PenetrationFrequency.High ? 1.5f : (agent.TacticsConfig.InPossession.PenetrationFrequency == Game.Scripts.Tactics.Data.PenetrationFrequency.Low ? 0.5f : 1.0f);
                    if (isStriker) blendWeight -= (penVal * 0.2f);
                }

                blendWeight = Mathf.Clamp01(blendWeight);
                
                target = Vector3.Lerp(target, basePos, blendWeight);
            }

            return target;
        }

        // =========================================================
        // STRIKER POSITIONING
        // =========================================================
        private Vector3 GetSupportTarget_Striker(HybridAgentController agent, Vector3 ballPos)
        {
            // STRIKER LOGIC: Penetrate, Offside Line, Space
            // 1. Get Offside Line from FormationManager
            float offsideZ = 50f; // Default deep
            if (agent.formationManager != null) 
            {
                offsideZ = agent.formationManager.GetOffsideLine(agent.TeamID);
            }

            // Adjust offside line by small buffer (stay ON SIDE)
            float onsideBuffer = (agent.TeamID == Team.Home) ? -1.0f : 1.0f; 
            float targetZ_Line = offsideZ + onsideBuffer;

            // Settings
            var config = agent.config;
            float depth = config ? config.StrikerDepth : 16f;
            float channelWidth = config ? config.StrikerChannelWidth : 12f;

            // 2. Determine lateral position (Channel Run)
            float targetX = 0f;
            
            // Check Position
            if (agent.assignedPosition == FormationPosition.ST_Left)
            {
                targetX = -channelWidth; // Left Channel
            }
            else if (agent.assignedPosition == FormationPosition.ST_Right)
            {
                targetX = channelWidth; // Right Channel
            }
            else if (agent.assignedPosition == FormationPosition.ST_Center)
            {
                // Central Striker: Counter-move to ball to open space
                targetX = (ballPos.x > 0) ? -5f : 5f; 
            }
            else
            {
                // Fallback (e.g. CAM acting as Striker)
                targetX = (ballPos.x > 0) ? -10f : 10f;
            }

            // [중요] 미러링은 값을 결정한 '후'에 적용!
            if (agent.TeamID == Team.Away) targetX *= -1f;

            // 3. Goal Direction
            Vector3 goalDir = (_goalPosition - ballPos).normalized;
            

            // Channel Logic Override using Settings if needed, but keeping simple channel logic for now unless explicitly requested.
            // For channel width, we can map ST_Left/Right to channelWidth.
            if (agent.assignedPosition == FormationPosition.ST_Left) targetX = -channelWidth;
            else if (agent.assignedPosition == FormationPosition.ST_Right) targetX = channelWidth;

            // Target: Depth ahead of ball, but X is overridden by Channel
            Vector3 idealPos = ballPos + (goalDir * depth);
            idealPos.x = targetX; // Snap to Channel
            
            // Clamp Z to Offside Line logic
            if (agent.TeamID == Team.Home)
            {
                // Attacking +Z. Cannot go > targetZ_Line
                if (idealPos.z > targetZ_Line) idealPos.z = targetZ_Line;
                // But generally always try to be high
                if (idealPos.z < targetZ_Line - 5f) idealPos.z = targetZ_Line - 2f; // Push line
            }
            else
            {
                // Attacking -Z. Cannot go < targetZ_Line
                if (idealPos.z < targetZ_Line) idealPos.z = targetZ_Line;
                if (idealPos.z > targetZ_Line + 5f) idealPos.z = targetZ_Line + 2f; 
            }
            
            return idealPos;
        }

        // =========================================================
        // MIDFIELDER POSITIONING
        // =========================================================
        private Vector3 GetSupportTarget_Midfielder(HybridAgentController agent, Vector3 ballPos)
        {
            // MIDFIELDER LOGIC: Triangle, Link-up, Space
            // Stay between Ball and Strikers, but finding pockets.
            
            // 1. Angle relative to goal
            Vector3 goalDir = (_goalPosition - ballPos).normalized;
            
            // 2. Position: Support Angle based on Role
            float angle = 0f;
            // Left Mid: Supports on Left (-45)
            if (agent.assignedPosition == FormationPosition.LM || agent.assignedPosition == FormationPosition.CM_Left)
                angle = -45f;
            // Right Mid: Supports on Right (+45)
            else if (agent.assignedPosition == FormationPosition.RM || agent.assignedPosition == FormationPosition.CM_Right)
                angle = 45f;
            // Center/Defensive Mid: Supports Behind/Central (0 or slight offset)
            else
                angle = (agent.GetInstanceID() % 2 == 0) ? 20f : -20f; // Slight variation

            // TEAM MIRROR CHECK
            if (agent.TeamID == Team.Away) angle *= -1f;

            Vector3 supportDir = Quaternion.Euler(0, angle, 0) * goalDir;
            
            // FIX: Reduce Support Distance (User Req: Follow closer)
            // 미드필더가 공격수와 너무 멀어지지 않도록 (기존 13f -> 9f) 하향 조정
            var config = agent.config;
            float supportDist = config ? config.MidfieldSupportDist : 9f;
            
            // 전방 침투 강화: ballPos에서 단순히 각도만큼 틀어진 위치가 아니라, 골 방향으로 좀 더 깊게 들어갑니다.
            Vector3 targetPos = ballPos + supportDir * supportDist;
            
            // 전진 보정: 미드필더는 볼과 수평선 상에 있지 않고, 항상 볼보다 조금 더 전방 공간(GoalDir)을 잡게 강제합니다.
            targetPos += goalDir * 4.0f; 

            // FIX: Relax Box Constraint (User Req: Enter attacking third)
            // Was 35f (Defensive Third Limit). Now 42f (Penalty Spot Level).
            float boxLine = (agent.TeamID == Team.Home) ? 42f : -42f;
            
            // CUTBACK LOGIC:
            // If Ball is VERY deep (near byline > 45), hold the edge of the box (35-38) for a pass back.
            float deepLineVal = config ? config.CutbackDeepLine : 45f;
            float targetZVal = config ? config.CutbackTargetZ : 38f;

            float deepLine = (agent.TeamID == Team.Home) ? deepLineVal : -deepLineVal;
            bool isDeepAttack = (agent.TeamID == Team.Home) ? (ballPos.z > deepLine) : (ballPos.z < deepLine);
            
            if (isDeepAttack)
            {
                // Force Target to Edge of Box (Cutback Spot)
                float cutbackZ = (agent.TeamID == Team.Home) ? targetZVal : -targetZVal;
                targetPos.z = cutbackZ;
                
                // Align X with goal posts or half-space (don't go too wide)
                if (Mathf.Abs(targetPos.x) > 15f) targetPos.x *= 0.7f; 
            }
            else
            {
                // Normal Support: Just apply the relaxed clamp
                if (agent.TeamID == Team.Home && targetPos.z > boxLine) targetPos.z = boxLine;
                if (agent.TeamID == Team.Away && targetPos.z < boxLine) targetPos.z = boxLine;
            }

            return targetPos;
        }

        // =========================================================
        // DEFENDER POSITIONING
        // =========================================================
        private Vector3 GetSupportTarget_Defender(HybridAgentController agent, Vector3 ballPos)
        {
            // DEFENDER LOGIC: Rest Defense, Safety
            // Always behind the ball
            
            // 1. Stay behind ball
            Vector3 goalDir = (_goalPosition - ballPos).normalized;
            
            var config = agent.config;
            var tacticsConfig = agent.TacticsConfig;
            float defLineVal = tacticsConfig != null ? (tacticsConfig.OutOfPossession.MidBlockLine == Game.Scripts.Tactics.Data.DefensiveLine.High ? 1f : (tacticsConfig.OutOfPossession.MidBlockLine == Game.Scripts.Tactics.Data.DefensiveLine.Low ? 0f : 0.5f)) : 0.5f;
            float defLineMultiplier = Mathf.Lerp(1.5f, 0.6f, defLineVal);
            // 수비수가 너무 뒤로 처지지 않게 18m 유지 간격을 12m로 대폭 좁힙니다. (전술 지침에 따라 조절)
            float backDist = (config != null ? config.DefenderBackDist : 12f) * defLineMultiplier;

            Vector3 targetPos = ballPos - (goalDir * backDist);

            // HIGH LINE CONSTRAINT (User Req: Don't retreat)
            // 공격 시 수비수들이 절대로 자기 진영 깊숙이 남지 않고 하프라인을 넘어서 진영을 끌어올리도록 강제합니다.
            float defLineValForPush = tacticsConfig != null ? (tacticsConfig.OutOfPossession.MidBlockLine == Game.Scripts.Tactics.Data.DefensiveLine.High ? 1f : (tacticsConfig.OutOfPossession.MidBlockLine == Game.Scripts.Tactics.Data.DefensiveLine.Low ? 0f : 0.5f)) : 0.5f;
            float pushUpMax = Mathf.Lerp(5f, 25f, defLineValForPush);

            if (agent.TeamID == Team.Home)
            {
                // Attacking +Z. 공이 넘어갔다면 하프라인(0) 이상으로 전술 라인을 올립니다.
                if (ballPos.z > 0f) 
                {
                    float pushUpLine = Mathf.Min(ballPos.z - 8f, pushUpMax); // 공 바로 뒤 8m, 최대 pushUpMax까지 전진
                    if (targetPos.z < pushUpLine) targetPos.z = pushUpLine;
                }
                else if (ballPos.z > -15f && targetPos.z < -10f) targetPos.z = -10f; // 초기 빌드업 시에도 덜 물러남
            }
            else
            {
                // Attacking -Z.
                if (ballPos.z < 0f) 
                {
                    float pushUpLine = Mathf.Max(ballPos.z + 8f, -pushUpMax);
                    if (targetPos.z > pushUpLine) targetPos.z = pushUpLine;
                }
                else if (ballPos.z < 15f && targetPos.z > 10f) targetPos.z = 10f;
            }

            // 2. Width (LB/RB)
            float teamSideMult = (agent.TeamID == Team.Home) ? 1f : -1f;
            float defWidth = config ? config.DefenderWidth : 20f;

            if (agent.assignedPosition == FormationPosition.LB || agent.assignedPosition == FormationPosition.RB)
            {
                float w = defWidth;
                if (agent.assignedPosition == FormationPosition.LB) w = -defWidth; // Base Left
                else w = defWidth; // Base Right
                
                // Apply Team Mirror
                targetPos.x = w * teamSideMult; 
            }
            else // CB
            {
                // Just keep relative X to ball, but clamp max width if needed
                if (Mathf.Abs(targetPos.x) > 20f) targetPos.x *= 0.8f; 
            }
            
            return targetPos;
        }

        // =========================================================
        // FIELD BOUNDS CLAMPING
        // =========================================================
        private Vector3 ClampToFieldBounds(Vector3 pos, HybridAgentController agent)
        {
            float w = 32f; // Default
            float l = 48f; // Default

            if (agent.BallHandler && agent.BallHandler.settings)
            {
                w = agent.BallHandler.settings.fieldHalfWidth;
                l = agent.BallHandler.settings.fieldHalfLength;
            }

            // Clamp X
            // Reduce slightly to avoid wall hugging
            pos.x = Mathf.Clamp(pos.x, -(w - 2f), (w - 2f));
            
            // Clamp Z
            float limitZ = l;
            if (agent.IsGoalkeeper) limitZ = l + 1.5f; // GK can go slightly deeper? Or standard.
            
            // Note: Attacking limits are usually stricter to avoid offside/endline, 
            // but this is a hard safety clamp.
            pos.z = Mathf.Clamp(pos.z, -limitZ, limitZ);
            
            return pos;
        }
    }
}
