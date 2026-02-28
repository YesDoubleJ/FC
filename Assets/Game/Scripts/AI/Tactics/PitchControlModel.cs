using UnityEngine;
using System.Collections.Generic;
using Game.Scripts.Managers;
using Game.Scripts.Data;
using Unity.Jobs;
using Unity.Collections;

namespace Game.Scripts.AI.Tactics
{
    /// <summary>
    /// 피치 컨트롤 모델 — 지침서 §6.1
    /// Spearman(2017) 모델 단순화: 각 그리드 셀에 대해 아군/적군의 도달 시간 계산.
    /// PC(x) = Σ(e^(-π·tᵢ²/σ²)) for each team
    /// 
    /// 갱신 주기: 0.5초
    /// </summary>
    public class PitchControlModel : MonoBehaviour
    {
        [Header("Grid Settings")]
        [Tooltip("가로 셀 수")]
        public int GridCols = 12;
        [Tooltip("세로 셀 수")]
        public int GridRows = 8;
        [Tooltip("갱신 간격 (초)")]
        public float UpdateInterval = 0.5f;

        [Header("Model Parameters")]
        [Tooltip("시그마 값 — 도달 시간 가우시안 분포의 표준편차")]
        public float Sigma = 0.7f; // ReactionTimeHuman과 연관

        [Header("Debug")]
        public bool ShowGizmos = true;
        public float GizmoHeight = 0.3f;

        // Constants
        private const int MAX_AGENTS = 22;
        private const float MIN_SPEED_DIVISOR = 0.1f;
        private const float DEFAULT_INFLUENCE = 0.5f;

        // =========================================================
        // 내부 데이터
        // =========================================================
        private float[,] _homePitchControl;
        private float[,] _awayPitchControl;
        
        // Job System Native Arrays
        private Unity.Collections.NativeArray<float> _homePCResult;
        private Unity.Collections.NativeArray<float> _awayPCResult;
        private Unity.Collections.NativeArray<Vector3> _agentPositions;
        private Unity.Collections.NativeArray<float> _agentSpeeds;
        private Unity.Collections.NativeArray<int> _agentTeamIDs; // 0 for Home, 1 for Away

        private float _cellWidth;
        private float _cellHeight;
        private float _fieldHalfW;
        private float _fieldHalfH;
        private float _updateTimer;
        private float _sigmaSquared;

        public static PitchControlModel Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            InitializeGrid();
        }

        private void InitializeGrid()
        {
            _homePitchControl = new float[GridCols, GridRows];
            _awayPitchControl = new float[GridCols, GridRows];
            _sigmaSquared = Sigma * Sigma;

            if (FieldManager.Instance != null)
            {
                _fieldHalfW = FieldManager.Instance.Width / 2f;
                _fieldHalfH = FieldManager.Instance.Length / 2f;
            }
            else
            {
                _fieldHalfW = 30f; // fallback
                _fieldHalfH = 45f;
            }

            _cellWidth = (_fieldHalfW * 2f) / GridCols;
            _cellHeight = (_fieldHalfH * 2f) / GridRows;
            
            int totalCells = GridCols * GridRows;
            _homePCResult = new Unity.Collections.NativeArray<float>(totalCells, Unity.Collections.Allocator.Persistent);
            _awayPCResult = new Unity.Collections.NativeArray<float>(totalCells, Unity.Collections.Allocator.Persistent);
            
            // Assume max players on the pitch for arrays
            _agentPositions = new Unity.Collections.NativeArray<Vector3>(MAX_AGENTS, Unity.Collections.Allocator.Persistent);
            _agentSpeeds = new Unity.Collections.NativeArray<float>(MAX_AGENTS, Unity.Collections.Allocator.Persistent);
            _agentTeamIDs = new Unity.Collections.NativeArray<int>(MAX_AGENTS, Unity.Collections.Allocator.Persistent);
        }

        private void OnDestroy()
        {
            if (_homePCResult.IsCreated) _homePCResult.Dispose();
            if (_awayPCResult.IsCreated) _awayPCResult.Dispose();
            if (_agentPositions.IsCreated) _agentPositions.Dispose();
            if (_agentSpeeds.IsCreated) _agentSpeeds.Dispose();
            if (_agentTeamIDs.IsCreated) _agentTeamIDs.Dispose();
        }

        private void Update()
        {
            _updateTimer += Time.deltaTime;
            if (_updateTimer >= UpdateInterval)
            {
                _updateTimer = 0f;
                RefreshPitchControlJob();
            }
        }

        // =========================================================
        // 피치 컨트롤 계산 (Job System)
        // =========================================================
        [Unity.Burst.BurstCompile]
        public struct PitchControlJob : Unity.Jobs.IJobParallelFor
        {
            [Unity.Collections.ReadOnly] public Unity.Collections.NativeArray<Vector3> AgentPositions;
            [Unity.Collections.ReadOnly] public Unity.Collections.NativeArray<float> AgentSpeeds;
            [Unity.Collections.ReadOnly] public Unity.Collections.NativeArray<int> AgentTeamIDs;
            public int AgentCount;

            public int GridCols;
            public int GridRows;
            public float CellWidth;
            public float CellHeight;
            public float FieldHalfW;
            public float FieldHalfH;
            public float SigmaSquared;
            public float Sigma;

            public Unity.Collections.NativeArray<float> HomePCResult;
            public Unity.Collections.NativeArray<float> AwayPCResult;

            public void Execute(int index)
            {
                int x = index % GridCols;
                int y = index / GridCols;

                // Calculate World Position for cell
                float worldX = (x * CellWidth) - FieldHalfW + (CellWidth / 2f);
                float worldZ = (y * CellHeight) - FieldHalfH + (CellHeight / 2f);
                Vector3 cellCenter = new Vector3(worldX, 0.05f, worldZ);

                float homeInfluence = 0f;
                float awayInfluence = 0f;

                for (int i = 0; i < AgentCount; i++)
                {
                    float distance = Vector3.Distance(AgentPositions[i], cellCenter);
                    float maxSpeed = AgentSpeeds[i];
                    float arrivalTime = (distance / Mathf.Max(maxSpeed, MIN_SPEED_DIVISOR)) + Sigma; // Reaction time

                    float influence = Mathf.Exp(-Mathf.PI * arrivalTime * arrivalTime / SigmaSquared);

                    if (AgentTeamIDs[i] == 0) // Home
                    {
                        homeInfluence += influence;
                    }
                    else
                    {
                        awayInfluence += influence;
                    }
                }

                float total = homeInfluence + awayInfluence;
                HomePCResult[index] = total > 0.001f ? homeInfluence / total : DEFAULT_INFLUENCE;
                AwayPCResult[index] = total > 0.001f ? awayInfluence / total : DEFAULT_INFLUENCE;
            }
        }

        private void RefreshPitchControlJob()
        {
            var agents = FindObjectsByType<Game.Scripts.AI.HybridAgentController>(FindObjectsSortMode.None);
            
            int agentCount = Mathf.Min(agents.Length, MAX_AGENTS);

            // Populate Input Data
            for (int i = 0; i < agentCount; i++)
            {
                _agentPositions[i] = agents[i].transform.position;
                
                var stats = agents[i].GetComponent<PlayerStats>();
                float maxSpeed = 6f; // 기본값
                if (stats != null)
                {
                    maxSpeed = Mathf.Lerp(4f, 9.8f, stats.GetEffectiveStat(StatType.Speed) / 100f);
                }
                _agentSpeeds[i] = maxSpeed;
                _agentTeamIDs[i] = agents[i].TeamID == Team.Home ? 0 : 1;
            }

            int totalCells = GridCols * GridRows;

            PitchControlJob job = new PitchControlJob
            {
                AgentPositions = _agentPositions,
                AgentSpeeds = _agentSpeeds,
                AgentTeamIDs = _agentTeamIDs,
                AgentCount = agentCount,
                GridCols = GridCols,
                GridRows = GridRows,
                CellWidth = _cellWidth,
                CellHeight = _cellHeight,
                FieldHalfW = _fieldHalfW,
                FieldHalfH = _fieldHalfH,
                SigmaSquared = _sigmaSquared,
                Sigma = Sigma,
                HomePCResult = _homePCResult,
                AwayPCResult = _awayPCResult
            };

            // Schedule and Complete immediately (Wait for it since it's used next frame)
            Unity.Jobs.JobHandle jobHandle = job.Schedule(totalCells, 16);
            jobHandle.Complete();

            // Dump back to 2D array for existing API compatibility
            for(int i = 0; i < totalCells; i++)
            {
                int x = i % GridCols;
                int y = i / GridCols;
                _homePitchControl[x, y] = _homePCResult[i];
                _awayPitchControl[x, y] = _awayPCResult[i];
            }
        }

        // =========================================================
        // Public API
        // =========================================================

        /// <summary>
        /// 특정 위치의 팀별 피치 컨트롤 값 반환 (0~1).
        /// 0.5 = 균등, >0.5 = 해당 팀 우세
        /// </summary>
        public float GetPitchControl(Vector3 worldPos, Team team)
        {
            Vector2Int cell = WorldToCell(worldPos);
            if (!IsValidCell(cell)) return 0.5f;

            return team == Team.Home
                ? _homePitchControl[cell.x, cell.y]
                : _awayPitchControl[cell.x, cell.y];
        }

        /// <summary>
        /// 공격 침투 지점 탐색: PC > threshold인 아군 우세 공간 반환.
        /// </summary>
        public List<Vector3> GetDominatedSpaces(Team team, float threshold = 0.6f)
        {
            var results = new List<Vector3>();
            var controlMap = team == Team.Home ? _homePitchControl : _awayPitchControl;

            for (int x = 0; x < GridCols; x++)
            {
                for (int y = 0; y < GridRows; y++)
                {
                    if (controlMap[x, y] > threshold)
                    {
                        results.Add(CellToWorld(x, y));
                    }
                }
            }
            return results;
        }

        /// <summary>
        /// 수비 취약 지점 탐색: 적 PC가 >threshold인 위험 지역.
        /// </summary>
        public List<Vector3> GetVulnerableSpaces(Team defendingTeam, float threshold = 0.6f)
        {
            Team attackingTeam = defendingTeam == Team.Home ? Team.Away : Team.Home;
            return GetDominatedSpaces(attackingTeam, threshold);
        }

        // =========================================================
        // 좌표 변환 유틸
        // =========================================================
        private Vector2Int WorldToCell(Vector3 worldPos)
        {
            float normalizedX = (worldPos.x + _fieldHalfW) / (_fieldHalfW * 2f);
            float normalizedZ = (worldPos.z + _fieldHalfH) / (_fieldHalfH * 2f);
            int col = Mathf.Clamp(Mathf.FloorToInt(normalizedX * GridCols), 0, GridCols - 1);
            int row = Mathf.Clamp(Mathf.FloorToInt(normalizedZ * GridRows), 0, GridRows - 1);
            return new Vector2Int(col, row);
        }

        private Vector3 CellToWorld(int col, int row)
        {
            float x = (col + 0.5f) * _cellWidth - _fieldHalfW;
            float z = (row + 0.5f) * _cellHeight - _fieldHalfH;
            return new Vector3(x, 0f, z);
        }

        private bool IsValidCell(Vector2Int cell)
        {
            return cell.x >= 0 && cell.x < GridCols && cell.y >= 0 && cell.y < GridRows;
        }

        // =========================================================
        // Debug — §8.3
        // =========================================================
        private void OnDrawGizmos()
        {
            if (!ShowGizmos || _homePitchControl == null) return;

            for (int x = 0; x < GridCols; x++)
            {
                for (int y = 0; y < GridRows; y++)
                {
                    Vector3 center = CellToWorld(x, y);
                    center.y = GizmoHeight;

                    float homePC = _homePitchControl[x, y];

                    // 홈 우세 = 파랑, 어웨이 우세 = 빨강, 균등 = 노랑
                    Color cellColor;
                    if (homePC > 0.6f)
                        cellColor = Color.Lerp(Color.yellow, Color.blue, (homePC - 0.5f) * 2f);
                    else if (homePC < 0.4f)
                        cellColor = Color.Lerp(Color.yellow, Color.red, (0.5f - homePC) * 2f);
                    else
                        cellColor = Color.yellow;

                    cellColor.a = 0.2f + Mathf.Abs(homePC - 0.5f) * 0.6f;

                    Gizmos.color = cellColor;
                    Vector3 size = new Vector3(_cellWidth * 0.85f, 0.05f, _cellHeight * 0.85f);
                    Gizmos.DrawCube(center, size);
                }
            }
        }
    }
}
