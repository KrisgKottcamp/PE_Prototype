using ProjectEri.SkillSystemV2;
using UnityEngine;

/// <summary>
/// Lightweight guaranteed feedback for prototype spells that do not yet have
/// authored VFX. A brief ring appears when SpellRunner accepts the cast, and
/// point-target spells also mark their resolved target point.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpellRunner))]
public sealed class PlayerSpellCastFeedback2D : MonoBehaviour
{
    [SerializeField] private Color castColor = new Color(0.25f, 0.9f, 1f, 0.95f);
    [SerializeField, Min(0.05f)] private float duration = 0.22f;
    [SerializeField, Min(0.05f)] private float originRadius = 0.55f;
    [SerializeField, Min(0.05f)] private float targetRadius = 0.35f;

    private SpellRunner runner;

    private void Awake()
    {
        runner = GetComponent<SpellRunner>();
    }

    private void OnEnable()
    {
        if (runner == null)
            runner = GetComponent<SpellRunner>();
        if (runner != null)
            runner.CastStarted += HandleCastStarted;
    }

    private void OnDisable()
    {
        if (runner != null)
            runner.CastStarted -= HandleCastStarted;
    }

    private void HandleCastStarted(SpellCastEvent castEvent)
    {
        SpawnPulse(castEvent.Context.Origin, originRadius);

        CastTargetingRequirement requirement = castEvent.Spell != null &&
            castEvent.Spell.Delivery != null
                ? castEvent.Spell.Delivery.TargetingRequirement
                : CastTargetingRequirement.None;

        if (castEvent.Context.HasTargetPoint &&
            (requirement & CastTargetingRequirement.TargetPoint) != 0 &&
            (requirement & CastTargetingRequirement.Direction) == 0)
        {
            SpawnPulse(castEvent.Context.TargetPoint, targetRadius);
        }
    }

    private void SpawnPulse(Vector2 position, float radius)
    {
        GameObject pulse = new GameObject("V2 Spell Cast Pulse");
        Renderer sourceRenderer = GetComponentInChildren<Renderer>(true);
        pulse.layer = sourceRenderer != null
            ? sourceRenderer.gameObject.layer
            : gameObject.layer;
        pulse.transform.position = new Vector3(position.x, position.y, transform.position.z);
        SpellFeedbackPulse2D visual = pulse.AddComponent<SpellFeedbackPulse2D>();
        visual.Initialize(
            castColor,
            duration,
            radius,
            sourceRenderer != null ? sourceRenderer.sortingLayerID : 0,
            sourceRenderer != null ? sourceRenderer.sortingOrder + 60 : 210);
    }
}

public sealed class SpellFeedbackPulse2D : MonoBehaviour
{
    private const int SegmentCount = 32;
    private LineRenderer line;
    private Material material;
    private Color color;
    private float duration;
    private float radius;
    private float elapsed;

    public void Initialize(
        Color pulseColor,
        float pulseDuration,
        float pulseRadius,
        int sortingLayerId,
        int sortingOrder)
    {
        color = pulseColor;
        duration = Mathf.Max(0.05f, pulseDuration);
        radius = Mathf.Max(0.05f, pulseRadius);

        line = gameObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = true;
        line.positionCount = SegmentCount;
        line.widthMultiplier = 0.04f;
        line.sortingLayerID = sortingLayerId;
        line.sortingOrder = sortingOrder;
        line.numCornerVertices = 2;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader != null)
        {
            material = new Material(shader) { name = "Runtime V2 Cast Feedback" };
            line.sharedMaterial = material;
        }

        Draw(0.35f, color);
    }

    private void Update()
    {
        elapsed += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(elapsed / duration);
        Color faded = color;
        faded.a *= 1f - t;
        Draw(Mathf.Lerp(0.35f, 1f, t), faded);

        if (t >= 1f)
            Destroy(gameObject);
    }

    private void Draw(float scale, Color drawColor)
    {
        if (line == null)
            return;

        line.startColor = drawColor;
        line.endColor = drawColor;
        Vector2 center = transform.position;
        float resolvedRadius = radius * scale;
        for (int i = 0; i < SegmentCount; i++)
        {
            float angle = i * Mathf.PI * 2f / SegmentCount;
            line.SetPosition(
                i,
                center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * resolvedRadius);
        }
    }

    private void OnDestroy()
    {
        if (material != null)
            Destroy(material);
    }
}
