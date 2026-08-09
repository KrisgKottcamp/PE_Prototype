using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [DisallowMultipleComponent]
    public sealed class StatusController : MonoBehaviour, ISpellStatusReceiver
    {
        private sealed class ActiveStatus
        {
            public StatusDefinition Definition;
            public SpellEffectContext SourceContext;
            public int Stacks;
            public float RemainingDuration;
            public float TimeUntilTick;
            public bool IsPermanent;
        }

        private readonly Dictionary<string, ActiveStatus> activeStatuses =
            new Dictionary<string, ActiveStatus>();
        private readonly List<string> keyBuffer = new List<string>();

        public event Action<StatusRuntimeContext> StatusApplied;
        public event Action<StatusRuntimeContext> StatusChanged;
        public event Action<StatusRuntimeContext> StatusRemoved;

        public int ActiveStatusCount => activeStatuses.Count;

        private void Update()
        {
            TickStatuses(Time.deltaTime, Time.unscaledDeltaTime);
        }

        private void OnDisable()
        {
            ClearStatuses(invokeRemovedEffects: true);
        }

        public bool HasStatus(StatusDefinition status)
        {
            return status != null &&
                   activeStatuses.ContainsKey(status.RuntimeKey);
        }

        public int GetStacks(StatusDefinition status)
        {
            return status != null &&
                   activeStatuses.TryGetValue(
                       status.RuntimeKey,
                       out ActiveStatus active)
                ? active.Stacks
                : 0;
        }

        public bool TryApplyStatus(
            in SpellStatusApplyRequest request,
            out SpellStatusResult result)
        {
            StatusDefinition definition = request.Status;
            if (definition == null)
            {
                result = default;
                return false;
            }

            string key = definition.RuntimeKey;
            float duration = request.Duration > 0f
                ? request.Duration
                : definition.DefaultDuration;

            if (!activeStatuses.TryGetValue(key, out ActiveStatus active))
            {
                active = new ActiveStatus
                {
                    Definition = definition,
                    SourceContext = request.EffectContext,
                    Stacks = Mathf.Min(
                        definition.MaximumStacks,
                        request.Stacks),
                    RemainingDuration = duration,
                    TimeUntilTick = definition.PeriodicInterval,
                    IsPermanent = duration <= 0f
                };
                activeStatuses.Add(key, active);

                StatusRuntimeContext runtime = ToRuntime(active);
                definition.InvokeApplied(runtime);
                StatusApplied?.Invoke(runtime);
                result = new SpellStatusResult(
                    0,
                    active.Stacks,
                    active.RemainingDuration);
                return true;
            }

            int previousStacks = active.Stacks;
            switch (definition.StackingMode)
            {
                case StatusStackingMode.IgnoreWhileActive:
                    result = new SpellStatusResult(
                        previousStacks,
                        active.Stacks,
                        active.RemainingDuration);
                    return false;

                case StatusStackingMode.AddStacksAndRefresh:
                    active.Stacks = Mathf.Min(
                        definition.MaximumStacks,
                        active.Stacks + request.Stacks);
                    break;

                case StatusStackingMode.ReplaceStacksAndRefresh:
                    active.Stacks = Mathf.Min(
                        definition.MaximumStacks,
                        request.Stacks);
                    break;
            }

            active.SourceContext = request.EffectContext;
            active.RemainingDuration = duration;
            active.IsPermanent = duration <= 0f;
            active.TimeUntilTick = definition.PeriodicInterval;

            StatusRuntimeContext changed = ToRuntime(active);
            definition.InvokeApplied(changed);
            StatusChanged?.Invoke(changed);
            result = new SpellStatusResult(
                previousStacks,
                active.Stacks,
                active.RemainingDuration);
            return true;
        }

        public bool TryRemoveStatus(
            StatusDefinition status,
            int stacksToRemove,
            out SpellStatusResult result)
        {
            if (status == null ||
                !activeStatuses.TryGetValue(
                    status.RuntimeKey,
                    out ActiveStatus active))
            {
                result = default;
                return false;
            }

            int previousStacks = active.Stacks;
            if (stacksToRemove > 0 && stacksToRemove < active.Stacks)
            {
                active.Stacks -= stacksToRemove;
                StatusRuntimeContext changed = ToRuntime(active);
                StatusChanged?.Invoke(changed);
                result = new SpellStatusResult(
                    previousStacks,
                    active.Stacks,
                    active.RemainingDuration);
                return true;
            }

            RemoveActiveStatus(status.RuntimeKey, active);
            result = new SpellStatusResult(previousStacks, 0, 0f);
            return true;
        }

        internal void TickStatuses(
            float scaledDeltaTime,
            float unscaledDeltaTime)
        {
            if (activeStatuses.Count == 0)
                return;

            keyBuffer.Clear();
            keyBuffer.AddRange(activeStatuses.Keys);

            for (int i = 0; i < keyBuffer.Count; i++)
            {
                string key = keyBuffer[i];
                if (!activeStatuses.TryGetValue(key, out ActiveStatus active))
                    continue;

                float delta = active.Definition.TimeMode ==
                              SpellTimeMode.Unscaled
                    ? Mathf.Max(0f, unscaledDeltaTime)
                    : Mathf.Max(0f, scaledDeltaTime);

                TickPeriodicEffects(key, active, delta);
                if (!activeStatuses.ContainsKey(key))
                    continue;

                if (active.IsPermanent)
                    continue;

                active.RemainingDuration -= delta;
                if (active.RemainingDuration <= 0f)
                    RemoveActiveStatus(key, active);
            }
        }

        public void ClearStatuses(bool invokeRemovedEffects)
        {
            if (activeStatuses.Count == 0)
                return;

            keyBuffer.Clear();
            keyBuffer.AddRange(activeStatuses.Keys);

            for (int i = 0; i < keyBuffer.Count; i++)
            {
                string key = keyBuffer[i];
                if (!activeStatuses.TryGetValue(key, out ActiveStatus active))
                    continue;

                activeStatuses.Remove(key);
                StatusRuntimeContext runtime = ToRuntime(active);

                if (invokeRemovedEffects)
                    active.Definition.InvokeRemoved(runtime);

                StatusRemoved?.Invoke(runtime);
            }
        }

        private void TickPeriodicEffects(
            string key,
            ActiveStatus active,
            float deltaTime)
        {
            float interval = active.Definition.PeriodicInterval;
            if (interval <= 0f)
                return;

            active.TimeUntilTick -= deltaTime;
            int safety = 0;

            while (active.TimeUntilTick <= 0f && safety < 8)
            {
                active.TimeUntilTick += interval;
                active.Definition.InvokePeriodic(ToRuntime(active));

                if (!activeStatuses.ContainsKey(key))
                    return;

                safety++;
            }
        }

        private void RemoveActiveStatus(string key, ActiveStatus active)
        {
            activeStatuses.Remove(key);
            StatusRuntimeContext runtime = ToRuntime(active);
            active.Definition.InvokeRemoved(runtime);
            StatusRemoved?.Invoke(runtime);
        }

        private StatusRuntimeContext ToRuntime(ActiveStatus active)
        {
            return new StatusRuntimeContext(
                active.Definition,
                gameObject,
                active.SourceContext,
                active.Stacks,
                active.RemainingDuration);
        }
    }
}
