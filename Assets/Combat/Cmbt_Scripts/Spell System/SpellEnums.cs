public enum SpellBase
{
    Casting,
    Movement,
    Stat
}

public enum SpellPhase
{
    Idle,
    BuildUp,
    Firing,
    Channeling,
    Antefire
}

public enum CastProjectileType
{
    Bullet,
    Bomb,
    Beam,
    Cone,
    Circle,
    Swipe,
    Helix,
    Mine,
    TripWire,
    Zone
}

public enum ProjectilePattern
{
    Circle,
    Cone,
    Line
}

public enum ProjectileHitBehavior
{
    Direct,
    Phase
}

public enum ChannelMode
{
    WindDown,
    Continuous
}

public enum DamageType
{
    Physical,
    Magical,
    True
}

public enum MovementSpellType
{
    Dash,
    Boost,
    Blink,
    Teleport
}
