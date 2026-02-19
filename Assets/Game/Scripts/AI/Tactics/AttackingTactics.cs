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

            // 2. 다른 동료들과의 겹침 방지 (간단 버전)
            // (성능을 위해 본인 주변 5m만 체크)
            /*
            var teammates = agent.GetTeammates();
            foreach (var tm in teammates)
            {
                if (tm == agent) continue;
                if (Vector3.Distance(targetPos, tm.transform.position) < 3.0f)
                {
                    Vector3 fleeDir = (targetPos - tm.transform.position).normalized;
                    targetPos += fleeDir * 2.0f; // 2m 옆으로 비켜섬
                }
            }
            */

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

            // SMART SPRINT LOGIC (Attack Support)
            // If we are far from position (> 8m), SPRINT to get there.
            // If close, use normal move for precision.
            float threshold = (agent.config) ? agent.config.SupportSprintThreshold : 8.0f;
            
            if (dist > threshold)
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
            switch (agent.assignedPosition)
            {
                case FormationPosition.ST_Left:
                case FormationPosition.ST_Right:
                case FormationPosition.ST_Center:
                    return GetSupportTarget_Striker(agent, ballPos);
                case FormationPosition.CM_Left:
                case FormationPosition.CM_Right:
                case FormationPosition.LM:
                case FormationPosition.RM:
                    return GetSupportTarget_Midfielder(agent, ballPos);
                default:
                    return GetSupportTarget_Defender(agent, ballPos);
            }
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
            // Was 18f, High -> 13f (Tight Support)
            var config = agent.config;
            float supportDist = config ? config.MidfieldSupportDist : 13f;
            
            Vector3 targetPos = ballPos + supportDir * supportDist;
            
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
            float backDist = config ? config.DefenderBackDist : 18f;

            Vector3 targetPos = ballPos - (goalDir * backDist);

            // HIGH LINE CONSTRAINT (User Req: Don't retreat)
            if (agent.TeamID == Team.Home)
            {
                // Attacking +Z. If Ball > 10, don't drop below -5.
                if (ballPos.z > 10f && targetPos.z < -5f) targetPos.z = -5f;
            }
            else
            {
                // Attacking -Z. If Ball < -10, don't drop above 5.
                if (ballPos.z < -10f && targetPos.z > 5f) targetPos.z = 5f;
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
