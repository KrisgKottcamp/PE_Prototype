using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Prototype-only encounter conductor. All timings use game time, including menu pauses.</summary>
public sealed class EriEnemyRhythm : MonoBehaviour
{
    [Header("Attack / dodge rhythm")]
    public float OpeningSeconds = 0.9f;
    public float TelegraphSeconds = 0.65f;
    public float RecoverySeconds = 1.5f;
    public float VolleyInterval = 0.38f;
    public float BulletSpeed = 5f;
    public float BulletLifetime = 2.2f;
    public int BulletDamage = 8;
    public int CleanWaveAP = 15;
    public int MaxBullets = 120;
    [Range(1, 6)] public int VolleyCount = 5;
    [Header("Repositioning between volleys")]
    public float MoveSpeed = 3.6f;
    public float PreferredDistance = 3.5f;
    [Header("Warning visibility")]
    public float TelegraphWidth = 0.025f;
    public float TelegraphOutlineWidth = 0.045f;
    private bool holdingFormation;
    private ArenaNavigationGrid navigation;
    private readonly Dictionary<EnemyHealth, Movement> movement = new Dictionary<EnemyHealth, Movement>();
    private sealed class Movement
    {
        public Rigidbody2D Body;
        public readonly List<Vector2> Path = new List<Vector2>();
        public float RefreshAt;
        public int Waypoint;
    }
    private readonly List<EnemyHealth> enemies = new List<EnemyHealth>();
    private readonly List<MonoBehaviour> suspended = new List<MonoBehaviour>();
    private readonly List<GameObject> cues = new List<GameObject>();
    private readonly List<Shot> shots = new List<Shot>();
    private readonly HashSet<int> registered = new HashSet<int>();
    private Material ink;
    private CombatPawn pawn;
    private Collider2D hurtbox;
    private bool waveActive, tookDamage;
    private float scanAt;
    private int obstacles;
    private static readonly HashSet<string> ReplacedControllers = new HashSet<string>
    {
        "EnemyBrain", "EnemyShooterDebug", "AttackDogBrain", "AttackDogLungeHitbox",
        "EnemyAgentV2", "EnemyActionRunnerV2", "EnemyCombatExecutorV2", "EnemySkillExecutorV2",
        "EnemyLocomotionV2", "EnemySquadCoordinator", "SquadDirectorV2", "EnemyAttackTelegraph",
        "SpellRunner"
    };
    private sealed class Shot
    {
        public GameObject Visual;
        public Vector2 Position, Velocity;
        public float Life;
    }
    private sealed class Pattern
    {
        public EnemyHealth Enemy;
        public int Kind;
        public Vector2 Origin, Aim;
        public readonly List<Vector2> Starts = new List<Vector2>();
        public readonly List<Vector2> Directions = new List<Vector2>();
        public readonly List<LineRenderer> Lines = new List<LineRenderer>();
    }
    private void Awake()
    {
        pawn = GetComponent<CombatPawn>();
        hurtbox = GetComponent<Collider2D>();
        obstacles = LayerMask.GetMask("Obstacles");
        ink = new Material(Shader.Find("Sprites/Default"));
        CombatPawn.AcceptedDamage += Damaged;
    }
    private void Damaged(int member, int amount) { if (waveActive) tookDamage = true; }
    private void Suspend(MonoBehaviour component)
    {
        if (component == null || !component.enabled || !ReplacedControllers.Contains(component.GetType().Name)) return;
        suspended.Add(component);
        component.StopAllCoroutines();
        component.enabled = false;
    }
    private void Scan()
    {
        foreach (var enemy in FindObjectsByType<EnemyHealth>(FindObjectsSortMode.InstanceID))
        {
            if (!registered.Add(enemy.GetInstanceID())) continue;
            enemies.Add(enemy);
            movement[enemy] = new Movement { Body = enemy.GetComponent<Rigidbody2D>() };
            foreach (var component in enemy.GetComponentsInChildren<MonoBehaviour>(true)) Suspend(component);
            var body = enemy.GetComponent<Rigidbody2D>();
            if (body != null) body.linearVelocity = Vector2.zero;
        }
        // Directors can live on the arena root rather than on an enemy.
        foreach (var component in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (component.GetType().Name == "EnemySquadCoordinator" || component.GetType().Name == "SquadDirectorV2") Suspend(component);
        enemies.RemoveAll(e => e == null || e.CurrentHP <= 0);
    }
    private IEnumerator Start()
    {
        yield return null; // CombatManager spawns the encounter during Start.
        Scan();
        yield return new WaitForSeconds(OpeningSeconds);
        int cycle = 0;
        while (pawn != null && !pawn.IsDown)
        {
            Scan();
            if (enemies.Count == 0) { yield return new WaitForSeconds(0.25f); continue; }
            // One clearly readable source per phrase; rotate so every enemy gets a turn.
            EnemyHealth source = null;
            var camera = Camera.main;
            for (int i = 0; i < enemies.Count; i++)
            {
                var candidate = enemies[(cycle + i) % enemies.Count];
                if (camera != null)
                {
                    var screen = camera.WorldToViewportPoint(candidate.transform.position);
                    if (screen.z <= 0 || screen.x < 0.05f || screen.x > 0.95f || screen.y < 0.08f || screen.y > 0.92f) continue;
                }
                source = candidate; break;
            }
            // Never stall the encounter because a source is near the player or just
            // outside the viewport; choose the next living enemy and keep the rhythm moving.
            if (source == null)
                source = enemies.Find(e => e != null && e.CurrentHP > 0);
            if (source == null) { yield return null; continue; }
            holdingFormation = true;
            var pattern = Prepare(source, cycle % 3);
            float time = 0;
            while (time < TelegraphSeconds)
            {
                time += Time.deltaTime;
                foreach (var line in pattern.Lines)
                {
                    if (line == null) continue;
                    var color = PatternColor(pattern.Kind);
                    color.a = Mathf.Lerp(0.45f, 0.7f, time / TelegraphSeconds);
                    line.startColor = line.endColor = color;
                    // Set actual widths, not another multiplier on the existing width curve.
                    line.startWidth = line.endWidth = TelegraphWidth;
                }
                yield return null;
            }
            ClearCues();
            waveActive = true; tookDamage = false;
            bool fired = false;
            for (int burst = 0; burst < VolleyCount; burst++)
            {
                if (pattern.Enemy == null || pattern.Enemy.CurrentHP <= 0) break;
                // Locked aim and origin match the preview. Displacement cancels the remaining volley.
                if (Vector2.Distance(pattern.Enemy.transform.position, pattern.Origin) > 0.75f) break;
                // Alternate half a cell every row: yesterday's gap becomes the next
                // row's bullet lane, creating a weave rather than stationary safe corridors.
                for (int i = 0; i < pattern.Directions.Count; i++)
                {
                    StaggerLane(pattern, i, burst % 2 != 0, out var start, out var direction);
                    Fire(start, direction, PatternColor(pattern.Kind));
                }
                fired = true;
                if (burst < VolleyCount - 1) yield return new WaitForSeconds(VolleyInterval);
            }
            // Once the last shot is committed, reposition while bullets travel instead of idling.
            holdingFormation = false;
            while (shots.Count > 0) yield return null; // No lingering bullets in the offensive window.
            waveActive = false;
            holdingFormation = false;
            if (fired && !tookDamage && pawn != null && !pawn.IsDown && enemies.Exists(e => e != null && e.CurrentHP > 0))
            {
                var member = PartyManager.Instance != null ? PartyManager.Instance.Active : null;
                if (member != null && member.def != null)
                    member.currentAP = Mathf.Min(member.currentAP + CleanWaveAP,
                        EriTurnRules.Capacity(member.def.maxAP, member.exhaustedSegments));
            }
            // Green halos identify the offensive recovery window.
            foreach (var enemy in enemies)
                if (enemy != null && enemy.CurrentHP > 0) Ring(enemy.transform, 0.6f, new Color(0.35f, 1f, 0.55f, 0.65f));
            float recovery = 0;
            while (recovery < RecoverySeconds)
            {
                recovery += Time.deltaTime;
                foreach (var cue in cues)
                {
                    if (cue == null) continue;
                    var line = cue.GetComponent<LineRenderer>();
                    float pulse = 0.5f + 0.5f * Mathf.Sin(recovery * 5f);
                    line.widthMultiplier = 0.035f + pulse * 0.025f;
                    line.startColor = line.endColor = new Color(0.35f, 1f, 0.55f,
                        Mathf.Lerp(0.65f, 0.15f, recovery / RecoverySeconds));
                }
                yield return null;
            }
            ClearCues();
            cycle++;
        }
    }
    private static Color PatternColor(int kind) => kind == 0 ? new Color(1f,0.65f,0.15f) :
        kind == 1 ? new Color(1f,0.35f,0.75f) : new Color(0.25f,0.9f,1f);
    private Pattern Prepare(EnemyHealth enemy, int kind)
    {
        var pattern = new Pattern { Enemy = enemy, Kind = kind, Origin = enemy.transform.position };
        pattern.Aim = ((Vector2)transform.position - pattern.Origin).normalized;
        if (pattern.Aim.sqrMagnitude < 0.01f) pattern.Aim = Vector2.down;
        if (kind == 0)
            for (int i = -3; i <= 3; i++) AddLane(pattern, pattern.Origin, Rotate(pattern.Aim, i * 18));
        else if (kind == 1)
        {
            // Full rings alternate by half their angular spacing on successive rows.
            for (int i = 0; i < 20; i++) AddLane(pattern, pattern.Origin, Rotate(pattern.Aim, i * 18));
        }
        else
        {
            Vector2 side = new Vector2(-pattern.Aim.y, pattern.Aim.x);
            for (int i = -3; i <= 3; i++) AddLane(pattern, pattern.Origin + side * i * 0.9f, pattern.Aim);
        }
        // Preview the interleaved row too, so all firing paths are signalled.
        for (int i = 0; i < pattern.Directions.Count; i++)
        {
            StaggerLane(pattern, i, true, out var start, out var direction);
            DrawLane(pattern, start, direction);
        }
        return pattern;
    }
    private static void StaggerLane(Pattern pattern, int index, bool staggered,
        out Vector2 start, out Vector2 direction)
    {
        start = pattern.Starts[index]; direction = pattern.Directions[index];
        if (!staggered) return;
        if (pattern.Kind == 2)
            start += new Vector2(-pattern.Aim.y, pattern.Aim.x) * 0.45f;
        else direction = Rotate(direction, 9f);
    }
    private void AddLane(Pattern pattern, Vector2 origin, Vector2 direction)
    {
        pattern.Starts.Add(origin); pattern.Directions.Add(direction);
        DrawLane(pattern, origin, direction);
    }
    private void DrawLane(Pattern pattern, Vector2 origin, Vector2 direction)
    {
        // Narrow translucent edging gives contrast without covering the arena with black bands.
        Vector2 end = origin + direction * BulletSpeed * BulletLifetime;
        var outline = Stroke("Projectile warning outline", TelegraphOutlineWidth, new Color(0.055f, 0.035f, 0.08f, 0.3f));
        outline.sortingOrder = 119;
        outline.positionCount = 2;
        outline.SetPosition(0, origin); outline.SetPosition(1, end);
        cues.Add(outline.gameObject);
        var color = PatternColor(pattern.Kind); color.a = 0.45f;
        var line = Stroke("Projectile path preview", TelegraphWidth, color);
        line.sortingOrder = 120;
        line.positionCount = 2;
        line.SetPosition(0, origin);
        line.SetPosition(1, end);
        pattern.Lines.Add(line); cues.Add(line.gameObject);
    }
    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float a = degrees * Mathf.Deg2Rad;
        return new Vector2(v.x * Mathf.Cos(a) - v.y * Mathf.Sin(a), v.x * Mathf.Sin(a) + v.y * Mathf.Cos(a));
    }
    private LineRenderer Stroke(string title, float width, Color color)
    {
        var obj = new GameObject(title);
        obj.transform.SetParent(transform, true);
        var line = obj.AddComponent<LineRenderer>();
        line.sharedMaterial = ink; line.useWorldSpace = true;
        line.startWidth = line.endWidth = width;
        line.startColor = line.endColor = color;
        line.sortingLayerName = "VFX"; line.sortingOrder = 110;
        return line;
    }
    private void Ring(Transform enemy, float radius, Color color)
    {
        var line = Stroke("Enemy recovery", 0.045f, color); line.loop = true; line.positionCount = 24;
        line.transform.SetParent(enemy, false);
        line.useWorldSpace = false;
        for (int i = 0; i < 24; i++) line.SetPosition(i, Rotate(Vector2.right, i * 15) * radius);
        cues.Add(line.gameObject);
    }
    private void FixedUpdate()
    {
        if (pawn == null || pawn.IsDown || holdingFormation) return;
        if (navigation == null) navigation = FindFirstObjectByType<ArenaNavigationGrid>();
        var fields = FindObjectsByType<EriFearField>(FindObjectsSortMode.None);
        for (int i = 0; i < enemies.Count; i++)
        {
            var enemy = enemies[i];
            if (enemy == null || enemy.CurrentHP <= 0) continue;
            var motion = movement[enemy];
            Vector2 position = enemy.transform.position;
            bool pulled = false;
            foreach (var field in fields) if (field.ControlsMotionAt(position)) { pulled = true; break; }
            var knockback = enemy.GetComponent<KnockbackReceiver2D>();
            var forced = enemy.GetComponent<ProjectEri.SkillSystemV2.SpellActorMotionController2D>();
            if (pulled || (knockback != null && knockback.IsKnockbackActive) ||
                (forced != null && forced.IsControllingMotion))
            { motion.RefreshAt = 0; continue; }
            if (Time.time >= motion.RefreshAt)
            {
                motion.RefreshAt = Time.time + 0.65f;
                Vector2 away = position - (Vector2)transform.position;
                if (away.sqrMagnitude < 0.01f) away = Rotate(Vector2.right, i * 137f);
                // Close distant gaps, create space when crowded, otherwise flank slowly.
                float angle = (i % 2 == 0 ? 1 : -1) * 22f;
                Vector2 goal = (Vector2)transform.position + Rotate(away.normalized, angle) * PreferredDistance;
                motion.Path.Clear(); motion.Waypoint = 0;
                if (navigation != null && navigation.IsBuilt)
                {
                    goal = navigation.FindNearestWalkablePosition(goal);
                    navigation.TryFindPath(position, goal, motion.Path);
                }
                else motion.Path.Add(goal);
            }
            while (motion.Waypoint < motion.Path.Count && Vector2.Distance(position, motion.Path[motion.Waypoint]) < 0.15f)
                motion.Waypoint++;
            if (motion.Waypoint >= motion.Path.Count) continue;
            Vector2 delta = Vector2.ClampMagnitude(motion.Path[motion.Waypoint] - position, MoveSpeed * Time.fixedDeltaTime);
            var slow = enemy.GetComponent<ProjectEri.EnemyAI.V2.EnemySlowReceiverV2>();
            if (slow != null) delta *= Mathf.Clamp01(slow.MovementSpeedMultiplier);
            // Keep movement collision-aware even with legacy/kinematic enemy bodies.
            if (obstacles != 0 && Physics2D.CircleCast(position, 0.25f, delta.normalized, delta.magnitude + 0.05f, obstacles).collider != null)
                continue;
            if (motion.Body != null) motion.Body.MovePosition(position + delta);
            else enemy.transform.position = position + delta;
        }
    }
    private void Fire(Vector2 origin, Vector2 direction, Color color)
    {
        if (shots.Count >= MaxBullets) return;
        var line = Stroke("Rhythm bullet", 0.20f, color);
        line.positionCount = 2; line.numCapVertices = 6;
        line.SetPosition(0, origin); line.SetPosition(1, origin + direction * 0.1f);
        shots.Add(new Shot { Visual = line.gameObject, Position = origin, Velocity = direction * BulletSpeed, Life = BulletLifetime });
    }
    private void Update()
    {
        if (Time.deltaTime <= 0) return;
        if (Time.time >= scanAt) { scanAt = Time.time + 0.5f; Scan(); }
        bool ended = pawn == null || pawn.IsDown || !enemies.Exists(e => e != null && e.CurrentHP > 0);
        Vector2 target = hurtbox != null ? (Vector2)hurtbox.bounds.center : (Vector2)transform.position;
        for (int i = shots.Count - 1; i >= 0; i--)
        {
            var shot = shots[i];
            Vector2 next = shot.Position + shot.Velocity * Time.deltaTime;
            Vector2 segment = next - shot.Position;
            float t = segment.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(target - shot.Position, segment) / segment.sqrMagnitude) : 0;
            // Small forgiving core; sweep prevents tunnelling at low frame rates.
            bool hit = !ended && Vector2.Distance(target, shot.Position + segment * t) < 0.24f;
            bool wall = obstacles != 0 && Physics2D.Linecast(shot.Position, next, obstacles).collider != null;
            shot.Life -= Time.deltaTime;
            if (hit || ended || wall || shot.Life <= 0)
            {
                if (hit) pawn.ApplyDamage(BulletDamage);
                Destroy(shot.Visual); shots.RemoveAt(i); continue;
            }
            shot.Position = next;
            var line = shot.Visual.GetComponent<LineRenderer>();
            line.SetPosition(0, next); line.SetPosition(1, next + shot.Velocity.normalized * 0.1f);
        }
    }
    private void ClearCues() { foreach (var cue in cues) if (cue != null) Destroy(cue); cues.Clear(); }
    private void OnDisable()
    {
        StopAllCoroutines();
        CombatPawn.AcceptedDamage -= Damaged;
        ClearCues();
        foreach (var shot in shots) if (shot.Visual != null) Destroy(shot.Visual);
        shots.Clear();
        foreach (var component in suspended) if (component != null) component.enabled = true;
        suspended.Clear();
    }
    private void OnDestroy() { if (ink != null) Destroy(ink); }
}
