using ProjectEri.SkillSystemV2;
using UnityEngine;

public enum EriCommandKind { Slash, Mark, Pull, BlackHole, Pierce, Shot, Burst, Recover,
    DashSlash, Snipe, Fan, Grenade, Motivate, HealSelf, Reflect, Cover, Inspire, Silence,
    WhipSlash, MarkShot, Dispel, OilSpill }

/// <summary>Small, runtime-only test kit. Existing spell assets are never edited.</summary>
public sealed class EriPrototypeDelivery : DeliveryDefinition
{
    public EriCommandKind Kind;
    public int Segments;
    public int MPCost;
    public int Damage;
    public float Range = 10f, Radius = 2f, Duration;
    public PlayerTargetingDefinition Targeting;
    public override PlayerTargetingDefinition ResolvePlayerTargeting(SpellDeliverySettings settings) => Targeting;
    public override CastTargetingRequirement TargetingRequirement =>
        Kind == EriCommandKind.Recover || Kind == EriCommandKind.Motivate || Kind == EriCommandKind.HealSelf ||
        Kind == EriCommandKind.Reflect || Kind == EriCommandKind.Inspire || Kind == EriCommandKind.Silence ? CastTargetingRequirement.None :
        Kind == EriCommandKind.Slash || Kind == EriCommandKind.Pierce || Kind == EriCommandKind.Shot ||
        Kind == EriCommandKind.DashSlash || Kind == EriCommandKind.Snipe || Kind == EriCommandKind.Fan ||
        Kind == EriCommandKind.WhipSlash || Kind == EriCommandKind.MarkShot
            ? CastTargetingRequirement.Direction : CastTargetingRequirement.TargetPoint;
    public override ISpellDeliveryExecution CreateExecution(in SpellExecutionContext context) =>
        new Execution(this, context);

    private sealed class Execution : ISpellDeliveryExecution
    {
        private readonly EriPrototypeDelivery definition;
        private readonly SpellExecutionContext context;
        private float wait;
        public bool IsComplete { get; private set; }
        public Execution(EriPrototypeDelivery definition, SpellExecutionContext context)
        { this.definition = definition; this.context = context; }
        public void Begin()
        {
            if (!context.SuppressGameplayEffects && definition.Kind != EriCommandKind.Recover)
            {
                EriKitEffects.Execute(definition, context.Cast.Origin,
                    context.Cast.HasTargetPoint ? context.Cast.TargetPoint : context.Cast.Origin,
                    context.Cast.AimDirection);
            }
            wait = definition.Kind == EriCommandKind.DashSlash && !context.SuppressGameplayEffects
                ? (EriTurnCombat.Active != null && EriTurnCombat.Active.KitSettings != null ? EriTurnCombat.Active.KitSettings.DashSeconds : .18f) : 0;
            IsComplete = wait <= 0;
        }
        public void Tick(float deltaTime) { wait -= deltaTime; if (wait <= 0) IsComplete = true; }
        public void End() { }
        public void Cancel() { }
    }
}

public sealed class EriFearMark : MonoBehaviour
{
    public float Remaining;
    public string AffinityLabel = "Fear: neutral (test)";
    public float NaturalMultiplier = 1f;
    private EriCombatUIView ui;
    private RectTransform icon;
    private Camera viewCamera;
    private Collider2D body;
    private void Update()
    {
        Remaining = Mathf.Max(0, Remaining - Time.deltaTime);
    }
    private void LateUpdate()
    {
        if(Remaining<=0){if(icon!=null)icon.gameObject.SetActive(false);return;}
        if(viewCamera==null)viewCamera=Camera.main;
        if(viewCamera==null)return;
        if(ui==null)ui=FindFirstObjectByType<EriCombatUIView>();
        if(ui==null)return;
        if(icon==null)
        {
            icon=ui.CreateEnemyMark();
            body=GetComponent<Collider2D>();
        }
        Vector3 above=body!=null?new Vector3(body.bounds.center.x,body.bounds.max.y,transform.position.z):transform.position+Vector3.up*0.9f;
        above+=(Vector3)ui.EnemyMarkOffset;
        if (GetComponent<EriEnemyDefenses>() != null) above += Vector3.right * 0.65f;
        Vector3 screen=viewCamera.WorldToScreenPoint(above);
        icon.gameObject.SetActive(screen.z>0);
        var canvas=icon.GetComponentInParent<Canvas>();
        if(RectTransformUtility.ScreenPointToWorldPointInRectangle((RectTransform)icon.parent,screen,
            canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,out var position))icon.position=position;
    }
    private void OnDisable(){if(icon!=null)icon.gameObject.SetActive(false);}
    private void OnDestroy(){if(icon!=null)Destroy(icon.gameObject);}
    public int ResolveFearDamage(int baseDamage)
    {
        bool marked = Remaining > 0;
        if (marked) Remaining = 0;
        return Mathf.RoundToInt(baseDamage * NaturalMultiplier * (marked ? 1.75f : 1f));
    }
}

public sealed class EriFearField : MonoBehaviour
{
    public bool ControlsMotionAt(Vector2 position) => remaining > 0 &&
        (kind == EriCommandKind.Pull || kind == EriCommandKind.BlackHole) &&
        Vector2.Distance(position, point) <= radius;
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
        line.startWidth = line.endWidth = 0.06f;
        line.sortingLayerID = SortingLayer.NameToID("VFX"); line.sortingOrder = 80;
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
            int damage = kind == EriCommandKind.Slash ? 32 : kind == EriCommandKind.Shot ? 40 : 65;
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
                mark.Remaining = 28f;
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
