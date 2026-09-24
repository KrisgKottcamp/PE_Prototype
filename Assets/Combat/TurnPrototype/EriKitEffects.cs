using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Small isolated prototype deliveries. Each cast snapshots actor and timing before handoff.</summary>
public sealed class EriKitEffects : MonoBehaviour
{
    public static bool IsDashing { get; private set; }
    private bool ownsDash;
    private EriCommandKind kind;
    private int damage, actor, attackId;
    private float defenseMultiplier, radius, range, remaining;
    private Vector2 position, direction;
    private bool burning, projectile, reflectingField;
    private LineRenderer visual;
    private Material material;
    private HashSet<int> hit = new HashSet<int>();
    private static readonly List<EriKitEffects> fields = new List<EriKitEffects>();
    private static EriCharacterKitSettings Settings => EriTurnCombat.Active != null && EriTurnCombat.Active.KitSettings != null
        ? EriTurnCombat.Active.KitSettings : null;

    public static void Execute(EriPrototypeDelivery definition, Vector2 origin, Vector2 target, Vector2 aim)
    {
        var mechanics = EriCombatMechanics.Active;
        if (mechanics == null) return;
        int actor = PartyManager.Instance != null ? PartyManager.Instance.activeIndex : 0;
        var kind = definition.Kind;
        if (kind == EriCommandKind.Motivate) { mechanics.BuffActor(actor, 2); return; }
        if (kind == EriCommandKind.Inspire) { mechanics.BuffParty(1); return; }
        if (kind == EriCommandKind.HealSelf) { mechanics.HealActor(actor, definition.Damage); return; }
        if (kind == EriCommandKind.Silence)
        { mechanics.ClearProjectiles(origin, definition.Radius, false, actor); Pulse(origin, definition.Radius, Color.cyan); return; }
        if (kind == EriCommandKind.Reflect)
        {
            var field = new GameObject("Eri projectile reflect field").AddComponent<EriKitEffects>();
            field.kind = kind; field.reflectingField = true; field.position = origin; field.radius = definition.Radius;
            field.remaining = Mathf.Max(.1f, definition.Duration); field.actor = actor;
            field.attackId = mechanics.NextAttackId();
            fields.Add(field); field.DrawCircle(new Color(.5f, 1f, 1f, .65f));
            mechanics.ClearProjectiles(origin, field.radius, true, actor, field.attackId);
            return;
        }
        if (kind == EriCommandKind.Cover) { mechanics.PlaceCover(target, definition.Duration, definition.Radius); return; }
        var effect = new GameObject("Eri " + kind).AddComponent<EriKitEffects>();
        effect.kind = kind; effect.damage = definition.Damage; effect.actor = actor;
        effect.attackId = mechanics.NextAttackId(); effect.defenseMultiplier = mechanics.DefenseDamageMultiplier;
        effect.radius = definition.Radius; effect.range = definition.Range;
        effect.position = origin; effect.direction = aim.sqrMagnitude > .001f ? aim.normalized : Vector2.right;
        target = origin + Vector2.ClampMagnitude(target - origin, Mathf.Max(.1f, definition.Range));
        if (kind == EriCommandKind.OilSpill)
        { effect.position = target; effect.remaining = definition.Duration; fields.Add(effect); effect.DrawCircle(new Color(.45f,.32f,.1f,.75f)); return; }
        if (kind == EriCommandKind.Grenade) { effect.StartCoroutine(effect.Grenade(origin, target)); return; }
        if (kind == EriCommandKind.DashSlash) { effect.StartCoroutine(effect.Dash(origin)); return; }
        if (kind == EriCommandKind.Dispel)
        {
            foreach (var enemy in Enemies()) if (Vector2.Distance(Center(enemy), target) <= effect.radius)
                enemy.GetComponent<EriEnemyDefenses>()?.RemoveTemporaryResistance();
            Pulse(target, effect.radius, Color.white); Destroy(effect.gameObject); return;
        }
        if (kind == EriCommandKind.Slash || kind == EriCommandKind.WhipSlash)
        {
            EnemyHealth closest = null; float distance = float.MaxValue;
            foreach (var enemy in Enemies())
            {
                float along = Vector2.Dot(Center(enemy) - origin, effect.direction);
                if (along < 0 || along > effect.range || DistanceToSegment(Center(enemy), origin, origin + effect.direction * effect.range) > effect.radius) continue;
                if (kind == EriCommandKind.Slash) effect.Hit(enemy);
                else if (along < distance) { closest = enemy; distance = along; }
            }
            if (closest != null)
            {
                // The setup hit cannot exploit the mark it creates. This keeps
                // Off-balance as a handoff to a later physical action.
                effect.Hit(closest);
                closest.GetComponent<EriEnemyDefenses>()?.ApplyOffBalance(Settings != null ? Settings.OffBalanceSeconds : 8);
            }
            effect.DrawLine(origin, origin + effect.direction * effect.range, new Color(1,.85f,.5f));
            effect.remaining = .18f; return;
        }
        if (kind == EriCommandKind.Fan)
        {
            int count = Settings != null ? Mathf.Max(1,Settings.FanProjectileCount) : 5;
            float angle = Settings != null ? Settings.FanAngle : 55;
            for (int i = 0; i < count; i++)
            {
                var bolt = new GameObject("Eri Panic Fan bolt").AddComponent<EriKitEffects>();
                bolt.kind = kind; bolt.damage = effect.damage; bolt.actor = actor; bolt.attackId = effect.attackId;
                bolt.defenseMultiplier = effect.defenseMultiplier; bolt.radius = effect.radius; bolt.range = effect.range;
                bolt.position = origin; bolt.hit = effect.hit; // One damage event per target per fan, not point-blank shotgun multiplication.
                float offset = count == 1 ? 0 : Mathf.Lerp(-angle*.5f,angle*.5f,(float)i/(count-1));
                bolt.direction = Quaternion.Euler(0,0,offset)*effect.direction; bolt.StartProjectile();
            }
            Destroy(effect.gameObject); return;
        }
        if (kind == EriCommandKind.Shot || kind == EriCommandKind.Snipe || kind == EriCommandKind.MarkShot || kind == EriCommandKind.Reflect)
        { effect.StartProjectile(); return; }
        // Retain old experiment deliveries only for existing assets not included in the new kits.
        var legacy = effect.gameObject.AddComponent<EriFearField>(); legacy.Initialize(kind, origin,target,effect.direction);
        Destroy(effect);
    }

    private static EnemyHealth[] Enemies() => FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None);
    private static Vector2 Center(EnemyHealth enemy)
    { var collider = enemy.GetComponent<Collider2D>(); return collider != null ? (Vector2)collider.bounds.center : (Vector2)enemy.transform.position; }
    private void Hit(EnemyHealth enemy)
    {
        if (enemy == null || enemy.CurrentHP <= 0 || !hit.Add(enemy.GetInstanceID())) return;
        if (kind == EriCommandKind.MarkShot)
        {
            var mark = enemy.GetComponent<EriFearMark>() ?? enemy.gameObject.AddComponent<EriFearMark>();
            mark.Remaining = Settings != null ? Settings.MarkSeconds : 28;
            return;
        }
        bool magic = kind == EriCommandKind.Shot || kind == EriCommandKind.Snipe || kind == EriCommandKind.Fan || kind == EriCommandKind.Reflect || kind == EriCommandKind.OilSpill;
        EriCombatMechanics.Active?.ApplySkillHit(enemy, damage, actor, magic, magic && kind != EriCommandKind.OilSpill, defenseMultiplier, attackId);
    }
    private void StartProjectile() { projectile = true; remaining = range / Speed; DrawLine(position,position+direction*.3f,new Color(.9f,.55f,1)); }
    private static float Speed => Settings != null ? Mathf.Max(.1f,Settings.ProjectileSpeed) : 18;
    private void Update()
    {
        if (Time.deltaTime <= 0 || remaining <= 0) return;
        remaining -= Time.deltaTime;
        if (reflectingField)
        {
            Vector2 center = EriTurnCombat.Active != null ? (Vector2)EriTurnCombat.Active.transform.position : position;
            position = center;
            DrawCircle(new Color(.5f, 1f, 1f, .5f));
            EriCombatMechanics.Active?.ClearProjectiles(center, radius, true, actor, attackId);
        }
        if (projectile)
        {
            Vector2 next = position + direction * Speed * Time.deltaTime;
            var obstruction = Physics2D.Linecast(position, next, LayerMask.GetMask("Obstacles"));
            bool blocked = obstruction.collider != null;
            if (blocked) next = obstruction.point;
            if (kind == EriCommandKind.Shot || kind == EriCommandKind.Snipe)
                foreach (var field in fields.ToArray()) if (field != null && field.kind == EriCommandKind.OilSpill && !field.burning && DistanceToSegment(field.position,position,next) <= field.radius)
                    field.Ignite();
            // Sort along the sweep so non-piercing shots cannot skip a nearer target.
            var enemies = new List<EnemyHealth>(Enemies());
            enemies.Sort((a,b)=>Vector2.SqrMagnitude(Center(a)-position).CompareTo(Vector2.SqrMagnitude(Center(b)-position)));
            foreach (var enemy in enemies)
            {
                if (enemy == null || enemy.CurrentHP<=0 || hit.Contains(enemy.GetInstanceID())) continue;
                var collider = enemy.GetComponent<Collider2D>();
                float bodyRadius = collider != null ? Mathf.Min(collider.bounds.extents.x,collider.bounds.extents.y) : .3f;
                if (DistanceToSegment(Center(enemy),position,next) > radius + bodyRadius) continue;
                Hit(enemy);
                if (kind != EriCommandKind.MarkShot) { Destroy(gameObject); return; }
            }
            if (blocked) { Destroy(gameObject); return; }
            position = next; DrawLine(position,position+direction*.3f,new Color(.9f,.55f,1));
        }
        else if (burning) foreach (var enemy in Enemies()) if (Vector2.Distance(Center(enemy),position) <= radius) Hit(enemy);
        if (remaining <= 0) Destroy(gameObject);
    }
    private void Ignite()
    { burning = true; remaining = Settings != null ? Settings.OilBurnSeconds : 3; DrawCircle(new Color(1,.45f,.12f,.9f)); foreach (var enemy in Enemies()) if (Vector2.Distance(Center(enemy),position)<=radius) Hit(enemy); }
    private IEnumerator Grenade(Vector2 from, Vector2 target)
    {
        float flight = Settings != null ? Mathf.Max(.1f,Settings.GrenadeFlightSeconds) : .55f;
        for (float elapsed = 0; elapsed < flight; elapsed += Time.deltaTime)
        { position = Vector2.Lerp(from,target,elapsed/flight)+Vector2.up*Mathf.Sin(elapsed/flight*Mathf.PI)*.65f; DrawLine(position-Vector2.right*.12f,position+Vector2.right*.12f,Color.gray); yield return null; }
        position = target; DrawCircle(new Color(1,.8f,.45f));
        foreach (var enemy in Enemies()) if (Vector2.Distance(Center(enemy),target)<=radius) Hit(enemy);
        remaining = .25f;
    }
    private IEnumerator Dash(Vector2 from)
    {
        IsDashing = ownsDash = true;
        var pawn = EriTurnCombat.Active != null ? EriTurnCombat.Active.transform : null;
        var body = pawn != null ? pawn.GetComponent<Rigidbody2D>() : null;
        float duration = Settings != null ? Mathf.Max(.05f,Settings.DashSeconds) : .18f;
        Vector2 destination = from + direction * range;
        // Stop before solid scenery, but pass through enemy colliders as the skill promises.
        foreach (var obstacle in Physics2D.RaycastAll(from,direction,range))
            if (obstacle.collider != null && !obstacle.collider.isTrigger && obstacle.collider.GetComponentInParent<EnemyHealth>() == null &&
                (pawn == null || !obstacle.collider.transform.IsChildOf(pawn)) && obstacle.distance > .1f)
                destination = Vector2.Distance(from,destination) > obstacle.distance ? from+direction*Mathf.Max(0,obstacle.distance-.3f) : destination;
        Vector2 previous = from;
        for (float elapsed=0;elapsed<duration;elapsed+=Time.deltaTime)
        {
            Vector2 next=Vector2.Lerp(from,destination,Mathf.Clamp01((elapsed+Time.deltaTime)/duration));
            if (body != null) body.position=next; else if(pawn!=null)pawn.position=next;
            foreach(var enemy in Enemies()) if(DistanceToSegment(Center(enemy),previous,next)<=radius)Hit(enemy);
            DrawLine(from,next,new Color(1,.8f,.45f)); previous=next; yield return null;
        }
        IsDashing = ownsDash = false;
        remaining=.15f;
    }
    public static void CreateCover(Vector2 position,float duration,float radius)
    {
        var cover=new GameObject("Eri temporary cover").AddComponent<EriKitEffects>();
        cover.kind=EriCommandKind.Cover; cover.position=position;cover.radius=radius;cover.remaining=duration;
        fields.Add(cover);cover.DrawCircle(new Color(.45f,.8f,1,.7f));
    }
    public static bool BlocksProjectile(Vector2 from,Vector2 to)
    {
        foreach(var field in fields)if(field!=null&&field.kind==EriCommandKind.Cover&&field.remaining>0&&DistanceToSegment(field.position,from,to)<=field.radius)return true;
        return false;
    }
    public static void ReflectProjectile(Vector2 position,int actor,int damage,int attackId = 0,float defenseMultiplier = 1f)
    {
        EnemyHealth nearest=null;float best=float.MaxValue;
        foreach(var enemy in Enemies()) if(enemy.CurrentHP>0){float d=Vector2.SqrMagnitude(Center(enemy)-position);if(d<best){best=d;nearest=enemy;}}
        if(nearest==null)return;
        var bolt=new GameObject("Eri reflected Fear bolt").AddComponent<EriKitEffects>();
        bolt.kind=EriCommandKind.Reflect;bolt.actor=actor;bolt.damage=damage;bolt.position=position;bolt.direction=(Center(nearest)-position).normalized;
        bolt.radius=.2f;bolt.range=18;bolt.defenseMultiplier=defenseMultiplier;
        bolt.attackId=attackId != 0 ? attackId : EriCombatMechanics.Active.NextAttackId();bolt.StartProjectile();
    }
    private static void Pulse(Vector2 position,float radius,Color color)
    {var fx=new GameObject("Eri skill pulse").AddComponent<EriKitEffects>();fx.position=position;fx.radius=radius;fx.remaining=.25f;fx.DrawCircle(color);}
    private void EnsureVisual(Color color)
    {
        if(visual==null){visual=gameObject.AddComponent<LineRenderer>();var shader=Shader.Find("Sprites/Default");if(shader!=null){material=new Material(shader);visual.sharedMaterial=material;}visual.useWorldSpace=true;visual.startWidth=visual.endWidth=.045f;visual.sortingLayerID=SortingLayer.NameToID("VFX");visual.sortingOrder=100;}
        visual.startColor=visual.endColor=color;
    }
    private void DrawLine(Vector2 start,Vector2 end,Color color)
    {EnsureVisual(color);visual.loop=false;visual.positionCount=2;visual.SetPosition(0,start);visual.SetPosition(1,end);}
    private void DrawCircle(Color color)
    {EnsureVisual(color);visual.loop=true;visual.positionCount=48;for(int i=0;i<48;i++){float a=i*Mathf.PI*2/48;visual.SetPosition(i,position+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius);}}
    private static float DistanceToSegment(Vector2 point,Vector2 start,Vector2 end)
    {Vector2 delta=end-start;float t=delta.sqrMagnitude<.00001f?0:Mathf.Clamp01(Vector2.Dot(point-start,delta)/delta.sqrMagnitude);return Vector2.Distance(point,start+delta*t);}
    private void OnDestroy(){if(ownsDash)IsDashing=false;fields.Remove(this);if(material!=null)Destroy(material);}
}
