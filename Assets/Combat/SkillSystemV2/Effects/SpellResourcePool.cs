using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    [DisallowMultipleComponent]
    public sealed class SpellResourcePool : MonoBehaviour,
        ISpellResourceProvider,
        ISpellResourceReceiver
    {
        [Serializable]
        private sealed class ResourceEntry
        {
            [SerializeField]
            private GameplayResourceDefinition definition;

            [Tooltip("Used only when no Gameplay Resource asset is assigned.")]
            [SerializeField]
            private string fallbackResourceId = SpellResourceCost.ActionPoints;

            [SerializeField, Min(0f)]
            private float currentValue;

            [Tooltip("Zero uses the Gameplay Resource asset's default maximum.")]
            [SerializeField, Min(0f)]
            private float maximumOverride;

            public GameplayResourceDefinition Definition => definition;
            public string ResourceId => definition != null
                ? definition.ResourceId
                : string.IsNullOrWhiteSpace(fallbackResourceId)
                    ? SpellResourceCost.ActionPoints
                    : fallbackResourceId.Trim();
            public float Minimum => definition != null
                ? definition.MinimumValue
                : 0f;
            public float Maximum => maximumOverride > 0f
                ? Mathf.Max(Minimum, maximumOverride)
                : definition != null
                    ? definition.DefaultMaximumValue
                    : 100f;
            public float Current
            {
                get => currentValue;
                set => currentValue = value;
            }

            public ResourceEntry(GameplayResourceDefinition resource)
            {
                definition = resource;
                fallbackResourceId = resource != null
                    ? resource.ResourceId
                    : SpellResourceCost.ActionPoints;
                currentValue = resource != null
                    ? resource.MinimumValue
                    : 0f;
                maximumOverride = 0f;
            }

            public void ClampCurrent()
            {
                currentValue = Mathf.Clamp(
                    currentValue,
                    Minimum,
                    Maximum);
            }
        }

        [SerializeField]
        private List<ResourceEntry> resources = new List<ResourceEntry>();

        private readonly Dictionary<string, ResourceEntry> entries =
            new Dictionary<string, ResourceEntry>(StringComparer.OrdinalIgnoreCase);

        public event Action<string, float, float> ResourceChanged;

        private void Awake()
        {
            RebuildEntries();
        }

        public float GetCurrent(string resourceId)
        {
            EnsureEntries();
            return entries.TryGetValue(NormalizeId(resourceId), out ResourceEntry entry)
                ? entry.Current
                : 0f;
        }

        public float GetMaximum(string resourceId)
        {
            EnsureEntries();
            return entries.TryGetValue(NormalizeId(resourceId), out ResourceEntry entry)
                ? entry.Maximum
                : 0f;
        }

        public bool CanSpend(in SpellResourceCost cost)
        {
            if (cost.IsFree)
                return true;

            EnsureEntries();
            return entries.TryGetValue(cost.ResourceId, out ResourceEntry entry) &&
                   entry.Current + 0.0001f >= cost.Amount;
        }

        public bool TrySpend(in SpellResourceCost cost)
        {
            if (cost.IsFree)
                return true;

            if (!CanSpend(cost) ||
                !entries.TryGetValue(cost.ResourceId, out ResourceEntry entry))
            {
                return false;
            }

            SetValue(entry, entry.Current - cost.Amount, clampToMaximum: true);
            return true;
        }

        public void Refund(in SpellResourceCost cost)
        {
            if (cost.IsFree)
                return;

            EnsureEntries();
            if (entries.TryGetValue(cost.ResourceId, out ResourceEntry entry))
                SetValue(entry, entry.Current + cost.Amount, clampToMaximum: true);
        }

        public bool TryChangeResource(
            in SpellResourceChangeRequest request,
            out SpellResourceChangeResult result)
        {
            EnsureEntries();
            string id = NormalizeId(request.ResourceId);

            if (!entries.TryGetValue(id, out ResourceEntry entry))
            {
                if (request.Resource == null)
                {
                    result = default;
                    return false;
                }

                entry = new ResourceEntry(request.Resource);
                resources.Add(entry);
                entries.Add(id, entry);
            }

            float before = entry.Current;
            float requestedValue;

            switch (request.Operation)
            {
                case SpellResourceOperation.Remove:
                    requestedValue = before - request.Amount;
                    break;
                case SpellResourceOperation.Set:
                    requestedValue = request.Amount;
                    break;
                default:
                    requestedValue = before + request.Amount;
                    break;
            }

            SetValue(
                entry,
                requestedValue,
                clampToMaximum: !request.AllowOverflow);
            result = new SpellResourceChangeResult(before, entry.Current);
            return !Mathf.Approximately(before, entry.Current);
        }

        private void SetValue(
            ResourceEntry entry,
            float value,
            bool clampToMaximum)
        {
            float before = entry.Current;
            entry.Current = clampToMaximum
                ? Mathf.Clamp(value, entry.Minimum, entry.Maximum)
                : Mathf.Max(entry.Minimum, value);

            if (!Mathf.Approximately(before, entry.Current))
            {
                ResourceChanged?.Invoke(
                    entry.ResourceId,
                    entry.Current,
                    entry.Maximum);
            }
        }

        private void EnsureEntries()
        {
            if (entries.Count == 0 && resources.Count > 0)
                RebuildEntries();
        }

        private void RebuildEntries()
        {
            entries.Clear();
            resources ??= new List<ResourceEntry>();

            for (int i = 0; i < resources.Count; i++)
            {
                ResourceEntry entry = resources[i];
                if (entry == null)
                    continue;

                entry.ClampCurrent();
                string id = NormalizeId(entry.ResourceId);
                if (!entries.ContainsKey(id))
                    entries.Add(id, entry);
            }
        }

        private static string NormalizeId(string resourceId)
        {
            return string.IsNullOrWhiteSpace(resourceId)
                ? SpellResourceCost.ActionPoints
                : resourceId.Trim();
        }

        private void OnValidate()
        {
            resources ??= new List<ResourceEntry>();
            for (int i = 0; i < resources.Count; i++)
                resources[i]?.ClampCurrent();
        }
    }
}
