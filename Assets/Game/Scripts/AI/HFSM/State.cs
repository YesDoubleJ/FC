using UnityEngine;

namespace Game.Scripts.AI.HFSM
{
    /// <summary>
    /// Abstract base class for all states in the Hierarchical Finite State Machine.
    /// </summary>
    public abstract class State
    {
        protected HybridAgentController agent;
        protected StateMachine stateMachine;

        /// <summary>
        /// Constructor to inject dependencies.
        /// </summary>
        protected State(HybridAgentController agent, StateMachine stateMachine)
        {
            this.agent = agent;
            this.stateMachine = stateMachine;
        }

        /// <summary>
        /// Called when entering the state.
        /// </summary>
        public virtual void Enter() { }

        /// <summary>
        /// Called every frame (Update). Use for logic, timers, etc.
        /// </summary>
        public virtual void Execute() { }

        /// <summary>
        /// Called every physics frame (FixedUpdate). Use for physics-based movement.
        /// </summary>
        public virtual void PhysicsExecute() { }

        /// <summary>
        /// Called when exiting the state.
        /// </summary>
        public virtual void Exit() { }
    }
}
