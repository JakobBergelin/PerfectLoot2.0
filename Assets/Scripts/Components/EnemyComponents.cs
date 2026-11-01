using Unity.Entities;
using Unity.Mathematics;

public struct Enemy : IComponentData
{

}
public struct NpcBehavior : IComponentData
{
    public float TurnSpeed;
    public float TurnAcceleration;
    public float3 Direction;
    public quaternion DesiredDirection;
    public float AttackRadius;
    public bool IsAttacking;
}

/// <summary>
/// On every enemy with AI: the player it's after (Entity.Null: none, it stands still). Set by the detection systems
/// (EnemyDetection or SwarmDetection, being hit, alerts), read by EnemyMovementSystem.
/// </summary>
public struct EnemyTarget : IComponentData
{
    public Entity Player;
    public float SinceSensed;   // Seconds since it last saw, was hit by or was alerted to the player (EnemyDetection forgets after ForgetTime)
}

/// <summary>Always knows where the (living) player is, from anywhere (HuntPlayerSystem). For enemies sent after the player, like a Survive step's waves.</summary>
public struct HuntsPlayer : IComponentData
{
}

/// <summary>
/// How a Physics enemy finds the player: seen within SightRadius (shrinking toward ProximityRadius for a player behind it,
/// line of sight on the FlowField), or within ProximityRadius through walls; hit by the player; or alerted (a player attack
/// missing it by NearMissMargin, another enemy that found the player within that one's AlertRadius). Forgets the player
/// after ForgetTime without sensing it.
/// </summary>
public struct EnemyDetection : IComponentData
{
    public float SightRadius;
    public float ProximityRadius;
    public float AlertRadius;       // Enemies this close join in when it finds the player itself (not when alerted)
    public float NearMissMargin;
    public float ForgetTime;
}

/// <summary>
/// The cheap detection for Swarm enemies: the player within Radius (no line of sight), being hit, or alerts. Never alerts
/// others and never forgets (only a dead or gone player).
/// </summary>
public struct SwarmDetection : IComponentData
{
    public float Radius;
    public float NearMissMargin;
}

public struct EnemyMeleeAttack : IComponentData
{
    public float Damage;
    public float AttackSpeed;
    public float Elapsed;  
}

/// <summary>
/// How an enemy's body interacts. Physics: collides with other units and the player (body-blocks, pushes).
/// Swarm: only collides with the world (ground, walls, props); separation steering keeps them apart. Much cheaper.
/// </summary>
public enum BodyType
{
    Physics,
    Swarm,
}

/// <summary>On every unit in the spatial grid (player and enemies). Enemies steer away from nearby units.</summary>
public struct Steering : IComponentData
{
    public float Radius;        // Body radius used for spacing
    public BodyType BodyType;   // Picks the separation strength from MovementConfig
}

/// <summary>
/// On every unit (player and enemies): the horizontal velocity its movement wants this frame (input, AI, steering),
/// which UnitMoverSystem moves it by (plus knockback), colliding and sliding. Actual is how fast it really moved.
/// </summary>
public struct UnitMotion : IComponentData
{
    public float3 Velocity;
    public float3 Actual;
}

/// <summary>A unit's weight, for knockback (heavier units are pushed less). Units are kinematic, so physics mass is 0.</summary>
public struct BodyMass : IComponentData
{
    public float InverseMass;
}

/// <summary>Enabled: the unit treats water as floor (UnitMoverSystem). For skills or passives that let it walk on water.</summary>
public struct WaterWalking : IComponentData, IEnableableComponent {}

/// <summary>
/// Enabled: the unit moves through other units, and a phased player is moved through by enemies (UnitMoverSystem).
/// Walls, water and map edges still block. For skills or passives that grant it (e.g. Ghost).
/// </summary>
public struct Phased : IComponentData, IEnableableComponent {}
