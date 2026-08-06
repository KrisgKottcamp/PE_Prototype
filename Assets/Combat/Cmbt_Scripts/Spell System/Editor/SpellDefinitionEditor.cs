using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SpellDefinition))]
public class SpellDefinitionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        SpellDefinition spell = (SpellDefinition)target;

        // Identity
        EditorGUILayout.PropertyField(serializedObject.FindProperty("displayName"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("spellBase"));

        EditorGUILayout.Space();

        // Phase Timing — shared across all types
        EditorGUILayout.LabelField("Phase Timing", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("buildUpDuration"));
        if (spell.spellBase == SpellBase.Casting)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("firingDuration"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("channelingDuration"));
        }
        EditorGUILayout.PropertyField(serializedObject.FindProperty("antefireDuration"));

        EditorGUILayout.Space();

        // Spell-base-specific fields
        switch (spell.spellBase)
        {
            case SpellBase.Casting:
                DrawCastingFields();
                break;
            case SpellBase.Movement:
                DrawMovementFields();
                break;
            case SpellBase.Stat:
                DrawStatFields();
                break;
        }

        EditorGUILayout.Space();

        // Shared fields
        EditorGUILayout.LabelField("AI Targeting", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("aiTargetingMode"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("preferredRange"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Cooldown", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("cooldown"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Visuals", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("icon"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("buildUpVfxPrefab"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("firingVfxPrefab"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("antefireVfxPrefab"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Animation Keys", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("buildUpAnimTrigger"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("firingAnimTrigger"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("antefireAnimTrigger"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Spell Chaining", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("chain"), true);

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawCastingFields()
    {
        EditorGUILayout.LabelField("Casting — Projectile", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("projectileType"));

        SpellDefinition spell = (SpellDefinition)target;

        if (spell.projectileType == CastProjectileType.Bullet)
            EditorGUILayout.PropertyField(serializedObject.FindProperty("pattern"));

        EditorGUILayout.PropertyField(serializedObject.FindProperty("hitBehavior"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Casting — Numerics", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("projectileCount"));

        if (spell.projectileType == CastProjectileType.Cone ||
            (spell.projectileType == CastProjectileType.Bullet && spell.pattern == ProjectilePattern.Cone))
            EditorGUILayout.PropertyField(serializedObject.FindProperty("coneDegree"));
        if (spell.projectileType == CastProjectileType.Swipe)
            EditorGUILayout.PropertyField(serializedObject.FindProperty("swipeDegree"));

        EditorGUILayout.PropertyField(serializedObject.FindProperty("projectileSpeed"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("range"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Casting — Multi-Fire", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("fireCount"));
        if (spell.fireCount > 1)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("fireDelay"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("lockAimDirection"));
        }

        if (spell.projectileType == CastProjectileType.Bomb ||
            spell.projectileType == CastProjectileType.Zone)
        {
            EditorGUILayout.Space();
            string label = spell.projectileType == CastProjectileType.Zone
                ? "Casting — Zone" : "Casting — Bomb";
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("bombDelay"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("bombRadius"));

            if (spell.projectileType == CastProjectileType.Zone)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("zoneDuration"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("zoneTickRate"));
            }
        }

        if (spell.projectileType == CastProjectileType.Mine)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Casting — Mine", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("mineDetectionRadius"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("mineFuseDelay"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("bombRadius"));
        }

        if (spell.projectileType == CastProjectileType.Beam)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Casting — Beam", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("beamWidth"));
        }

        if (spell.projectileType == CastProjectileType.Helix)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Casting — Helix", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("helixGrowthRate"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("helixInitialRadius"));
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Casting — Damage", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("damage"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("damageFalloff"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("damageType"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Casting — Modifiers", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("modifiers"), true);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Casting — Effects on Hit", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("onHitEffects"), true);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Casting — Multicast on Hit", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("multicastOnHit"), true);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Casting — Projectile Visual", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("projectileVisual"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("projectilePrefab"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Casting — Collision", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("hitMask"));
    }

    private void DrawMovementFields()
    {
        EditorGUILayout.LabelField("Movement", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("movementType"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("movementDistance"));

        SpellDefinition spell = (SpellDefinition)target;

        if (spell.movementType == MovementSpellType.Dash)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Movement — Dash", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("dashSpeed"));
        }

        if (spell.movementType == MovementSpellType.Boost)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Movement — Boost", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("boostMultiplier"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("boostDuration"));
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Movement — Effects", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("movementEffects"), true);
    }

    private void DrawStatFields()
    {
        EditorGUILayout.LabelField("Stat — Apply", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("applyEffects"), true);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Stat — Remove", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("removeEffectIds"), true);
    }
}
