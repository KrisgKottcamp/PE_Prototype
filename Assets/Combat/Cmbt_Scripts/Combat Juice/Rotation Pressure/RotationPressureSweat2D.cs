using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Emits pooled, curved anime-style sweat streaks around the active pawn's
/// head. The droplet sprite is generated at runtime, so no prefab or imported
/// texture is required. This component is presentation-only.
/// </summary>
[DisallowMultipleComponent]
public sealed class RotationPressureSweat2D : MonoBehaviour
{
    private sealed class SweatDrop
    {
        public Transform root;
        public SpriteRenderer renderer;
        public Vector2 velocity;
        public float age;
        public float lifetime;
        public float startingSize;
        public float startingRotation;
        public bool active;
    }

    private struct PendingDrop
    {
        public int side;
        public int burstIndex;
        public float burstAngle;
        public float emissionTime;
    }

    [Header("Placement")]
    [SerializeField] private Vector2 headLocalOffset =
        new Vector2(0f, 0.68f);
    [SerializeField, Min(0f)] private float horizontalSpawnOffset = 0.25f;
    [SerializeField, Min(0f)] private float verticalSpawnJitter = 0.07f;

    [Header("Emission")]
    [SerializeField, Min(0.05f)] private float minimumDropsPerSecond = 0.75f;
    [SerializeField, Min(0.05f)] private float maximumDropsPerSecond = 1.75f;
    [SerializeField, Min(1)] private int maximumActiveDrops = 24;
    [SerializeField] private Vector2 lifetimeRange =
        new Vector2(0.62f, 0.88f);
    [SerializeField] private Vector2 sizeRange =
        new Vector2(0.18f, 0.25f);

    [Header("Motion")]
    [SerializeField] private Vector2 outwardSpeedRange =
        new Vector2(0.20f, 0.38f);
    [SerializeField] private Vector2 upwardSpeedRange =
        new Vector2(0.26f, 0.46f);
    [SerializeField, Min(0f)] private float downwardAcceleration = 0.42f;
    [Tooltip("Angle difference between additional same-side droplets in one burst.")]
    [SerializeField, Range(4f, 35f)] private float burstAngleStep = 18f;
    [SerializeField, Range(10f, 80f)] private float maximumBurstAngle = 54f;
    [SerializeField, Min(0f)] private float burstSpawnSpacing = 0.035f;
    [Tooltip("Small local-position variation applied only to multi-drop bursts.")]
    [SerializeField] private Vector2 multipleDropSpawnJitter =
        new Vector2(0.025f, 0.035f);
    [Tooltip("Gently staggers each drop in a burst vertically before random jitter.")]
    [SerializeField, Min(0f)] private float multipleDropVerticalStagger = 0.018f;
    [Tooltip("Real-time delay between each drop in a multi-drop burst.")]
    [SerializeField, Range(0f, 0.2f)] private float multipleDropTimeStagger = 0.055f;
    [Tooltip("Small random variation added to each staggered drop's delay.")]
    [SerializeField, Range(0f, 0.05f)] private float multipleDropTimeJitter = 0.012f;

    [Header("Appearance")]
    [SerializeField] private Sprite spriteOverride;
    [SerializeField] private Color sweatColor =
        new Color(1f, 1f, 1f, 0.88f);
    [SerializeField] private string sortingLayerName = "Foreground";
    [SerializeField] private int sortingOrder = 165;

    [Header("Runtime Debug")]
    [SerializeField, Range(0f, 1f)] private float pressure;
    [SerializeField, Min(1)] private int dropsPerEmission = 1;
    [SerializeField] private int activeDropCount;

    private readonly List<SweatDrop> drops =
        new List<SweatDrop>(8);
    private readonly List<PendingDrop> pendingDrops =
        new List<PendingDrop>(16);

    private Transform worldRoot;
    private Texture2D dropletTexture;
    private Sprite dropletSprite;
    private float emissionAccumulator;
    private int nextSide = 1;
    private bool wasEmitting;

    public void Configure(
        Sprite newSpriteOverride,
        Vector2 newHeadLocalOffset,
        Color newSweatColor,
        Vector2 newSizeRange,
        float minimumRate,
        float maximumRate,
        string newSortingLayerName,
        int newSortingOrder)
    {
        spriteOverride = newSpriteOverride;
        headLocalOffset = newHeadLocalOffset;
        sweatColor = newSweatColor;
        sizeRange = new Vector2(
            Mathf.Max(0.04f, newSizeRange.x),
            Mathf.Max(Mathf.Max(0.04f, newSizeRange.x), newSizeRange.y));
        minimumDropsPerSecond = Mathf.Max(0.05f, minimumRate);
        maximumDropsPerSecond = Mathf.Max(
            minimumDropsPerSecond,
            maximumRate);

        if (!string.IsNullOrWhiteSpace(newSortingLayerName))
            sortingLayerName = newSortingLayerName;

        sortingOrder = newSortingOrder;
        EnsureVisualResources();
        ApplyRendererSettings();
    }

    public void SetPressure(
        float normalizedPressure,
        bool clearImmediately = false,
        int newDropsPerEmission = 1)
    {
        int previousBurstSize = dropsPerEmission;
        pressure = Mathf.Clamp01(normalizedPressure);
        dropsPerEmission = Mathf.Clamp(
            newDropsPerEmission,
            1,
            Mathf.Max(1, maximumActiveDrops));

        if (pressure > 0.001f &&
            dropsPerEmission > previousBurstSize)
        {
            // Make each newly resolved skill readable immediately: the next
            // frame emits a burst using the newly increased droplet count.
            emissionAccumulator = 0f;
            wasEmitting = false;
            pendingDrops.Clear();
        }

        if (clearImmediately)
            ClearActiveDrops();
    }

    private void Awake()
    {
        EnsureVisualResources();
    }

    private void Update()
    {
        EnsureVisualResources();
        TickPendingDrops();
        TickEmission();
        TickDrops();
    }

    private void TickEmission()
    {
        if (pressure <= 0.001f)
        {
            emissionAccumulator = 0f;
            wasEmitting = false;
            pendingDrops.Clear();
            return;
        }

        if (!wasEmitting)
        {
            EmitBurst();
            wasEmitting = true;
        }

        float rate = Mathf.Lerp(
            minimumDropsPerSecond,
            maximumDropsPerSecond,
            pressure);

        emissionAccumulator += Time.unscaledDeltaTime * rate;

        while (emissionAccumulator >= 1f)
        {
            emissionAccumulator -= 1f;
            EmitBurst();
        }
    }

    private void EmitBurst()
    {
        int burstCount = Mathf.Clamp(
            dropsPerEmission,
            1,
            Mathf.Max(1, maximumActiveDrops));

        for (int i = 0; i < burstCount; i++)
        {
            int side = i % 2 == 0
                ? nextSide
                : -nextSide;

            float angle = CalculateBurstAngle(i, side);

            if (i == 0 || multipleDropTimeStagger <= 0f)
            {
                EmitDrop(side, i, angle);
                continue;
            }

            float delay =
                i * multipleDropTimeStagger +
                Random.Range(
                    -multipleDropTimeJitter,
                    multipleDropTimeJitter);

            pendingDrops.Add(new PendingDrop
            {
                side = side,
                burstIndex = i,
                burstAngle = angle,
                emissionTime =
                    Time.unscaledTime +
                    Mathf.Max(0.01f, delay)
            });
        }

        nextSide *= -1;
    }

    private void TickPendingDrops()
    {
        float now = Time.unscaledTime;

        for (int i = pendingDrops.Count - 1; i >= 0; i--)
        {
            PendingDrop pending = pendingDrops[i];

            if (now < pending.emissionTime)
                continue;

            EmitDrop(
                pending.side,
                pending.burstIndex,
                pending.burstAngle);
            pendingDrops.RemoveAt(i);
        }
    }

    private float CalculateBurstAngle(
        int burstIndex,
        int side)
    {
        int sameSideLayer = burstIndex / 2;

        if (sameSideLayer <= 0)
            return 0f;

        int magnitudeStep =
            (sameSideLayer + 1) / 2;
        float alternatingDirection =
            sameSideLayer % 2 == 1
                ? 1f
                : -1f;
        float localAngle = Mathf.Min(
            maximumBurstAngle,
            magnitudeStep * burstAngleStep) *
            alternatingDirection;

        // Mirror the fan so left- and right-side drops remain symmetrical.
        return side * localAngle;
    }

    private void EmitDrop(
        int side,
        int burstIndex,
        float burstAngle)
    {
        SweatDrop drop = GetAvailableDrop();

        if (drop == null)
            return;

        int sameSideLayer = burstIndex / 2;
        Vector2 multipleDropOffset = Vector2.zero;

        if (dropsPerEmission > 1)
        {
            float centeredIndex =
                burstIndex -
                (dropsPerEmission - 1) * 0.5f;

            multipleDropOffset = new Vector2(
                Random.Range(
                    -Mathf.Abs(multipleDropSpawnJitter.x),
                    Mathf.Abs(multipleDropSpawnJitter.x)),
                centeredIndex * multipleDropVerticalStagger +
                Random.Range(
                    -Mathf.Abs(multipleDropSpawnJitter.y),
                    Mathf.Abs(multipleDropSpawnJitter.y)));
        }

        Vector2 localSpawn =
            headLocalOffset +
            new Vector2(
                side * (
                    horizontalSpawnOffset +
                    sameSideLayer * burstSpawnSpacing),
                Random.Range(-verticalSpawnJitter, verticalSpawnJitter)) +
            multipleDropOffset;

        drop.root.position = transform.TransformPoint(localSpawn);
        Vector2 localVelocity = new Vector2(
            side * Random.Range(outwardSpeedRange.x, outwardSpeedRange.y),
            Random.Range(upwardSpeedRange.x, upwardSpeedRange.y));
        localVelocity = Quaternion.Euler(0f, 0f, burstAngle) * localVelocity;
        drop.velocity = transform.TransformVector(localVelocity);
        drop.age = 0f;
        drop.lifetime = Random.Range(
            Mathf.Max(0.1f, lifetimeRange.x),
            Mathf.Max(Mathf.Max(0.1f, lifetimeRange.x), lifetimeRange.y));
        drop.startingSize = Random.Range(sizeRange.x, sizeRange.y) *
            Mathf.Lerp(0.94f, 1.08f, pressure);
        drop.startingRotation =
            burstAngle +
            Random.Range(-5f, 5f);
        drop.root.rotation = Quaternion.Euler(0f, 0f, drop.startingRotation);
        drop.renderer.flipX = side < 0;
        drop.active = true;
        drop.root.gameObject.SetActive(true);
        activeDropCount++;

        ApplyDropVisual(drop, 0f);
    }

    private SweatDrop GetAvailableDrop()
    {
        for (int i = 0; i < drops.Count; i++)
        {
            if (!drops[i].active)
                return drops[i];
        }

        if (drops.Count >= Mathf.Max(1, maximumActiveDrops))
            return null;

        return CreateDrop();
    }

    private SweatDrop CreateDrop()
    {
        GameObject rootObject = new GameObject(
            $"RotationPressureSweat_{drops.Count + 1}");
        rootObject.transform.SetParent(worldRoot, false);

        SpriteRenderer renderer = rootObject.AddComponent<SpriteRenderer>();
        renderer.sprite = GetActiveSprite();
        renderer.sortingLayerName = sortingLayerName;
        renderer.sortingOrder = sortingOrder;

        SweatDrop drop = new SweatDrop
        {
            root = rootObject.transform,
            renderer = renderer
        };

        rootObject.SetActive(false);
        drops.Add(drop);
        return drop;
    }

    private void TickDrops()
    {
        float deltaTime = Time.unscaledDeltaTime;

        for (int i = 0; i < drops.Count; i++)
        {
            SweatDrop drop = drops[i];

            if (!drop.active)
                continue;

            drop.age += deltaTime;

            if (drop.age >= drop.lifetime)
            {
                RecycleDrop(drop);
                continue;
            }

            drop.velocity += Vector2.down * downwardAcceleration * deltaTime;
            drop.root.position += (Vector3)(drop.velocity * deltaTime);

            float horizontalDirection = Mathf.Sign(drop.velocity.x);
            float tilt = Mathf.Lerp(0f, -horizontalDirection * 12f,
                drop.age / drop.lifetime);
            drop.root.rotation = Quaternion.Euler(
                0f,
                0f,
                drop.startingRotation + tilt);

            ApplyDropVisual(drop, drop.age / drop.lifetime);
        }
    }

    private void ApplyDropVisual(
        SweatDrop drop,
        float normalizedAge)
    {
        float fadeIn = Mathf.SmoothStep(
            0f,
            1f,
            Mathf.InverseLerp(0f, 0.10f, normalizedAge));
        float fadeOut = 1f - Mathf.SmoothStep(
            0f,
            1f,
            Mathf.InverseLerp(0.55f, 1f, normalizedAge));
        float alpha = fadeIn * fadeOut;

        float pop = Mathf.Lerp(
            0.72f,
            1f,
            Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(0f, 0.14f, normalizedAge)));

        drop.root.localScale = Vector3.one * drop.startingSize * pop;

        Color color = sweatColor;
        color.a *= alpha;
        drop.renderer.color = color;
    }

    private void RecycleDrop(SweatDrop drop)
    {
        drop.active = false;
        drop.root.gameObject.SetActive(false);
        activeDropCount = Mathf.Max(0, activeDropCount - 1);
    }

    private void EnsureVisualResources()
    {
        if (worldRoot == null)
        {
            GameObject rootObject = new GameObject(
                $"{name}_RotationPressureSweat");
            worldRoot = rootObject.transform;
            worldRoot.position = Vector3.zero;
        }

        if (dropletSprite != null)
            return;

        CreateCurvedSweatSprite();
        ApplyRendererSettings();
    }

    private void CreateCurvedSweatSprite()
    {
        const int textureSize = 64;
        const int curveSamples = 32;

        dropletTexture = new Texture2D(
            textureSize,
            textureSize,
            TextureFormat.RGBA32,
            false,
            true);
        dropletTexture.name = "White Curved Sweat Streak";
        dropletTexture.filterMode = FilterMode.Bilinear;
        dropletTexture.wrapMode = TextureWrapMode.Clamp;
        dropletTexture.hideFlags = HideFlags.HideAndDontSave;

        Color[] pixels = new Color[textureSize * textureSize];
        Vector2 curveStart = new Vector2(-0.58f, -0.18f);
        Vector2 curveControl = new Vector2(-0.12f, 0.72f);
        Vector2 curveEnd = new Vector2(0.56f, 0.24f);

        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                Vector2 point = new Vector2(
                    ((x + 0.5f) / textureSize) * 2f - 1f,
                    ((y + 0.5f) / textureSize) * 2f - 1f);

                float nearestDistance = float.MaxValue;
                float nearestT = 0f;
                Vector2 previous = curveStart;

                for (int sample = 1; sample <= curveSamples; sample++)
                {
                    float t = sample / (float)curveSamples;
                    Vector2 current = QuadraticBezier(
                        curveStart,
                        curveControl,
                        curveEnd,
                        t);
                    float segmentT;
                    float distance = DistanceToSegment(
                        point,
                        previous,
                        current,
                        out segmentT);

                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearestT = ((sample - 1) + segmentT) / curveSamples;
                    }

                    previous = current;
                }

                float strokeRadius = Mathf.Lerp(0.035f, 0.105f,
                    nearestT * nearestT);
                float curveDistance = nearestDistance - strokeRadius;
                float bulbDistance = Vector2.Distance(point, curveEnd) - 0.16f;
                float signedDistance = Mathf.Min(curveDistance, bulbDistance);
                float alpha = 1f - Mathf.SmoothStep(-0.015f, 0.035f,
                    signedDistance);

                pixels[y * textureSize + x] =
                    new Color(1f, 1f, 1f, alpha);
            }
        }

        dropletTexture.SetPixels(pixels);
        dropletTexture.Apply(false, true);

        dropletSprite = Sprite.Create(
            dropletTexture,
            new Rect(0f, 0f, textureSize, textureSize),
            new Vector2(0.5f, 0.5f),
            textureSize);
        dropletSprite.name = "White Curved Sweat Streak";
        dropletSprite.hideFlags = HideFlags.HideAndDontSave;
    }

    private static Vector2 QuadraticBezier(
        Vector2 start,
        Vector2 control,
        Vector2 end,
        float t)
    {
        float inverse = 1f - t;
        return inverse * inverse * start +
               2f * inverse * t * control +
               t * t * end;
    }

    private static float DistanceToSegment(
        Vector2 point,
        Vector2 start,
        Vector2 end,
        out float segmentT)
    {
        Vector2 segment = end - start;
        float lengthSquared = segment.sqrMagnitude;

        if (lengthSquared <= 0.000001f)
        {
            segmentT = 0f;
            return Vector2.Distance(point, start);
        }

        segmentT = Mathf.Clamp01(
            Vector2.Dot(point - start, segment) / lengthSquared);
        Vector2 closest = start + segment * segmentT;
        return Vector2.Distance(point, closest);
    }

    private void ApplyRendererSettings()
    {
        for (int i = 0; i < drops.Count; i++)
        {
            SpriteRenderer renderer = drops[i].renderer;

            if (renderer == null)
                continue;

            renderer.sprite = GetActiveSprite();
            renderer.sortingLayerName = sortingLayerName;
            renderer.sortingOrder = sortingOrder;
        }
    }

    private Sprite GetActiveSprite()
    {
        return spriteOverride != null
            ? spriteOverride
            : dropletSprite;
    }

    private void ClearActiveDrops()
    {
        for (int i = 0; i < drops.Count; i++)
        {
            SweatDrop drop = drops[i];

            if (!drop.active)
                continue;

            drop.active = false;
            drop.root.gameObject.SetActive(false);
        }

        activeDropCount = 0;
        emissionAccumulator = 0f;
        wasEmitting = false;
        pendingDrops.Clear();
    }

    private void OnDisable()
    {
        pressure = 0f;
        ClearActiveDrops();
    }

    private void OnDestroy()
    {
        if (worldRoot != null)
            Destroy(worldRoot.gameObject);

        if (dropletSprite != null)
            Destroy(dropletSprite);

        if (dropletTexture != null)
            Destroy(dropletTexture);
    }
}
