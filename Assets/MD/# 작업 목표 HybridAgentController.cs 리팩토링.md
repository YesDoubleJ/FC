# 작업 목표: HybridAgentController.cs 리팩토링 (God Class 해체 및 구조 개선)

# 현재 상황 분석
현재 `HybridAgentController.cs`는 약 1,000줄에 달하며, 단일 클래스가 너무 많은 책임을 지고 있는 'God Class' 형태가 되었습니다.
1. **SRP 위반:** 이동, 물리 연산, 드리블/킥 로직, 스킬, 애니메이션, UI가 전부 한곳에 섞여 있습니다.
2. **상태 관리 충돌:** HFSM(StateMachine)이 존재함에도 불구하고, `Update()` 내부에서 `_isShooting`, `_isRecoveringBall` 등의 수많은 bool 플래그와 `if`문으로 로직을 제어하고 있어 유지보수가 불가능합니다.
3. **높은 결합도:** 물리 엔진과 로직이 강하게 결합되어 있어, 작은 수정이 다른 기능을 망가뜨릴 위험이 큽니다.

# 요청 사항
위 코드를 **기능별 컴포넌트로 분리(Component-Based Architecture)**하는 리팩토링을 수행해야 합니다. 기능(Tuning Values)은 유지하되, 구조를 아래와 같이 변경해 주세요.

## 1. 컴포넌트 분리 (Extract Classes)
다음과 같이 별도의 스크립트로 기능을 위임하세요. `HybridAgentController`는 이 컴포넌트들을 조율하는 'Brain' 역할만 수행해야 합니다.

* **`AgentMover.cs`**:
    * NavMeshAgent와 Rigidbody 동기화 로직 담당.
    * `MoveCharacter()`, `RotateCharacter()`, `SyncAgentToBody()` 포함.
    * 회전(Rotation)과 이동 속도 관련 변수 관리.
* **`AgentBallHandler.cs`**:
    * 공 소유 판정(`UpdatePossessionLogic`), 드리블(`DribbleAssist`), 킥/패스/슈팅의 물리적 실행(`ExecuteKickPhysics`, `ExecutePendingKick`) 담당.
    * 마그누스 효과 및 오차 원뿔(Error Cone) 시각화(`OnDrawGizmos`) 포함.
* **`AgentSkillSystem.cs`**:
    * `Tackle`, `ActivateDefenseBurst`, `ActivateBreakthrough` 등 특수 스킬 로직 및 쿨타임 관리 담당.

## 2. 상태 패턴 정상화 (Fix State Pattern Violation)
* **Bool 플래그 제거:** `Update` 문에서 `if (_isShooting)` 등으로 동작을 막는 방식을 제거하세요.
* **상태 위임:** 슈팅이나 패스 중일 때는 `RootState_Attacking` 내부의 하위 상태(Sub-State)나 별도의 `ActionState`로 전환하여 처리하도록 HFSM 구조를 활용하세요.

## 3. 필수 유지 사항 (Constraints)
* **튜닝 값 보존:** 코드 내에 `// User Req` 주석이 달린 하드코딩된 수치(거리 1.5f, 각도 30도, 힘 배율 등)는 기획 의도이므로 절대 변경하지 말고 그대로 새 컴포넌트에 옮기세요.
* **안전성:** 각 컴포넌트는 `RequireComponent`를 통해 의존성을 명시하고, `Awake`에서 서로를 확실하게 캐싱하세요.

# 출력 요청
먼저 리팩토링할 **새로운 파일 구조와 클래스 간의 관계(의사 코드)**를 먼저 설명하고, 내가 승인하면 실제 분리된 코드를 작성해 주세요.