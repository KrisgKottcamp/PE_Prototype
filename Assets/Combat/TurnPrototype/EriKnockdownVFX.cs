using UnityEngine;

/// <summary>Small world-space knockdown cues; no text or screen-sized overlay.</summary>
[DisallowMultipleComponent]
public sealed class EriKnockdownVFX : MonoBehaviour
{
    private readonly LineRenderer[] birds = new LineRenderer[3];
    private readonly LineRenderer[] shards = new LineRenderer[14];
    private readonly Vector2[] shardVelocity = new Vector2[14];
    private readonly Vector2[] shardOrigin = new Vector2[14];
    private static Material sharedMaterial;
    private Collider2D body;
    private ParticleSystem burstParticles;
    private bool down;
    private float downProgress;
    private float burstAt = -10f;

    private static Material Material
    {
        get
        {
            if (sharedMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader != null) sharedMaterial = new Material(shader) { name = "Eri Knockdown Lines" };
            }
            return sharedMaterial;
        }
    }

    private void Awake() { body = GetComponent<Collider2D>(); }

    private LineRenderer MakeLine(string label, float width, Color color)
    {
        var child = new GameObject(label);
        child.transform.SetParent(transform, false);
        var line = child.AddComponent<LineRenderer>();
        line.sharedMaterial = Material;
        line.useWorldSpace = true;
        line.positionCount = 3;
        line.startWidth = line.endWidth = width;
        line.startColor = line.endColor = color;
        line.sortingLayerName = "VFX";
        line.sortingOrder = 125;
        return line;
    }

    public void SetDown(bool value)
    {
        down = value;
        if (!value) downProgress = 0;
        if (value)
        {
            for (int i = 0; i < birds.Length; i++)
                if (birds[i] == null) birds[i] = MakeLine("Knockdown bird " + i, 0.045f, new Color(1f, .9f, .45f));
        }
        for (int i = 0; i < birds.Length; i++) if (birds[i] != null) birds[i].enabled = value;
        if (value) SetDownProgress(0, 1);
    }

    public void SetDownProgress(int hitsLanded, int hitsRequired)
    {
        downProgress = Mathf.Clamp01(hitsLanded / (float)Mathf.Max(1, hitsRequired));
        var color = Color.Lerp(new Color(1f, .9f, .45f), new Color(1f, .55f, .38f), downProgress);
        for (int i = 0; i < birds.Length; i++) if (birds[i] != null) birds[i].startColor = birds[i].endColor = color;
    }

    public void BreakBurst()
    {
        burstAt = Time.unscaledTime;
        Vector2 center = body != null ? body.bounds.center : (Vector2)transform.position;
        if (burstParticles == null)
        {
            var child = new GameObject("Defense break particles");
            child.transform.SetParent(transform, false);
            burstParticles = child.AddComponent<ParticleSystem>();
            burstParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = burstParticles.main;
            main.loop = false; main.duration = .4f; main.startLifetime = .34f;
            main.startSpeed = 3.5f; main.startSize = .09f;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(.65f, .95f, 1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = burstParticles.emission; emission.rateOverTime = 0;
            var shape = burstParticles.shape; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = .28f;
            var renderer = child.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = Material; renderer.sortingLayerName = "VFX"; renderer.sortingOrder = 124;
        }
        burstParticles.transform.position = center;
        burstParticles.Emit(18);
        for (int i = 0; i < shards.Length; i++)
        {
            if (shards[i] == null) shards[i] = MakeLine("Defense shard " + i, .055f, i % 2 == 0
                ? new Color(.5f, .92f, 1f) : new Color(1f, .92f, .55f));
            shards[i].enabled = true;
            float angle = (i + .2f * (i % 3)) * Mathf.PI * 2f / shards.Length;
            shardVelocity[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (2.5f + i % 4 * .4f);
            shardOrigin[i] = center + shardVelocity[i].normalized * .25f;
        }
    }

    private void LateUpdate()
    {
        float now = Time.unscaledTime;
        if (down)
        {
            Vector2 center = body != null ? new Vector2(body.bounds.center.x, body.bounds.max.y + .72f)
                : (Vector2)transform.position + Vector2.up;
            for (int i = 0; i < birds.Length; i++)
            {
                var bird = birds[i];
                if (bird == null) continue;
                float angle = now * 2.8f + i * Mathf.PI * 2f / birds.Length;
                Vector2 p = center + new Vector2(Mathf.Cos(angle) * .38f, Mathf.Sin(angle) * .12f);
                float flap = .10f + .065f * Mathf.Sin(now * 13f + i);
                bird.SetPosition(0, p + new Vector2(-.13f, flap));
                bird.SetPosition(1, p);
                bird.SetPosition(2, p + new Vector2(.13f, flap));
            }
        }
        float elapsed = now - burstAt;
        if (elapsed < 0 || elapsed > .46f)
        {
            for (int i = 0; i < shards.Length; i++) if (shards[i] != null) shards[i].enabled = false;
            return;
        }
        for (int i = 0; i < shards.Length; i++)
        {
            var shard = shards[i];
            if (shard == null) continue;
            Vector2 p = shardOrigin[i] + shardVelocity[i] * elapsed;
            Vector2 side = new Vector2(-shardVelocity[i].y, shardVelocity[i].x).normalized * .075f * (1 - elapsed / .46f);
            shard.SetPosition(0, p - side);
            shard.SetPosition(1, p + shardVelocity[i].normalized * .11f);
            shard.SetPosition(2, p + side);
        }
    }
}
