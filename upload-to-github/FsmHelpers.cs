using System;
using System.Linq;
using HutongGames.PlayMaker;

namespace PlayableVessel
{
    /// <summary>A PlayMaker action that runs C# callbacks.</summary>
    internal class CallbackAction : FsmStateAction
    {
        public Action<CallbackAction> Enter;
        public Action<CallbackAction> Update;
        public bool FinishOnEnter = true;

        public override void OnEnter()
        {
            Enter?.Invoke(this);
            if (FinishOnEnter && Update == null) Finish();
        }

        public override void OnUpdate() => Update?.Invoke(this);

        public void SendEvent(string name) => Fsm.Event(FsmEvent.GetFsmEvent(name));
    }

    internal static class FsmHelpers
    {
        public static FsmState GetState(this PlayMakerFSM fsm, string name) =>
            fsm.FsmStates.FirstOrDefault(s => s.Name == name);

        public static void InsertAction(this FsmState state, int index, FsmStateAction action)
        {
            var list = state.Actions.ToList();
            index = Math.Max(0, Math.Min(index, list.Count));
            list.Insert(index, action);
            state.Actions = list.ToArray();
            action.Init(state);
        }

        public static FsmState AddState(this PlayMakerFSM fsm, string name)
        {
            var existing = fsm.GetState(name);
            if (existing != null) return existing;
            var state = new FsmState(fsm.Fsm) { Name = name };
            fsm.Fsm.States = fsm.FsmStates.Concat(new[] { state }).ToArray();
            return state;
        }

        public static void AddTransition(this FsmState from, string eventName, string toState)
        {
            var ev = FsmEvent.GetFsmEvent(eventName);
            var fsm = from.Fsm;
            if (!fsm.Events.Contains(ev)) fsm.Events = fsm.Events.Concat(new[] { ev }).ToArray();
            var tr = new FsmTransition { FsmEvent = ev, ToState = toState, ToFsmState = fsm.GetState(toState) };
            from.Transitions = from.Transitions.Concat(new[] { tr }).ToArray();
        }

        public static FsmInt GetInt(this PlayMakerFSM fsm, string name) => fsm.FsmVariables.FindFsmInt(name);
    }
}
