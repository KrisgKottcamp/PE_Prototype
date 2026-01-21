using System.Collections;
using UnityEngine;

public class EnemyStunnable : MonoBehaviour
{
    [Header("Stun")]
    [SerializeField] private float defaultStunSeconds = 0.25f;

    [Header("Disable during stun")]
    [SerializeField] private MonoBehaviour[] disableScripts;

    [Header("Optional")]
    [SerializeField] private Rigidbody2D rb2d;

    private Coroutine stunRoutine;
    private float stunEndTime;

    public bool IsStunned { get; private set; }

    private void Awake()
    {
        if (rb2d == null) rb2d = GetComponent<Rigidbody2D>();
    }

    public void Stun(float seconds)
    {
        if (seconds <= 0f) seconds = defaultStunSeconds;

        // extend stun if already stunned
        float newEnd = Time.time + seconds;
        if (newEnd > stunEndTime) stunEndTime = newEnd;

        if (stunRoutine == null)
            stunRoutine = StartCoroutine(StunRoutine());
    }

    private IEnumerator StunRoutine()
    {
        IsStunned = true;

        if (rb2d != null)
        {
            rb2d.linearVelocity = Vector2.zero;
            rb2d.angularVelocity = 0f;
        }

        for (int i = 0; i < disableScripts.Length; i++)
            if (disableScripts[i] != null) disableScripts[i].enabled = false;

        while (Time.time < stunEndTime)
            yield return null;

        for (int i = 0; i < disableScripts.Length; i++)
            if (disableScripts[i] != null) disableScripts[i].enabled = true;

        IsStunned = false;
        stunRoutine = null;
    }
}
