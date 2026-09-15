using ProjectEri.SkillSystemV2;
using UnityEngine;

public enum EriCommandKind { Slash, Mark, Pull, BlackHole, Pierce, Shot, Burst, Recover }

/// <summary>Small, runtime-only test kit. Existing spell assets are never edited.</summary>
public sealed class EriPrototypeDelivery : DeliveryDefinition
{
    public EriCommandKind Kind;
    public int Segments;
    public int MPCost;
    public PlayerTargetingDefinition Targeting;
    public override PlayerTargetingDefinition ResolvePlayerTargeting(SpellDeliverySettings settings) => Targeting;
    public override CastTargetingRequirement TargetingRequirement =>
        Kind == EriCommandKind.Recover ? CastTargetingRequirement.None :
        Kind == EriCommandKind.Slash || Kind == EriCommandKind.Pierce || Kind == EriCommandKind.Shot
            ? CastTargetingRequirement.Direction : CastTargetingRequirement.TargetPoint;
    public override ISpellDeliveryExecution CreateExecution(in SpellExecutionContext context) =>
        new Execution(this, context);

    private sealed class Execution : ISpellDeliveryExecution
    {
        private readonly EriPrototypeDelivery definition;
        private readonly SpellExecutionContext context;
        public bool IsComplete { get; private set; }
        public Execution(EriPrototypeDelivery definition, SpellExecutionContext context)
        { this.definition = definition; this.context = context; }
        public void Begin()
        {
            if (!context.SuppressGameplayEffects && definition.Kind != EriCommandKind.Recover)
            {
                var effect = new GameObject("Eri Prototype " + definition.Kind).AddComponent<EriFearField>();
                effect.Initialize(definition.Kind, context.Cast.Origin,
                    context.Cast.HasTargetPoint ? context.Cast.TargetPoint : context.Cast.Origin,
                    context.Cast.AimDirection);
            }
            IsComplete = true;
        }
        public void Tick(float deltaTime) { }
        public void End() { }
        public void Cancel() { }
    }
}

public sealed class EriFearMark : MonoBehaviour
{
    public float Remaining;
    public string AffinityLabel = "Fear: neutral (test)";
    public float NaturalMultiplier = 1f;
    private TMPro.TextMeshPro label;
    private void Awake()
    {
        var go = new GameObject("Prototype Affinity Label");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0, 1.1f, -0.1f);
        label = go.AddComponent<TMPro.TextMeshPro>();
        label.alignment = TMPro.TextAlignmentOptions.Center;
        label.rectTransform.sizeDelta = new Vector2(4, 1);
        label.fontSize = 2.3f;
        label.GetComponent<MeshRenderer>().sortingOrder = 100;
    }
    private void Update()
    {
        Remaining = Mathf.Max(0, Remaining - Time.deltaTime);
        label.text = Remaining > 0 ? $"FEAR MARK {Remaining:0}s\n{AffinityLabel}" : AffinityLabel;
        label.color = Remaining > 0 ? new Color(0.9f, 0.6f, 1f) : Color.white;
    }
    public int ResolveFearDamage(int baseDamage)
    {
        bool marked = Remaining > 0;
        if (marked) Remaining = 0;
        return Mathf.RoundToInt(baseDamage * NaturalMultiplier * (marked ? 1.75f : 1f));
    }
}

public sealed class EriFearField : MonoBehaviour
{
    private EriCommandKind kind;
    private Vector2 origin, point, direction;
    private float remaining;
    private float radius;
    private EnemyHealth[] enemies;
    private LineRenderer line;
    private Material material;
    private readonly System.Collections.Generic.HashSet<int> marked = new System.Collections.Generic.HashSet<int>();
    public void Initialize(EriCommandKind command, Vector2 from, Vector2 target, Vector2 aim)
    {
        kind = command; origin = from; point = target; direction = aim.normalized;
        radius = kind == EriCommandKind.BlackHole ? 4f : 2.5f;
        remaining = kind == EriCommandKind.Mark ? 10f : kind == EriCommandKind.BlackHole ? 2f : kind == EriCommandKind.Pull ? 0.7f : 0.35f;
        enemies = FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None);
        line = gameObject.AddComponent<LineRenderer>();
        var shader = Shader.Find("Sprites/Default");
        if (shader != null) { material = new Material(shader); line.sharedMaterial = material; }
        line.startWidth = line.endWidth = 0.06f; line.sortingOrder = 80;
        line.startColor = line.endColor = kind == EriCommandKind.Mark ? new Color(0.9f, 0.5f, 1f) : new Color(0.35f, 0.85f, 1f);
        bool ray = kind == EriCommandKind.Pierce || kind == EriCommandKind.Shot || kind == EriCommandKind.Slash;
        if (ray)
        {
            float length = kind == EriCommandKind.Slash ? 2.5f : 10f;
            line.positionCount = 2;
            line.SetPosition(0, origin); line.SetPosition(1, origin + direction * length);
        }
        else
        {
            line.loop = true; line.positionCount = 64;
            for (int i = 0; i < 64; i++)
            {
                float a = i * Mathf.PI * 2 / 64;
                line.SetPosition(i, point + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
            }
        }
        if (kind == EriCommandKind.Slash || kind == EriCommandKind.Pierce || kind == EriCommandKind.Shot || kind == EriCommandKind.Burst)
            ApplyDamage();
    }
    private void ApplyDamage()
    {
        foreach (var enemy in enemies)
        {
            if (enemy == null || enemy.CurrentHP <= 0) continue;
            Vector2 offset = (Vector2)enemy.transform.position - origin;
            float along = Vector2.Dot(offset, direction);
            float across = Mathf.Abs(offset.x * direction.y - offset.y * direction.x);
            bool hit = kind == EriCommandKind.Burst
                ? Vector2.Distance(enemy.transform.position, point) <= radius
                : along >= 0 && along <= (kind == EriCommandKind.Slash ? 2.5f : 10f) &&
                  across <= (kind == EriCommandKind.Slash ? 1.2f : kind == EriCommandKind.Shot ? 0.3f : 0.65f);
            if (!hit) continue;
            int damage = kind == EriCommandKind.Slash ? 16 : kind == EriCommandKind.Shot ? 18 : 30;
            var mark = enemy.GetComponent<EriFearMark>();
            if (kind != EriCommandKind.Slash && mark != null) damage = mark.ResolveFearDamage(damage);
            enemy.TakeDamage(damage);
        }
    }
    private void Update()
    {
        if (Time.deltaTime <= 0) return;
        remaining -= Time.deltaTime;
        if (remaining <= 0) { Destroy(gameObject); return; }
        foreach (var enemy in enemies)
        {
            if (enemy == null || enemy.CurrentHP <= 0 || Vector2.Distance(enemy.transform.position, point) > radius) continue;
            if (kind == EriCommandKind.Mark && marked.Add(enemy.GetInstanceID()))
            {
                var mark = enemy.GetComponent<EriFearMark>() ?? enemy.gameObject.AddComponent<EriFearMark>();
                mark.Remaining = 16f;
            }
            else if (kind == EriCommandKind.Pull || kind == EriCommandKind.BlackHole)
            {
                var body = enemy.GetComponent<Rigidbody2D>();
                Vector2 next = Vector2.MoveTowards(enemy.transform.position, point, Time.deltaTime * 7f);
                if (body != null) body.MovePosition(next);
                else enemy.transform.position = next;
            }
        }
    }
    private void OnDestroy() { if (material != null) Destroy(material); }
}
