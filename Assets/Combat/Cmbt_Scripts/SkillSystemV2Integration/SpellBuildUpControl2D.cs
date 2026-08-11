using System.Collections.Generic;
using ProjectEri.SkillSystemV2;
using UnityEngine;

/// <summary>
/// Player-side adapter for SpellTiming.BuildUpControl. It intentionally lives
/// outside the reusable spell core: enemies can share Build Up timing without
/// inheriting player input restrictions.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpellRunner))]
public sealed class SpellBuildUpControl2D : MonoBehaviour
{
    [SerializeField] private SpellRunner spellRunner;

    private BuildUpPowerParticles2D particles;

    public bool IsBuildUpActive => spellRunner != null &&
                                   spellRunner.IsCasting &&
                                   spellRunner.CurrentPhase ==
                                   SpellCastPhase.BuildUp;

    private SpellBuildUpControl CurrentControl => IsBuildUpActive
        ? spellRunner.ActiveSpell.Timing.BuildUpControl
        : default;

    public bool BlocksMovement => IsBuildUpActive &&
                                  CurrentControl.BlocksPlayerMovement;
    public bool BlocksBasicAttacks => IsBuildUpActive &&
                                      CurrentControl.BlocksPlayerBasicAttacks;
    public bool BlocksSkillUsage => IsBuildUpActive &&
                                    CurrentControl.BlocksPlayerSkillUsage;

    public static bool IsMovementBlocked(GameObject source)
    {
        SpellBuildUpControl2D control = source != null
            ? source.GetComponentInParent<SpellBuildUpControl2D>()
            : null;
        return control != null && control.BlocksMovement;
    }

    public static bool IsBasicAttackBlocked(GameObject source)
    {
        SpellBuildUpControl2D control = source != null
            ? source.GetComponentInParent<SpellBuildUpControl2D>()
            : null;
        return control != null && control.BlocksBasicAttacks;
    }

    public static bool IsSkillUsageBlocked(GameObject source)
    {
        SpellBuildUpControl2D control = source != null
            ? source.GetComponentInParent<SpellBuildUpControl2D>()
            : null;
        return control != null && control.BlocksSkillUsage;
    }

    private void Awake()
    {
        if (spellRunner == null)
            spellRunner = GetComponent<SpellRunner>();
    }

    private void Update()
    {
        if (!IsBuildUpActive || !CurrentControl.ShowPowerUpParticles)
        {
            particles?.StopParticles();
            return;
        }

        if (particles == null)
        {
            var visualRoot = new GameObject("Skill V2 Build Up Particles");
            visualRoot.transform.SetParent(transform, false);
            particles = visualRoot.AddComponent<BuildUpPowerParticles2D>();
        }

        particles.Play(
            CurrentControl,
            spellRunner.ActiveSpell.Timing.TimeMode == SpellTimeMode.Unscaled);
    }
}

[DisallowMultipleComponent]
public sealed class BuildUpPowerParticles2D : MonoBehaviour
{
    private sealed class Particle
    {
        public Transform Transform;
        public SpriteRenderer Renderer;
        public float Lifetime;
        public float Age;
    }

    private static Sprite softParticleSprite;
    private readonly List<Particle> activeParticles = new List<Particle>();
    private float spawnAccumulator;
    private SpellBuildUpControl settings;
    private bool useUnscaledTime;

    public void Play(SpellBuildUpControl newSettings, bool unscaledTime)
    {
        settings = newSettings;
        useUnscaledTime = unscaledTime;
    }

    public void StopParticles()
    {
        spawnAccumulator = 0f;
        for (int i = activeParticles.Count - 1; i >= 0; i--)
        {
            Particle particle = activeParticles[i];
            if (particle?.Transform != null)
                Destroy(particle.Transform.gameObject);
        }
        activeParticles.Clear();
    }

    private void Update()
    {
        float delta = useUnscaledTime
            ? Time.unscaledDeltaTime
            : Time.deltaTime;
        if (delta <= 0f)
            return;

        spawnAccumulator += delta * settings.ParticlesPerSecond;
        int safety = 0;
        while (spawnAccumulator >= 1f && safety++ < 8)
        {
            spawnAccumulator -= 1f;
            SpawnParticle();
        }

        for (int i = activeParticles.Count - 1; i >= 0; i--)
        {
            Particle particle = activeParticles[i];
            if (particle?.Transform == null || particle.Renderer == null)
            {
                activeParticles.RemoveAt(i);
                continue;
            }

            particle.Age += delta;
            Vector3 localPosition = particle.Transform.localPosition;
            float remaining = localPosition.magnitude;
            float travel = settings.ParticleInwardSpeed * delta;
            particle.Transform.localPosition = remaining > travel
                ? localPosition.normalized * (remaining - travel)
                : Vector3.zero;

            Color color = settings.ParticleColor;
            color.a *= Mathf.Clamp01(1f - particle.Age / particle.Lifetime);
            particle.Renderer.color = color;
            if (particle.Age >= particle.Lifetime ||
                particle.Transform.localPosition.sqrMagnitude <= 0.0025f)
            {
                Destroy(particle.Transform.gameObject);
                activeParticles.RemoveAt(i);
            }
        }
    }

    private void SpawnParticle()
    {
        var instance = new GameObject("Build Up Energy Particle");
        instance.transform.SetParent(transform, false);
        Vector2 direction = Random.insideUnitCircle;
        if (direction.sqrMagnitude <= 0.0001f)
            direction = Vector2.up;
        direction.Normalize();
        instance.transform.localPosition = direction * settings.ParticleSpawnRadius;
        instance.transform.localScale = Vector3.one * settings.ParticleSize;

        var renderer = instance.AddComponent<SpriteRenderer>();
        renderer.sprite = GetSoftParticleSprite();
        renderer.color = settings.ParticleColor;
        renderer.sortingLayerID = SortingLayer.NameToID("VFX");
        renderer.sortingOrder = 100;
        activeParticles.Add(new Particle
        {
            Transform = instance.transform,
            Renderer = renderer,
            Lifetime = settings.ParticleSpawnRadius /
                       settings.ParticleInwardSpeed + 0.1f
        });
    }

    private static Sprite GetSoftParticleSprite()
    {
        if (softParticleSprite != null)
            return softParticleSprite;

        const int size = 16;
        var texture = new Texture2D(size, size)
        {
            name = "Skill V2 Build Up Particle",
            hideFlags = HideFlags.HideAndDontSave
        };
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float alpha = Mathf.Clamp01(1f - new Vector2(dx, dy).magnitude);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha * alpha));
            }
        }
        texture.Apply();
        softParticleSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            size);
        softParticleSprite.hideFlags = HideFlags.HideAndDontSave;
        return softParticleSprite;
    }

    private void OnDisable()
    {
        StopParticles();
    }
}
