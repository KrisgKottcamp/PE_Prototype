using System.Collections;
using UnityEngine;

public class HitMorph : MonoBehaviour
{
    [Header("Morph")]
    [SerializeField] private Vector3 hitScale = new Vector3(1.15f, 0.85f, 1f);
    [SerializeField] private float inTime = 0.05f;
    [SerializeField] private float outTime = 0.08f;

    [Header("Optional")]
    [SerializeField] private bool alsoFlash = false;
    [SerializeField] private Color flashColor = Color.white;
    [SerializeField] private float flashTime = 0.06f;

    private Vector3 baseScale;
    private Coroutine routine;

    private SpriteRenderer sr;
    private Color baseColor;

    private void Awake()
    {
        baseScale = transform.localScale;
        sr = GetComponent<SpriteRenderer>();
        if (sr != null) baseColor = sr.color;
    }

    public void Play()
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(MorphRoutine());
    }

    private IEnumerator MorphRoutine()
    {
        // Instant reset to avoid drift if spammed
        transform.localScale = baseScale;

        // Optional flash
        if (alsoFlash && sr != null)
        {
            sr.color = flashColor;
            // flash runs alongside morph
            StartCoroutine(FlashRoutine());
        }

        // Ease in to hit scale
        yield return LerpScale(baseScale, Multiply(baseScale, hitScale), inTime);

        // Ease back to normal
        yield return LerpScale(transform.localScale, baseScale, outTime);

        transform.localScale = baseScale;
        routine = null;
    }

    private IEnumerator FlashRoutine()
    {
        yield return new WaitForSeconds(flashTime);
        if (sr != null) sr.color = baseColor;
    }

    private IEnumerator LerpScale(Vector3 from, Vector3 to, float time)
    {
        if (time <= 0f)
        {
            transform.localScale = to;
            yield break;
        }

        float t = 0f;
        while (t < time)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / time);

            // simple ease-out
            k = 1f - Mathf.Pow(1f - k, 2f);

            transform.localScale = Vector3.LerpUnclamped(from, to, k);
            yield return null;
        }

        transform.localScale = to;
    }

    private static Vector3 Multiply(Vector3 a, Vector3 b)
        => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
}
