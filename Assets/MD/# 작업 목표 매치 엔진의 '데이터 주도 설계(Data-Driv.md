# 작업 목표: 매치 엔진의 '데이터 주도 설계(Data-Driven Design)' 전환

# 현재 문제점
현재 `AgentMover`, `RootState_Attacking` 등의 코드에 `15f`, `0.5f` 같은 매직 넘버(하드코딩된 수치)가 산재해 있습니다. 이로 인해 밸런스 수정 시마다 코드를 재컴파일해야 해서 개발 속도가 느립니다.

# 요청 사항
모든 하드코딩된 튜닝 값들을 **`MatchEngineConfig`라는 ScriptableObject**로 추출하고, 인스펙터에서 조절 가능하게 리팩토링해 주세요.

## 1. `MatchEngineConfig.cs` 생성
다음 카테고리의 변수들을 포함하세요. (변수명은 직관적으로, `[Range]`와 `[Tooltip]` 필수)
* **AI 판단 기준:** `ShootThreshold`, `PassThreshold`, `DribbleScoreBias`
* **거리 기준:** `ShortPassRange`, `LongShotDistance`, `SupportDistance`
* **타이밍:** `DecisionInterval`, `ActionLockoutTime`
* **물리/이동:** `BaseMoveSpeed`, `RotationSpeed`, `DribbleDistance`

## 2. 기존 클래스 수정 (`HybridAgentController` 등)
* `HybridAgentController`에 `public MatchEngineConfig config;` 필드를 추가하세요.
* `RootState_Attacking`, `AgentMover` 등 하위 클래스에서 하드코딩된 숫자 대신 `agent.config.ShootThreshold`와 같이 참조하도록 변경하세요.

## 3. 결과물
* `MatchEngineConfig.cs` 코드
* Config를 적용하여 수정된 `RootState_Attacking.cs`의 주요 부분 (예시)