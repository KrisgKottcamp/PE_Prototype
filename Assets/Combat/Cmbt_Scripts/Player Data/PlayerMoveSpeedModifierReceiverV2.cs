using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Collects temporary player movement modifiers from combat zones and buffs.
/// CombatPawnMover multiplies this value after the existing SpeedModifier and Focus multiplier,
/// so Speed Boost can stack with Slow Orb instead of one system overwriting the other.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerMoveSpeedModifierReceiverV2 : MonoBehaviour
{
    private enum ModifierKind
    {
        Slow,
        Boost,
        Generic
    }

    private struct ModifierSource
    {
        public float multiplier;
        public float expiresAt;
        public ModifierKind kind;
        public string sourceName;
    }

    [Header("Player Movement Modifier Receiver V2")]
    [Tooltip("Smallest final multiplier allowed from zone modifiers. Prevents accidental zero/negative values from freezing the player.")]
    [SerializeField, Min(0.01f)] private float minimumFinalMultiplier = 0.05f;

    [Tooltip("Largest final multiplier allowed from zone/buff modifiers. Speed Boost can still exceed normal speed, but this prevents extreme accidental values.")]
    [SerializeField, Min(1f)] private float maximumFinalMultiplier = 5f;

    [Tooltip("If true, applications are logged. Keep off unless debugging movement stacking.")]
    [SerializeField] private bool logApplications = false;

    [Header("Runtime Debug")]
    [SerializeField] private bool debugIsSlowed;
    [SerializeField] private bool debugHasReceiverBoost;
    [SerializeField] private float debugSlowMultiplier = 1f;
    [SerializeField] private float debugBoostMultiplier = 1f;
    [SerializeField] private float debugGenericMultiplier = 1f;
    [SerializeField] private float debugFinalMultiplier = 1f;
    [SerializeField] private string debugStrongestSlowSource = "None";
    [SerializeField] private string debugStrongestBoostSource = "None";

    private readonly Dictionary<int, ModifierSource> activeSources = new Dictionary<int, ModifierSource>();

    public bool IsSlowed => debugIsSlowed;
    public bool HasReceiverBoost => debugHasReceiverBoost;
    public float SlowMultiplier => debugSlowMultiplier;
    public float BoostMultiplier => debugBoostMultiplier;
    public float GenericMultiplier => debugGenericMultiplier;
    public float FinalMultiplier => debugFinalMultiplier;
    public string DebugStrongestSlowSource => debugStrongestSlowSource;
    public string DebugStrongestBoostSource => debugStrongestBoostSource;

    private void Awake()
    {
        RefreshDebug();
    }

    private void Update()
    {
        PruneExpired();
        RefreshDebug();
    }

    public void ApplySlow(Component source, float movementMultiplier, float lingerSeconds)
    {
        Apply(source, movementMultiplier, lingerSeconds, ModifierKind.Slow);
    }

    public void ApplyBoost(Component source, float movementMultiplier, float lingerSeconds)
    {
        Apply(source, movementMultiplier, lingerSeconds, ModifierKind.Boost);
    }

    public void ApplyGenericMultiplier(Component source, float movementMultiplier, float lingerSeconds)
    {
        Apply(source, movementMultiplier, lingerSeconds, ModifierKind.Generic);
    }

    public void ClearSource(Component source)
    {
        if (source == null)
            return;

        activeSources.Remove(source.GetInstanceID());
        RefreshDebug();
    }

    public void ClearAllModifiers()
    {
        activeSources.Clear();
        RefreshDebug();
    }

    private void Apply(Component source, float movementMultiplier, float lingerSeconds, ModifierKind kind)
    {
        if (source == null)
            return;

        float safeMultiplier;

        if (kind == ModifierKind.Slow)
            safeMultiplier = Mathf.Clamp(movementMultiplier, minimumFinalMultiplier, 1f);
        else if (kind == ModifierKind.Boost)
            safeMultiplier = Mathf.Clamp(movementMultiplier, 1f, maximumFinalMultiplier);
        else
            safeMultiplier = Mathf.Clamp(movementMultiplier, minimumFinalMultiplier, maximumFinalMultiplier);

        ModifierSource entry = new ModifierSource
        {
            multiplier = safeMultiplier,
            expiresAt = Time.time + Mathf.Max(0.02f, lingerSeconds),
            kind = kind,
            sourceName = source.name
        };

        activeSources[source.GetInstanceID()] = entry;
        RefreshDebug();

        if (logApplications)
        {
            Debug.Log(
                $"[PlayerMoveSpeedModifierReceiverV2] {name}: {kind} by {source.name}, move x{safeMultiplier:0.00}, linger {lingerSeconds:0.00}s",
                this
            );
        }
    }

    private void PruneExpired()
    {
        if (activeSources.Count == 0)
            return;

        List<int> expired = null;
        float now = Time.time;

        foreach (KeyValuePair<int, ModifierSource> pair in activeSources)
        {
            if (pair.Value.expiresAt <= now)
            {
                expired ??= new List<int>();
                expired.Add(pair.Key);
            }
        }

        if (expired == null)
            return;

        for (int i = 0; i < expired.Count; i++)
            activeSources.Remove(expired[i]);
    }

    private void RefreshDebug()
    {
        if (activeSources.Count == 0)
        {
            debugIsSlowed = false;
            debugHasReceiverBoost = false;
            debugSlowMultiplier = 1f;
            debugBoostMultiplier = 1f;
            debugGenericMultiplier = 1f;
            debugFinalMultiplier = 1f;
            debugStrongestSlowSource = "None";
            debugStrongestBoostSource = "None";
            return;
        }

        float slow = 1f;
        float boost = 1f;
        float generic = 1f;
        string slowSource = "None";
        string boostSource = "None";

        foreach (KeyValuePair<int, ModifierSource> pair in activeSources)
        {
            ModifierSource source = pair.Value;

            if (source.kind == ModifierKind.Slow)
            {
                if (source.multiplier < slow)
                {
                    slow = source.multiplier;
                    slowSource = source.sourceName;
                }
            }
            else if (source.kind == ModifierKind.Boost)
            {
                // Boosts from this receiver multiply together. Existing SpeedModifier-based Speed Boost
                // is still multiplied separately by CombatPawnMover.
                boost *= Mathf.Max(1f, source.multiplier);

                if (boostSource == "None" || source.multiplier > 1f)
                    boostSource = source.sourceName;
            }
            else
            {
                generic *= Mathf.Clamp(source.multiplier, minimumFinalMultiplier, maximumFinalMultiplier);
            }
        }

        boost = Mathf.Clamp(boost, 1f, maximumFinalMultiplier);
        generic = Mathf.Clamp(generic, minimumFinalMultiplier, maximumFinalMultiplier);

        debugSlowMultiplier = Mathf.Clamp(slow, minimumFinalMultiplier, 1f);
        debugBoostMultiplier = boost;
        debugGenericMultiplier = generic;
        debugFinalMultiplier = Mathf.Clamp(debugSlowMultiplier * debugBoostMultiplier * debugGenericMultiplier, minimumFinalMultiplier, maximumFinalMultiplier);
        debugIsSlowed = debugSlowMultiplier < 0.999f;
        debugHasReceiverBoost = debugBoostMultiplier > 1.001f;
        debugStrongestSlowSource = debugIsSlowed ? slowSource : "None";
        debugStrongestBoostSource = debugHasReceiverBoost ? boostSource : "None";
    }
}
