using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

/// <summary>
/// On player ghosts (and Mirror Image copies, made from the same prefab): what the player is doing, for its animation and
/// effects on every machine (RigidAnimationSystem, VfxSystem). The host fills it from the player's skills and passives,
/// which only it has. The counters count up as a fireball, barrage bolt, tornado or Fists of Thunder punch goes out.
/// Portal is the one field set elsewhere (NetPortal), and kept here: the portal the player chose.
/// </summary>
public struct NetPlayerActions : IComponentData
{
    public const byte Dashing = 1, Spinning = 2, Winding = 4;

    [GhostField] public byte States;
    [GhostField] public byte Releases;
    [GhostField] public byte Shots;
    [GhostField] public byte Sweeps;
    [GhostField] public byte Punches;
    /// <summary>The enemy its Quarry passive marks, or Entity.Null.</summary>
    [GhostField] public Entity QuarryTarget;
    /// <summary>The portal (its scene) the player chose and stands at, or empty (NetPortal; PortalHUD shows it).</summary>
    [GhostField] public FixedString64Bytes Portal;

    public bool Has(byte state) => (States & state) != 0;
}

/// <summary>The host: fills each player's NetPlayerActions from its Dash, Whirlwind, Fireball, skill uses and Quarry.</summary>
[UpdateInGroup(typeof(HealthAndResourceSystemGroup), OrderLast = true)]
[WorldSystemFilter(GameWorlds.Server)]
[BurstCompile]
public partial struct NetPlayerActionsWriteSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var dashes = SystemAPI.GetComponentLookup<Dash>(true);
        var whirlwinds = SystemAPI.GetComponentLookup<Whirlwind>(true);
        var fireballs = SystemAPI.GetComponentLookup<FireBall>(true);
        var quarries = SystemAPI.GetComponentLookup<Quarry>(true);
        var skills = SystemAPI.GetBufferLookup<Skill>(true);
        foreach (var (actions, entity) in SystemAPI.Query<RefRW<NetPlayerActions>>().WithEntityAccess())
        {
            var value = actions.ValueRO;
            value.States = 0;
            if (dashes.HasComponent(entity) && dashes[entity].Dashing) value.States |= NetPlayerActions.Dashing;
            if (whirlwinds.HasComponent(entity) && whirlwinds[entity].Spinning) value.States |= NetPlayerActions.Spinning;
            if (fireballs.HasComponent(entity) && fireballs.IsComponentEnabled(entity)) value.States |= NetPlayerActions.Winding;

            if (skills.HasBuffer(entity))
                foreach (var skill in skills[entity])
                {
                    if (!skill.events.OnUsed) continue;
                    if (skill.Type == SkillType.Fireball) value.Releases++;
                    else if (skill.Type == SkillType.Barrage) value.Shots++;
                    else if (skill.Type == SkillType.Tornado) value.Sweeps++;
                    else if (skill.Type == SkillType.FistOfThunder) value.Punches++;
                }
            value.QuarryTarget = quarries.HasComponent(entity) ? quarries[entity].Target : Entity.Null;

            var old = actions.ValueRO;
            if (value.States != old.States || value.Releases != old.Releases || value.Shots != old.Shots || value.Sweeps != old.Sweeps ||
                value.Punches != old.Punches || value.QuarryTarget != old.QuarryTarget)
                actions.ValueRW = value;
        }
    }
}
