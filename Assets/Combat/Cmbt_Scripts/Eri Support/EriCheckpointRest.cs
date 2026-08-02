using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Small bridge for future dungeon/village checkpoints.
/// Call Rest() from the checkpoint interaction after the player confirms.
/// The UnityEvent should be wired to the dungeon's standard-enemy respawner.
/// </summary>
public sealed class EriCheckpointRest : MonoBehaviour
{
    [SerializeField]
    private UnityEvent onRested;

    public void Rest()
    {
        EriSupportManager support =
            EriSupportManager.Instance;

        if (support == null)
        {
            Debug.LogWarning(
                "EriCheckpointRest: EriSupportManager is missing.",
                this
            );
            return;
        }

        support.RestoreAtCheckpoint();
        onRested?.Invoke();
    }
}
