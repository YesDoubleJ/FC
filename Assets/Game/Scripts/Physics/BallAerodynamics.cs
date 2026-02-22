using UnityEngine;
using Game.Scripts.Data;

namespace Game.Scripts.Physics
{
    /// <summary>
    /// 공의 공기역학을 시뮬레이션합니다.
    /// 지침서 §3.1: 마그누스 효과 (F_magnus = C_L × ρ × |v|² × dir)
    /// 지침서 §3.2: 환경 마찰 (Dry/Wet), 구름 저항 (Rolling Resistance)
    /// 
    /// 원칙 2 준수: 모든 물리 상수는 MatchEngineConfig ScriptableObject에서 참조.
    /// 원칙 4 준수: 매 프레임 물리 엔진(PhysX)으로 실시간 계산, 미리 계산된 애니메이션 없음.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class BallAerodynamics : MonoBehaviour
    {
        [Header("Config Reference")]
        [Tooltip("MatchEngineConfig ScriptableObject. null이면 Resources에서 로드 시도")]
        public MatchEngineConfig config;

        [Header("Collision Damping")]
        [Tooltip("볼 소유자 선수와 충돌 시 잔류 속도 비율 (0=완전 정지, 1=그대로). 낮을수록 발 밑에 붙음.")]
        [Range(0.0f, 1.0f)]
        public float ownerCollisionDamping = 0.15f;

        [Tooltip("비소유자 선수와 충돌 시 잔류 속도 비율 (달려오는 선수가 공을 차버리는 세기 조절).")]
        [Range(0.0f, 1.0f)]
        public float nonOwnerCollisionDamping = 0.35f;

        // 하위호환성 유지 (기존 코드에서 playerCollisionDamping 참조 시)
        public float playerCollisionDamping
        {
            get => ownerCollisionDamping;
            set => ownerCollisionDamping = value;
        }

        private Rigidbody _rb;
        private Vector3 _currentMagnusForce;
        private Vector3 _currentDragForce;
        private bool _isGrounded;

        // =========================================================
        // 캐시된 Config 값 (매 프레임 접근 최적화)
        // =========================================================
        private float _magnusCoeff;
        private float _airDensity;
        private float _dragCoeff;
        private float _groundFriction;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();

            // Config 로드
            if (config == null)
                config = Resources.Load<MatchEngineConfig>("DefaultMatchEngineConfig");

            if (_rb != null)
            {
                _rb.interpolation = RigidbodyInterpolation.Interpolate;
                _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                _rb.maxAngularVelocity = 100f;

                // §3.1: Rigidbody 기본 Drag는 0 — 스크립트에서 직접 제어
                _rb.linearDamping = 0f;
                _rb.angularDamping = 0.05f;

                // [핵심 수정] 공 PhysicsMaterial bounciness 코드로 강제 0 설정
                // 선수 몸에 맞고 탱탱볼처럼 튀어나가는 현상 방지
                var col = GetComponent<Collider>();
                if (col != null)
                {
                    var mat = new PhysicsMaterial("BallNoBounceMat");
                    mat.bounciness      = 0f;
                    mat.dynamicFriction = 0.4f;
                    mat.staticFriction  = 0.4f;
                    mat.frictionCombine = PhysicsMaterialCombine.Minimum;
                    mat.bounceCombine   = PhysicsMaterialCombine.Minimum;
                    col.material        = mat;
                }
            }

            CacheConfigValues();
        }

        /// <summary>Config 값을 로컬 캐시에 저장 (매 프레임 ScriptableObject 접근 방지)</summary>
        private void CacheConfigValues()
        {
            if (config != null)
            {
                _magnusCoeff   = config.MagnusCoeff;
                _airDensity    = config.AirDensity;
                _dragCoeff     = config.DragCoeffAir;
                _groundFriction = config.CurrentGroundFriction;
            }
            else
            {
                // 폴백 기본값 (지침서 부록 기준)
                _magnusCoeff   = 0.0004f;
                _airDensity    = 1.225f;
                _dragCoeff     = 0.2f;
                _groundFriction = 0.8f;
            }
        }

        /// <summary>런타임에 Config가 변경되었을 때 호출 (날씨 변경 등)</summary>
        public void RefreshConfig()
        {
            CacheConfigValues();
        }

        // =========================================================
        // FixedUpdate — 매 물리 프레임 실행 (§원칙 4)
        // =========================================================
        private void FixedUpdate()
        {
            if (_rb == null || _rb.isKinematic)
            {
                _currentMagnusForce = Vector3.zero;
                _currentDragForce = Vector3.zero;
                return;
            }

            // [FIX] 공의 실제 크기 비례로 바닥 판정 거리 확충 (0.15f 하드코딩 시 공 크면 영원히 공중 판정)
            float checkDist = 0.6f;
            var col = GetComponent<Collider>();
            if (col != null) checkDist = col.bounds.extents.y + 0.1f;

            _isGrounded = UnityEngine.Physics.Raycast(transform.position, Vector3.down, checkDist) 
                          || transform.position.y < checkDist + 0.05f;

            ApplyAirDrag();
            ApplyGroundFriction();
            ApplyMagnusForce();
        }

        // =========================================================
        // §3.1: 공기 항력 — 속도의 제곱에 비례
        // F_drag = -v̂ × C_d × ρ × |v|²
        // =========================================================
        private void ApplyAirDrag()
        {
            Vector3 velocity = _rb.linearVelocity;
            float speedSq = velocity.sqrMagnitude;

            if (speedSq < 0.01f)
            {
                _currentDragForce = Vector3.zero;
                return;
            }

            // F_drag = 0.5 * C_d * ρ * A * |v|² (단면적 A ≈ 0.038m²)
            float area = 0.038f;
            _currentDragForce = -velocity.normalized * (0.5f * _dragCoeff * _airDensity * area * speedSq);

            // 항력이 현재 운동량을 초과하지 않도록 클램프
            float maxDragMagnitude = velocity.magnitude / Time.fixedDeltaTime * _rb.mass;
            if (_currentDragForce.magnitude > maxDragMagnitude)
                _currentDragForce = _currentDragForce.normalized * maxDragMagnitude * 0.5f;

            _rb.AddForce(_currentDragForce, ForceMode.Force);
        }

        // =========================================================
        // §3.2: 지면 마찰 — 환경 상태(Dry/Wet)에 따른 구름 저항
        // =========================================================
        private void ApplyGroundFriction()
        {
            if (!_isGrounded) return;

            Vector3 velocity = _rb.linearVelocity;
            // 수평 속도만을 기준으로 마찰 적용
            velocity.y = 0f;
            float speed = velocity.magnitude;

            if (speed < 0.1f)
            {
                // 매우 느린 공: 완전 정지
                if (_rb.linearVelocity.sqrMagnitude > 0f)
                {
                    _rb.linearVelocity = Vector3.Lerp(_rb.linearVelocity, Vector3.zero, Time.fixedDeltaTime * 10f);
                    _rb.angularVelocity = Vector3.Lerp(_rb.angularVelocity, Vector3.zero, Time.fixedDeltaTime * 10f);
                }
                return;
            }

            // 구름 저항: 잔디 마찰력 반영 (기존 0.08은 너무 작아 하염없이 굴러감 -> 0.4로 대폭 상향)
            float rollingResistanceCoeff = Mathf.Max(0.4f, _groundFriction * 0.5f); 
            float frictionForce = rollingResistanceCoeff * 9.81f * (config != null ? config.BallMass : 0.43f);
            Vector3 frictionVec = -velocity.normalized * frictionForce;

            _rb.AddForce(frictionVec, ForceMode.Force);

            // Rolling Resistance: 회전 마찰 (공의 회전 자체도 더 빨리 멈추게 함)
            if (_rb.angularVelocity.magnitude > 0.1f)
            {
                Vector3 rollingResistance = -_rb.angularVelocity.normalized * _groundFriction * 5f;
                _rb.AddTorque(rollingResistance, ForceMode.Acceleration);
            }
        }

        // =========================================================
        // §3.1: 마그누스 효과 — F = C_L × ρ × |v|² × (ω × v).normalized
        // =========================================================
        private void ApplyMagnusForce()
        {
            if (_rb == null || _rb.isKinematic) return;

            Vector3 velocity = _rb.linearVelocity;
            Vector3 angularVelocity = _rb.angularVelocity;

            float speed = velocity.magnitude;
            if (speed < 2.0f || angularVelocity.magnitude < 0.5f)
            {
                _currentMagnusForce = Vector3.zero;
                return;
            }

            // §3.1 공식: 마그누스 힘의 방향 = 각속도 × 선속도의 외적
            Vector3 magnusDir = Vector3.Cross(angularVelocity, velocity).normalized;

            // 힘의 크기: C_L × ρ × |v|²
            float magnusMagnitude = _magnusCoeff * _airDensity * velocity.sqrMagnitude;

            Vector3 targetForce = magnusDir * magnusMagnitude;

            // 안전 클램프: 과도한 힘 방지
            if (targetForce.magnitude > 15f)
                targetForce = targetForce.normalized * 15f;

            // 부드러운 전환 (갑작스러운 힘 변화 방지)
            _currentMagnusForce = Vector3.Lerp(_currentMagnusForce, targetForce, Time.fixedDeltaTime * 5f);
            _rb.AddForce(_currentMagnusForce, ForceMode.Force);
        }

        // =========================================================
        // 충돌 처리 (기존 로직 유지)
        // =========================================================
        private void OnCollisionEnter(Collision collision)
        {
            if (_rb == null || _rb.isKinematic) return;

            if (collision.gameObject.CompareTag("Player"))
            {
                HandlePlayerCollision(collision);
                return;
            }

            // 땅에 닿으면 스핀 감소
            if (collision.gameObject.layer == LayerMask.NameToLayer("Ground") || collision.transform.position.y < 0.1f)
            {
                _rb.angularVelocity *= 0.8f;
            }
            else
            {
                string objName = collision.gameObject.name;
                if (objName.Contains("Net") || objName.Contains("Wall") || objName.Contains("Post") || objName.Contains("Goal"))
                {
                    ResetAerodynamics();
                }
            }
        }

        private void HandlePlayerCollision(Collision collision)
        {
            // 볼 소유자 여부 확인
            var matchMgr = Game.Scripts.Managers.MatchManager.Instance;
            bool isOwner = false;

            if (matchMgr != null && matchMgr.CurrentBallOwner != null)
            {
                isOwner = (collision.gameObject == matchMgr.CurrentBallOwner.gameObject);
            }

            if (isOwner)
            {
                // ── 소유자 충돌: 발 밑에 공 고정 ──────────────────────────────
                _rb.linearVelocity  *= ownerCollisionDamping;
                _rb.angularVelocity *= 0.3f;
            }
            else
            {
                // ── 비소유자 충돌: 충격 방향 제거 ──────────────────────────────
                Vector3 ballVel = _rb.linearVelocity;

                Vector3 contactNormal = Vector3.zero;
                foreach (var contact in collision.contacts)
                    contactNormal += contact.normal;
                if (collision.contactCount > 0)
                    contactNormal = (contactNormal / collision.contactCount).normalized;

                float impactComponent = Vector3.Dot(ballVel, -contactNormal);
                if (impactComponent > 0f)
                {
                    Vector3 canceledVel = ballVel - (-contactNormal) * impactComponent;
                    _rb.linearVelocity  = canceledVel * nonOwnerCollisionDamping;
                }
                else
                {
                    _rb.linearVelocity  *= nonOwnerCollisionDamping;
                }

                _rb.angularVelocity *= 0.4f;
            }
        }

        // =========================================================
        // Public API
        // =========================================================
        public void ResetAerodynamics(bool clearVelocity = true)
        {
            _currentMagnusForce = Vector3.zero;
            _currentDragForce = Vector3.zero;
            if (clearVelocity && _rb != null && !_rb.isKinematic)
            {
                _rb.angularVelocity = Vector3.zero;
                _rb.linearVelocity  = Vector3.zero;
            }
        }

        // =========================================================
        // Debug Visualization — §8.3
        // =========================================================
        private void OnDrawGizmos()
        {
            if (_rb == null || !Application.isPlaying) return;

            // 마그누스 힘 (마젠타)
            if (_currentMagnusForce.magnitude > 0.1f)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawRay(transform.position, _currentMagnusForce * 5f);
            }

            // 항력 (빨강)
            if (_currentDragForce.magnitude > 0.1f)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawRay(transform.position, _currentDragForce.normalized * 2f);
            }

            // 지면 접촉 표시
            if (_isGrounded)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(transform.position - Vector3.up * 0.1f, 0.15f);
            }
        }
    }
}