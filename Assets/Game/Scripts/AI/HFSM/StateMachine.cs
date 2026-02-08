using UnityEngine;

namespace Game.Scripts.AI.HFSM
{
    /// <summary>
    /// Manages the current state and transitions.
    /// </summary>
    public class StateMachine
    {
        public State CurrentState { get; private set; }
        public State PreviousState { get; private set; } // [추가] 이전 상태 기억

        // 디버깅용 (선수 머리 위에 상태 띄울 때 유용)
        public string CurrentStateName => CurrentState != null ? CurrentState.GetType().Name : "None";

        /// <summary>
        /// Initializes the StateMachine with a starting state.
        /// </summary>
        public void Initialize(State startingState)
        {
            CurrentState = startingState;
            CurrentState.Enter();
        }

        /// <summary>
        /// Transitions to a new state.
        /// </summary>
        public void ChangeState(State newState)
        {
            // [중요] 여기가 핵심입니다! newState가 Null이면 즉시 멈춰서 크래시를 막습니다.
            if (newState == null) 
            {
                Debug.LogError($"[StateMachine] Attempted to change to a NULL state!");
                return;
            }

            // [방어 코드] 같은 상태로 또 바꾸려고 하면 무시
            if (CurrentState == newState) return;

            if (CurrentState != null)
            {
                // 나가기 전에 이전 상태로 기록
                PreviousState = CurrentState; 
                CurrentState.Exit();
            }

            // 상태 교체
            CurrentState = newState;

            // [디버그] 상태 변화 로그 (필요시 주석 해제)
            // Debug.Log($"[FSM] State Changed: {PreviousState?.GetType().Name} -> {newState.GetType().Name}");

            CurrentState.Enter();
        }

        /// <summary>
        /// Should be called by the owner's Update().
        /// </summary>
        public void Update()
        {
            if (CurrentState != null)
            {
                CurrentState.Execute();
            }
        }

        /// <summary>
        /// Should be called by the owner's FixedUpdate().
        /// </summary>
        public void FixedUpdate()
        {
            if (CurrentState != null)
            {
                CurrentState.PhysicsExecute();
            }
        }

        // [추가] 이전 상태로 되돌리기 (예: 태클 피하고 다시 원래 하던 거 해)
        public void RevertToPreviousState()
        {
            if (PreviousState != null)
            {
                ChangeState(PreviousState);
            }
        }
    }
}
