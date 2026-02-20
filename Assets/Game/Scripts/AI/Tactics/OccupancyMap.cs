using UnityEngine;
using System.Collections.Generic;
using Game.Scripts.Managers;

namespace Game.Scripts.AI.Tactics
{
    /// <summary>
    /// 밀도 기반 내비게이션 — 지침서 §2.2
    /// 경기장을 N×M 그리드로 분할하여 각 셀의 선수 밀도를 추적합니다.
    /// AI는 밀도가 높은 지역을 회피하고, 빈 공간으로 침투합니다.
    /// 
    /// 갱신 주기: 0.5초 (LOD 2 수준)
    /// </summary>
    public class OccupancyMap : MonoBehaviour
    {
        [Header("Grid Settings")]
        [Tooltip("가로 셀 수 (X축)")]
        public int GridCols = 12;

        [Tooltip("세로 셀 수 (Z축)")]
        public int GridRows = 8;

        [Tooltip("밀도 갱신 간격 (초)")]
        public float UpdateInterval = 0.5f;

        [Header("Debug")]
        public bool ShowGizmos = true;
        public float GizmoHeight = 0.5f;

        // =========================================================
        // 내부 데이터
        // =========================================================
        private int[,] _densityMap;          // 각 셀의 총 선수 수
        private int[,] _homeDensityMap;      // 홈팀 밀도
        private int[,] _awayDensityMap;      // 어웨이팀 밀도
        private float _cellWidth;
        private float _cellHeight;
        private float _fieldHalfW;
        private float _fieldHalfH;
        private float _updateTimer;

        // 외부 접근용 캐시
        private List<Vector3> _openSpacesCache = new List<Vector3>();

        // =========================================================
        // 싱글톤 (선택적)
        // =========================================================
        public static OccupancyMap Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            InitializeGrid();
        }

        private void InitializeGrid()
        {
            _densityMap = new int[GridCols, GridRows];
            _homeDensityMap = new int[GridCols, GridRows];
            _awayDensityMap = new int[GridCols, GridRows];

            // 필드 크기 가져오기
            if (FieldManager.Instance != null)
            {
                _fieldHalfW = FieldManager.Instance.Width / 2f;
                _fieldHalfH = FieldManager.Instance.Length / 2f;
            }
            else
            {
                _fieldHalfW = 30f;  // 기본값
                _fieldHalfH = 50f;
            }

            _cellWidth = (_fieldHalfW * 2f) / GridCols;
            _cellHeight = (_fieldHalfH * 2f) / GridRows;
        }

        private void Update()
        {
            _updateTimer += Time.deltaTime;
            if (_updateTimer >= UpdateInterval)
            {
                _updateTimer = 0f;
                RefreshDensityMap();
            }
        }

        // =========================================================
        // 밀도 맵 갱신
        // =========================================================
        private void RefreshDensityMap()
        {
            // 초기화
            System.Array.Clear(_densityMap, 0, _densityMap.Length);
            System.Array.Clear(_homeDensityMap, 0, _homeDensityMap.Length);
            System.Array.Clear(_awayDensityMap, 0, _awayDensityMap.Length);
            _openSpacesCache.Clear();

            // 모든 에이전트 순회
            var agents = FindObjectsByType<Game.Scripts.AI.HybridAgentController>(FindObjectsSortMode.None);
            foreach (var agent in agents)
            {
                Vector2Int cell = WorldToCell(agent.transform.position);
                if (IsValidCell(cell))
                {
                    _densityMap[cell.x, cell.y]++;

                    if (agent.TeamID == Game.Scripts.Data.Team.Home)
                        _homeDensityMap[cell.x, cell.y]++;
                    else
                        _awayDensityMap[cell.x, cell.y]++;
                }
            }

            // 빈 공간 캐시 갱신
            for (int x = 0; x < GridCols; x++)
            {
                for (int y = 0; y < GridRows; y++)
                {
                    if (_densityMap[x, y] == 0)
                    {
                        _openSpacesCache.Add(CellToWorld(x, y));
                    }
                }
            }
        }

        // =========================================================
        // Public API
        // =========================================================

        /// <summary>월드 좌표의 총 선수 밀도 반환</summary>
        public int GetDensityAt(Vector3 worldPos)
        {
            Vector2Int cell = WorldToCell(worldPos);
            if (!IsValidCell(cell)) return 0;
            return _densityMap[cell.x, cell.y];
        }

        /// <summary>특정 팀의 밀도 반환</summary>
        public int GetTeamDensityAt(Vector3 worldPos, Game.Scripts.Data.Team team)
        {
            Vector2Int cell = WorldToCell(worldPos);
            if (!IsValidCell(cell)) return 0;
            return team == Game.Scripts.Data.Team.Home
                ? _homeDensityMap[cell.x, cell.y]
                : _awayDensityMap[cell.x, cell.y];
        }

        /// <summary>빈 공간(선수가 0인 셀) 목록 반환</summary>
        public List<Vector3> GetOpenSpaces()
        {
            return _openSpacesCache;
        }

        /// <summary>특정 위치 주변의 밀도 합산 (3×3 이웃)</summary>
        public int GetNeighborDensity(Vector3 worldPos)
        {
            Vector2Int center = WorldToCell(worldPos);
            int total = 0;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    int nx = center.x + dx;
                    int ny = center.y + dy;
                    if (nx >= 0 && nx < GridCols && ny >= 0 && ny < GridRows)
                        total += _densityMap[nx, ny];
                }
            }
            return total;
        }

        /// <summary>
        /// AI 이동 시 밀도 회피 비용 반환 (0~1).
        /// 0 = 빈 공간, 1 = 매우 밀집.
        /// </summary>
        public float GetAvoidanceCost(Vector3 worldPos)
        {
            int density = GetNeighborDensity(worldPos);
            // 9셀 × 최대 2~3명 = 약 20 이상이면 최대치
            return Mathf.Clamp01(density / 10f);
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
            if (!ShowGizmos || _densityMap == null) return;

            for (int x = 0; x < GridCols; x++)
            {
                for (int y = 0; y < GridRows; y++)
                {
                    Vector3 center = CellToWorld(x, y);
                    center.y = GizmoHeight;

                    int density = _densityMap[x, y];
                    if (density == 0)
                    {
                        // 빈 공간 = 연한 초록
                        Gizmos.color = new Color(0f, 1f, 0f, 0.05f);
                    }
                    else
                    {
                        // 밀집 = 빨강 (밀도에 비례)
                        float t = Mathf.Clamp01(density / 4f);
                        Gizmos.color = new Color(1f, 1f - t, 0f, 0.2f + t * 0.4f);
                    }

                    Vector3 size = new Vector3(_cellWidth * 0.9f, 0.1f, _cellHeight * 0.9f);
                    Gizmos.DrawCube(center, size);

                    // 셀 외곽선
                    Gizmos.color = new Color(1f, 1f, 1f, 0.1f);
                    Gizmos.DrawWireCube(center, size);
                }
            }
        }
    }
}
