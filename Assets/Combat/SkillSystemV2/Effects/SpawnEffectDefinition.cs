using System.Collections.Generic;
using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    public enum SpellSpawnPosition
    {
        HitPoint,
        Target,
        CastOrigin,
        TargetPoint
    }

    public enum SpellSpawnRotation
    {
        Identity,
        AimDirection,
        HitNormal
    }

    [CreateAssetMenu(
        fileName = "Effect_Spawn",
        menuName = "Project Eri/Skill System V2/Effects/Spawn Object")]
    public sealed class SpawnEffectDefinition : EffectDefinition
    {
        [SerializeField]
        private GameObject prefab;

        [SerializeField]
        private SpellSpawnPosition spawnPosition = SpellSpawnPosition.HitPoint;

        [SerializeField]
        private Vector2 worldOffset;

        [SerializeField]
        private SpellSpawnRotation rotation = SpellSpawnRotation.Identity;

        [SerializeField]
        private float rotationOffsetDegrees;

        [SerializeField]
        private bool parentToTarget;

        [SerializeField, Min(0f)]
        private float lifetime;

        [SerializeField]
        private SpellTimeMode lifetimeTimeMode = SpellTimeMode.Scaled;

        public override bool Apply(in SpellEffectContext context)
        {
            if (prefab == null)
                return false;

            Vector2 position = ResolvePosition(context) + worldOffset;
            Quaternion resolvedRotation = ResolveRotation(context);
            GameObject instance = Object.Instantiate(
                prefab,
                new Vector3(position.x, position.y, prefab.transform.position.z),
                resolvedRotation);

            if (parentToTarget && context.Target != null)
                instance.transform.SetParent(context.Target.transform, true);

            var spawnContext = new SpellSpawnContext(context, instance);
            MonoBehaviour[] receivers =
                instance.GetComponentsInChildren<MonoBehaviour>(true);

            for (int i = 0; i < receivers.Length; i++)
            {
                if (receivers[i] is ISpellSpawnReceiver receiver)
                    receiver.InitializeSpawn(spawnContext);
            }

            if (lifetime > 0f)
            {
                TimedSpellObject timer = instance.GetComponent<TimedSpellObject>();
                if (timer == null)
                    timer = instance.AddComponent<TimedSpellObject>();

                timer.Initialize(lifetime, lifetimeTimeMode);
            }

            return true;
        }

        public override void CollectValidationIssues(
            List<SpellValidationIssue> issues)
        {
            if (prefab == null)
            {
                issues?.Add(new SpellValidationIssue(
                    SpellValidationSeverity.Error,
                    $"Spawn effect '{DisplayName}' needs a prefab."));
            }
        }

        private Vector2 ResolvePosition(in SpellEffectContext context)
        {
            switch (spawnPosition)
            {
                case SpellSpawnPosition.Target:
                    return context.Target != null
                        ? (Vector2)context.Target.transform.position
                        : context.HitPoint;
                case SpellSpawnPosition.CastOrigin:
                    return context.Cast.Origin;
                case SpellSpawnPosition.TargetPoint:
                    return context.Cast.HasTargetPoint
                        ? context.Cast.TargetPoint
                        : context.HitPoint;
                default:
                    return context.HitPoint;
            }
        }

        private Quaternion ResolveRotation(in SpellEffectContext context)
        {
            Vector2 direction;
            switch (rotation)
            {
                case SpellSpawnRotation.AimDirection:
                    direction = context.Cast.AimDirection;
                    break;
                case SpellSpawnRotation.HitNormal:
                    direction = context.HitNormal;
                    break;
                default:
                    return Quaternion.Euler(0f, 0f, rotationOffsetDegrees);
            }

            float angle = direction.sqrMagnitude > 0.000001f
                ? Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg
                : 0f;
            return Quaternion.Euler(
                0f,
                0f,
                angle + rotationOffsetDegrees);
        }

        private void OnValidate()
        {
            lifetime = Mathf.Max(0f, lifetime);
        }
    }
}
