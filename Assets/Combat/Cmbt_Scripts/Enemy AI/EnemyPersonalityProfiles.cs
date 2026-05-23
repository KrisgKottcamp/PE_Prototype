using UnityEngine;

[CreateAssetMenu(menuName = "Combat/Enemy Personality Profiles")]
public class EnemyPersonalityProfiles : ScriptableObject
{
    [System.Serializable]
    public class Profile
    {
        public EnemyPersonality personality;

        [Header("Opening")]
        public OpeningAction openingAction = OpeningAction.None;
        public float openingDuration = 0.9f;

        [Header("Range")]
        public float preferredMinRange = 1.2f;
        public float preferredMaxRange = 3.2f;

        [Header("Flee")]
        [Range(0f, 1f)] public float fleeAtHealth01 = 0.35f; // set to 0 for never flee
        public float panicDistance = 1.3f; // if player is closer than this, stop fleeing and fight

        [Header("Point Bias")]
        public float coverBias = 0f; // positive boosts cover points
        public float flankBias = 0f; // positive boosts flank points
        public float fireBias = 0f;  // positive boosts fire points

        [Header("Shooting Gate")]
        public bool mustBeInPreferredRangeToShoot = false;

        [Header("Burst")]
        public int shotsPerBurst = 2;
        public float intraBurstInterval = 0.12f;
        public float burstCooldown = 0.90f;
        public int burstsPerEnable = 1;

        [Header("Pattern Picks")]
        public EnemyShooterDebug.PatternType farPattern = EnemyShooterDebug.PatternType.AimedSingle;
        public EnemyShooterDebug.PatternType midPattern = EnemyShooterDebug.PatternType.AimedFan;
        public EnemyShooterDebug.PatternType closePattern = EnemyShooterDebug.PatternType.BoI_8Way;

        [Header("Pattern Params")]
        public int fanBullets = 5;
        public float fanArc = 35f;
        public int ringBullets = 12;
        public float angularSpeedPerTick = 12f;
    }

    public enum OpeningAction { None, ChargePlayer, RunToCover, SeekFlank }

    public Profile aggressive = new Profile
    {
        personality = EnemyPersonality.Aggressive,
        openingAction = OpeningAction.ChargePlayer,
        openingDuration = 0.9f,
        preferredMinRange = 0.9f,
        preferredMaxRange = 2.2f,
        fleeAtHealth01 = 0f,
        panicDistance = 0f,
        coverBias = -0.2f,
        flankBias = 0.2f,
        fireBias = 0.3f,
        mustBeInPreferredRangeToShoot = true,
        shotsPerBurst = 2,
        intraBurstInterval = 0.11f,
        burstCooldown = 0.75f,
        burstsPerEnable = 1,
        farPattern = EnemyShooterDebug.PatternType.AimedFan,
        midPattern = EnemyShooterDebug.PatternType.AimedFan,
        closePattern = EnemyShooterDebug.PatternType.BoI_8Way,
        fanBullets = 5,
        fanArc = 28f
    };

    public Profile scaredCat = new Profile
    {
        personality = EnemyPersonality.ScaredCat,
        openingAction = OpeningAction.RunToCover,
        openingDuration = 1.0f,
        preferredMinRange = 4.5f,
        preferredMaxRange = 8.0f,
        fleeAtHealth01 = 0.45f,
        panicDistance = 1.4f,
        coverBias = 0.9f,
        flankBias = -0.2f,
        fireBias = 0.1f,
        mustBeInPreferredRangeToShoot = false,
        shotsPerBurst = 2,
        intraBurstInterval = 0.16f,
        burstCooldown = 1.10f,
        burstsPerEnable = 1,
        farPattern = EnemyShooterDebug.PatternType.AimedSingle,
        midPattern = EnemyShooterDebug.PatternType.AimedFan,
        closePattern = EnemyShooterDebug.PatternType.AimedFan,
        fanBullets = 4,
        fanArc = 50f
    };

    public Profile backstabber = new Profile
    {
        personality = EnemyPersonality.Backstabber,
        openingAction = OpeningAction.SeekFlank,
        openingDuration = 1.1f,
        preferredMinRange = 2.5f,
        preferredMaxRange = 6.0f,
        fleeAtHealth01 = 0.35f,
        panicDistance = 1.3f,
        coverBias = 0.7f,
        flankBias = 1.2f,
        fireBias = 0.2f,
        mustBeInPreferredRangeToShoot = false,
        shotsPerBurst = 2,
        intraBurstInterval = 0.10f,
        burstCooldown = 0.95f,
        burstsPerEnable = 1,
        farPattern = EnemyShooterDebug.PatternType.AimedFan,
        midPattern = EnemyShooterDebug.PatternType.AimedFan,
        closePattern = EnemyShooterDebug.PatternType.AimedFan,
        fanBullets = 3,
        fanArc = 20f
    };

    public Profile avenger = new Profile
    {
        personality = EnemyPersonality.Avenger,
        openingAction = OpeningAction.RunToCover,
        openingDuration = 1.0f,
        preferredMinRange = 4.5f,
        preferredMaxRange = 8.0f,
        fleeAtHealth01 = 0.45f,
        panicDistance = 1.4f,
        coverBias = 0.9f,
        flankBias = -0.2f,
        fireBias = 0.1f,
        mustBeInPreferredRangeToShoot = false,
        shotsPerBurst = 2,
        intraBurstInterval = 0.16f,
        burstCooldown = 1.10f,
        burstsPerEnable = 1,
        farPattern = EnemyShooterDebug.PatternType.AimedSingle,
        midPattern = EnemyShooterDebug.PatternType.AimedFan,
        closePattern = EnemyShooterDebug.PatternType.AimedFan,
        fanBullets = 4,
        fanArc = 50f
    };

    public Profile Get(EnemyPersonality p)
    {
        switch (p)
        {
            case EnemyPersonality.Aggressive: return aggressive;
            case EnemyPersonality.ScaredCat: return scaredCat;
            case EnemyPersonality.Backstabber: return backstabber;
            case EnemyPersonality.Avenger: return avenger;
            default: return aggressive;
        }
    }
}
