using UnityEngine;

namespace Game.Scripts.Physics
{
    [RequireComponent(typeof(Rigidbody))]
    public class BallAerodynamics : MonoBehaviour
    {
        [Header("Magnus Effect (Curve)")]
        [Tooltip("Higher = More Curve. 0.5 is realistic for pro shots.")]
        public float magnusCoefficient = 0.5f; // [수정] 0.03은 너무 약함, 0.5 정도로 올려야 휨

        [Header("Drag / Friction")]
        [Tooltip("Air resistance. 0.1 ~ 0.2 is good.")]
        public float airDrag = 0.15f; 
        
        [Tooltip("Ground friction. 0.5 ~ 1.0. Too high stops ball instantly.")]
        public float groundRollingDrag = 0.8f; 

        private Rigidbody _rb;
        private Vector3 _currentMagnusForce;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            
            if (_rb != null)
            {
                _rb.interpolation = RigidbodyInterpolation.Interpolate;
                _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                _rb.maxAngularVelocity = 100f; // 회전 속도 제한 (안정성 확보)
            }
        }

        private void Start()
        {
            // [삭제] Time.fixedDeltaTime 설정 코드 삭제 (MatchManager와 충돌 방지)
            // 물리 설정은 프로젝트 세팅이나 MatchManager에서 관리하는 게 맞습니다.
        }

        private void FixedUpdate()
        {
            // [중요] 공이 키네마틱(리셋 중, 골키퍼가 잡음 등) 상태면 물리 계산 중단
            if (_rb == null || _rb.isKinematic) 
            {
                _currentMagnusForce = Vector3.zero;
                return;
            }

            ApplyDrag();
            ApplyMagnusForce();
        }

        private void ApplyDrag()
        {
            bool isGrounded = UnityEngine.Physics.Raycast(transform.position, Vector3.down, 0.15f);
            _rb.linearDamping = isGrounded ? groundRollingDrag : airDrag;
        }

        private void ApplyMagnusForce()
        {
            if (_rb == null || _rb.isKinematic) return;

            float speed = _rb.linearVelocity.magnitude;
            // 속도가 너무 느리면(3m/s 미만) 커브 계산 안 함 (안정성 확보)
            if (speed < 3.0f) 
            {
                _currentMagnusForce = Vector3.zero;
                return;
            }

            Vector3 magnusDirection = Vector3.Cross(_rb.angularVelocity, _rb.linearVelocity);
            Vector3 finalForce = magnusCoefficient * magnusDirection;

            // [핵심 수정 1] 힘의 크기 제한 (Clamp)
            // 공 무게가 0.45kg이므로, 10N 이상의 힘은 비현실적인 급커브를 만듭니다.
            // 이 수치를 넘어가면 강제로 자릅니다.
            if (finalForce.magnitude > 10f)
            {
                finalForce = finalForce.normalized * 10f;
            }
            
            _currentMagnusForce = Vector3.Lerp(_currentMagnusForce, finalForce, Time.fixedDeltaTime * 5f);
            _rb.AddForce(_currentMagnusForce, ForceMode.Acceleration);
        }

        private void OnCollisionEnter(Collision collision)
        {
            // [핵심 수정] 태그가 없어도 에러가 나지 않도록 Try-Catch 대신 안전한 비교 사용
            // 혹은 태그 검사를 이름 검사나 레이어 검사로 대체 권장
            // 여기서는 에러 방지를 위해 태그 존재 여부를 확인하지 않고 단순히 물리적 반응만 처리
            
            // 땅에 닿으면 스핀 감소
            if (collision.gameObject.layer == LayerMask.NameToLayer("Ground") || collision.transform.position.y < 0.1f) 
            {
                _rb.angularVelocity *= 0.8f;
            }
            else
            {
                // [핵심 수정] 태그가 없어도 에러가 나지 않도록 Try-Catch 대신 안전한 비교 사용
                // CompareTag는 태그가 정의되지 않았으면 에러를 뱉음.
                string objName = collision.gameObject.name;
                if (objName.Contains("Net") || objName.Contains("Wall") || objName.Contains("Post") || objName.Contains("Goal"))
                {
                    ResetAerodynamics();
                }
            }
        }

        public void ResetAerodynamics()
        {
            _currentMagnusForce = Vector3.zero;
            if (_rb != null && !_rb.isKinematic)
            {
                _rb.angularVelocity = Vector3.zero;
                _rb.linearVelocity = Vector3.zero;
            }
        }

        private void OnDrawGizmos()
        {
            if (_rb != null && Application.isPlaying && _currentMagnusForce.magnitude > 0.1f)
            {
                // 커브 방향을 시각적으로 표시 (디버깅용)
                Gizmos.color = Color.magenta;
                Gizmos.DrawRay(transform.position, _currentMagnusForce * 5f);
            }
        }
    }
}