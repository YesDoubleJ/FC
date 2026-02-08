# 작업 목표: RootState_Attacking.cs 리팩토링 (God State 분해 및 로직 위임)

# 현재 상황 분석
`RootState_Attacking.cs` 파일이 약 1,700줄에 달하며, 단일 State 클래스가 과도한 책임을 지고 있습니다.
1. **SRP 위반:** 상태 전환뿐만 아니라, 오프더볼 위치 선정, 패스/슈팅 점수 계산, 마그누스 효과 계산, 골키퍼 전용 로직이 전부 하드코딩되어 있습니다.
2. **코드 중복:** 레이캐스트(Raycast)를 통한 시야 차단 확인 로직이 패스, 슈팅, 스루패스 로직에 반복해서 등장합니다.
3. **가독성 저하:** `GetSupportTarget_Striker`, `_Midfielder` 등 거대한 분기문이 클래스 내부를 차지하고 있어 흐름 파악이 어렵습니다.

# 요청 사항
이 코드를 **'전략 패턴(Strategy Pattern)'과 '헬퍼 클래스'**를 활용하여 분리해 주세요. 기능(Tuning Values)은 100% 보존하되, 구조를 아래와 같이 변경해 주세요.

## 1. 로직 분리 (Extract Logic Classes)
다음과 같이 별도의 C# 클래스(일반 클래스, MonoBehaviour 아님)로 로직을 추출하세요.

* **`AttackingTactics.cs`**:
    * **위치 선정 로직 담당.**
    * `MoveToSupportPosition`, `GetSupportTarget_Striker`, `GetSupportTarget_Midfielder`, `GetSupportTarget_Defender` 등 위치 계산 로직을 이곳으로 이동.
    * `SetSafeDestination` 포함.
* **`DecisionEvaluator.cs`**:
    * **의사결정 점수 계산 담당.**
    * `MakeDecision` 내부의 거대한 점수 산정 로직(Pass, Dribble, Shoot Score 계산)을 분리.
    * `FindBestDribbleTarget`, `CalculateEnemyDensity`, `Is1v1Situation` 포함.
    * `UtilityScorer`를 내부에서 활용하도록 구성.
* **`FootballPhysicsUtils.cs` (Static Helper)**:
    * **물리/기하학 연산 담당.**
    * `TryCurvedShot`, `IsFrontalBlocked`, `IsGoalAhead`, `GetSmartShootTarget` 등의 레이캐스트 및 벡터 연산 로직을 정적 메서드로 추출.

## 2. 상태 클래스 경량화 (Refactor State Class)
* **`RootState_Attacking.cs`**:
    * 위 3개의 클래스를 인스턴스로 생성(또는 주입)하여 사용합니다.
    * `RunAttackingLogic`에서는 `Evaluator`에게 "가장 좋은 행동이 뭐야?"라고 묻고, 결과에 따라 `agent.Shoot()`, `agent.Pass()` 등을 호출하는 **실행(Execution)** 역할만 남기세요.
    * 골키퍼(GK) 로직은 별도 메서드(`RunGKLogic`)로 명확히 분리하여 가독성을 높이세요.

## 3. 필수 유지 사항 (Constraints)
* **튜닝 값 보존:** `supportDist = 13f`, `shootThreshold = 0.5f`, `decisionInterval = 0.1f` 등 모든 하드코딩된 수치는 추출된 클래스로 정확히 옮겨야 합니다.
* **GK 특수 로직 유지:** 골키퍼가 공격 상황일 때 처리하는 `HighClearanceKick` 및 안전 지향 로직은 절대 누락되지 않도록 주의하세요.
* **Controller 연동:** `HybridAgentController`의 컴포넌트(`Mover`, `BallHandler`)를 적극 활용하세요.

# 출력 요청
먼저 리팩토링할 **새로운 파일 구조와 각 클래스의 역할(의사 코드)**을 먼저 설명하고, 내가 승인하면 실제 분리된 코드를 작성해 주세요.