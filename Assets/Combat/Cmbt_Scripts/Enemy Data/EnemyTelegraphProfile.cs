using UnityEngine;

[CreateAssetMenu(menuName = "Game/Enemies/Telegraph Profile", fileName = "EnemyTelegraphProfile")]
public class EnemyTelegraphProfile : ScriptableObject
{
    [Header("Timing")]
    [Min(0.01f)] public float duration = 0.4f;

    [Header("Shake")]
    public bool useShake = true;
    [Min(0f)] public float shakeAmount = 0.06f;
    [Min(1f)] public float shakeFrequency = 30f;
    [Min(0.01f)] public float shakeRampPower = 1f;

    [Header("Scale")]
    public bool useScalePulse = true;
    public Vector3 scaleMultiplier = new Vector3(1.12f, 1.12f, 1f);
    [Min(0.25f)] public float scalePulseCount = 1f;

    [Header("Flash")]
    public bool useFlash = true;
    public Color warningColor = new Color(1f, 0.25f, 0.2f, 1f);
    [Min(0f)] public float flashFrequency = 8f;
    [Range(0f, 1f)] public float maximumFlashStrength = 0.75f;

    [Header("Directional Lean")]
    public bool useDirectionalLean = false;
    [Min(0f)] public float directionalLeanDistance = 0.08f;

    [Header("Rotation Wobble")]
    public bool useRotationWobble = false;
    [Min(0f)] public float rotationWobbleDegrees = 6f;
    [Min(0f)] public float rotationWobbleFrequency = 5f;
}
