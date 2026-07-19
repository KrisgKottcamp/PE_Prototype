using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
public class APParticlePickup : MonoBehaviour
{
    private APParticleSystem owner;
    private Rigidbody2D body;
    private SpriteRenderer visual;
    private int apValue;
    private float age;
    private float pickupDelay;
    private float lifetime;
    private float blinkDuration;
    private float magnetAcceleration;
    private float maximumMagnetSpeed;
    private float collectionDistance;
    private bool collected;
    private float externalCollectorPullUntil = -1f;
    private float externalPullAcceleration;
    private float externalMaximumPullSpeed;

    public Vector2 Position => body != null
        ? body.position
        : (Vector2)transform.position;

    public void Configure(
        APParticleSystem system,
        int value,
        Vector2 initialVelocity,
        float delay,
        float life,
        float blink,
        float acceleration,
        float maximumSpeed,
        float collectDistance)
    {
        owner = system;
        apValue = Mathf.Max(1, value);
        pickupDelay = Mathf.Max(0f, delay);
        lifetime = Mathf.Max(0.1f, life);
        blinkDuration = Mathf.Clamp(blink, 0f, lifetime);
        magnetAcceleration = Mathf.Max(0f, acceleration);
        maximumMagnetSpeed = Mathf.Max(0.01f, maximumSpeed);
        collectionDistance = Mathf.Max(0.01f, collectDistance);

        body = GetComponent<Rigidbody2D>();
        visual = GetComponent<SpriteRenderer>();
        body.linearVelocity = initialVelocity;
    }

    private void Update()
    {
        if (collected)
            return;

        age += Time.deltaTime;

        if (age >= lifetime)
        {
            Remove();
            return;
        }

        if (visual != null && blinkDuration > 0f &&
            age >= lifetime - blinkDuration)
        {
            float blinkRate = Mathf.Lerp(
                7f,
                20f,
                Mathf.InverseLerp(
                    lifetime - blinkDuration,
                    lifetime,
                    age
                )
            );

            visual.enabled =
                Mathf.Repeat(age * blinkRate, 1f) < 0.58f;
        }
    }

    private void FixedUpdate()
    {
        if (collected)
            return;

        APParticleCollector collector = APParticleCollector.Current;

        if (collector == null || !collector.CanReceiveAP())
        {
            return;
        }

        Vector2 toCollector = collector.Position - body.position;
        float distance = toCollector.magnitude;

        if (age >= pickupDelay && distance <= collectionDistance)
        {
            int granted = collector.Collect(apValue);

            if (granted > 0)
            {
                collected = true;
                Remove();
            }

            return;
        }

        if (Time.time < externalCollectorPullUntil)
        {
            PullTowardCollector(
                toCollector,
                externalPullAcceleration,
                externalMaximumPullSpeed
            );

            return;
        }

        if (age < pickupDelay)
            return;

        float magnetRange = collector.GetMagnetizationRange();

        if (magnetRange <= 0f || distance > magnetRange)
        {
            return;
        }

        float rangeProgress = 1f - Mathf.Clamp01(distance / magnetRange);
        float speed = Mathf.Lerp(
            maximumMagnetSpeed * 0.35f,
            maximumMagnetSpeed,
            rangeProgress
        );

        Vector2 desiredVelocity =
            toCollector.normalized * speed;

        body.linearVelocity = Vector2.MoveTowards(
            body.linearVelocity,
            desiredVelocity,
            magnetAcceleration * Time.fixedDeltaTime
        );
    }

    /// <summary>
    /// Temporarily strengthens this pickup's pull toward the current AP collector.
    /// The collector still decides whether and which active character receives AP.
    /// </summary>
    public bool RequestExternalCollectorPull(
        float acceleration,
        float maximumSpeed,
        float duration)
    {
        if (collected || duration <= 0f)
            return false;

        if (Time.time >= externalCollectorPullUntil)
        {
            externalPullAcceleration = 0f;
            externalMaximumPullSpeed = 0f;
        }

        externalCollectorPullUntil = Mathf.Max(
            externalCollectorPullUntil,
            Time.time + duration
        );

        externalPullAcceleration = Mathf.Max(
            externalPullAcceleration,
            Mathf.Max(0f, acceleration)
        );

        externalMaximumPullSpeed = Mathf.Max(
            externalMaximumPullSpeed,
            Mathf.Max(0.01f, maximumSpeed)
        );

        return true;
    }

    private void PullTowardCollector(
        Vector2 toCollector,
        float acceleration,
        float maximumSpeed)
    {
        if (toCollector.sqrMagnitude <= 0.0001f)
            return;

        Vector2 desiredVelocity =
            toCollector.normalized * Mathf.Max(0.01f, maximumSpeed);

        body.linearVelocity = Vector2.MoveTowards(
            body.linearVelocity,
            desiredVelocity,
            Mathf.Max(0f, acceleration) * Time.fixedDeltaTime
        );
    }

    private void Remove()
    {
        if (owner != null)
            owner.Unregister(this);

        Destroy(gameObject);
    }
}
