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
        if (collected || age < pickupDelay)
            return;

        APParticleCollector collector = APParticleCollector.Current;

        if (collector == null || !collector.CanReceiveAP())
        {
            return;
        }

        Vector2 toCollector = collector.Position - body.position;
        float distance = toCollector.magnitude;
        float magnetRange = collector.GetMagnetizationRange();

        if (distance <= collectionDistance)
        {
            int granted = collector.Collect(apValue);

            if (granted > 0)
            {
                collected = true;
                Remove();
            }

            return;
        }

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

    private void Remove()
    {
        if (owner != null)
            owner.Unregister(this);

        Destroy(gameObject);
    }
}
