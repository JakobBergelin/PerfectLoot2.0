using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

/// <summary>
/// On player ghosts (and Mirror Image copies, made from the same prefab): what the player is doing, for its animation and
/// effects on every machine (RigidAnimationSystem, VfxSystem). The host fills it from the player's skills and passives,
/// which only it has. Casts counts up each time a skill is used (a fireball, a barrage bolt, a punch), with the skill.
/// Portal is the one field set elsewhere (NetPortal), and kept here: the portal the player chose.
/// </summary>
public struct NetPlayerActions : IComponentData
{
    // States: what the player is doing (held or lasting actions)
    public const ushort Dashing = 1, Spinning = 2, Winding = 4, Channeling = 8, Guarding = 16, Rolling = 32, Flying = 64,
        Phasing = 128, Hooking = 256;
    // Auras: lasting effects on the player
    public const byte Invulnerable = 1, Immolating = 2, GaleWard = 4, GaleWardReversed = 8, BarrageOn = 16, Empowered = 32,
        Shadowed = 64;
    // Stance values (Stances skill); 0 is none
    public const byte StanceStrength = 1, StanceAgility = 2, StanceIntelligence = 3;

    /// <summary>Dashing (Dash), Spinning (Whirlwind), Winding (Fireball held), Channeling (Flamethrower, Tesla Coil),
    /// Guarding (Shield Wall raised), Rolling (Sonic Ball), Flying (Ball Lightning), Phasing (Phantom Edge flurry),
    /// Hooking (Hookshot Slash: hook out or pulling).</summary>
    [GhostField] public ushort States;
    /// <summary>Invulnerable, Immolating, GaleWard (with GaleWardReversed when it turns the other way), BarrageOn (toggle),
    /// Empowered (Blood Rite buff), Shadowed (Shadowstep buff).</summary>
    [GhostField] public byte Auras;
    [GhostField] public byte Stance;
    /// <summary>Counts up each time the player uses a skill; CastSkill is the last one used (a SkillType).</summary>
    [GhostField] public byte Casts;
    [GhostField] public byte CastSkill;
    /// <summary>While Hooking: where the hook is.</summary>
    [GhostField(Quantization = 100)] public float3 HookPoint;
    /// <summary>The enemy its Quarry passive marks, or Entity.Null.</summary>
    [GhostField] public Entity QuarryTarget;
    /// <summary>The portal (its scene) the player chose and stands at, or empty (NetPortal; PortalHUD shows it).</summary>
    [GhostField] public FixedString64Bytes Portal;

    public bool Has(ushort state) => (States & state) != 0;
    public bool HasAura(byte aura) => (Auras & aura) != 0;
}

/// <summary>
/// The host: fills each player's NetPlayerActions from its skills' own state (read only), skill uses and Quarry. Phasing
/// hides the Invulnerable aura: Phantom Edge keeps its caster invulnerable while it's gone.
/// </summary>
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
        var flamethrowers = SystemAPI.GetComponentLookup<Flamethrower>(true);
        var teslaCoils = SystemAPI.GetComponentLookup<TeslaCoil>(true);
        var shieldWalls = SystemAPI.GetComponentLookup<ShieldWall>(true);
        var sonicBalls = SystemAPI.GetComponentLookup<SonicBall>(true);
        var ballLightnings = SystemAPI.GetComponentLookup<BallLightning>(true);
        var flurries = SystemAPI.GetComponentLookup<PhantomEdgeFlurry>(true);
        var hookshots = SystemAPI.GetComponentLookup<HookshotSlash>(true);
        var invulnerables = SystemAPI.GetComponentLookup<Invulnerable>(true);
        var immolations = SystemAPI.GetComponentLookup<Immolation>(true);
        var galeWards = SystemAPI.GetComponentLookup<GaleWard>(true);
        var barrages = SystemAPI.GetComponentLookup<Barrage>(true);
        var bloodRites = SystemAPI.GetComponentLookup<BloodRiteBuff>(true);
        var shadowsteps = SystemAPI.GetComponentLookup<ShadowstepBuff>(true);
        var quarries = SystemAPI.GetComponentLookup<Quarry>(true);
        var skills = SystemAPI.GetBufferLookup<Skill>(true);
        foreach (var (actions, entity) in SystemAPI.Query<RefRW<NetPlayerActions>>().WithEntityAccess())
        {
            var value = actions.ValueRO;
            value.States = 0;
            if (dashes.HasComponent(entity) && dashes[entity].Dashing) value.States |= NetPlayerActions.Dashing;
            if (whirlwinds.HasComponent(entity) && whirlwinds[entity].Spinning) value.States |= NetPlayerActions.Spinning;
            if (fireballs.HasComponent(entity) && fireballs.IsComponentEnabled(entity)) value.States |= NetPlayerActions.Winding;
            if (flamethrowers.HasComponent(entity) && flamethrowers[entity].Spraying || teslaCoils.HasComponent(entity) && teslaCoils[entity].Channeling)
                value.States |= NetPlayerActions.Channeling;
            if (shieldWalls.HasComponent(entity) && shieldWalls[entity].Raised) value.States |= NetPlayerActions.Guarding;
            if (sonicBalls.HasComponent(entity) && sonicBalls[entity].Rolling) value.States |= NetPlayerActions.Rolling;
            if (ballLightnings.HasComponent(entity) && ballLightnings[entity].Flying) value.States |= NetPlayerActions.Flying;
            if (flurries.HasComponent(entity) && flurries[entity].Active) value.States |= NetPlayerActions.Phasing;
            if (hookshots.HasComponent(entity) && hookshots[entity].State != HookshotState.Idle)
            {
                value.States |= NetPlayerActions.Hooking;
                value.HookPoint = hookshots[entity].HookPosition;
            }

            value.Auras = 0;
            if (Enabled(invulnerables, entity) && !value.Has(NetPlayerActions.Phasing)) value.Auras |= NetPlayerActions.Invulnerable;
            if (immolations.HasComponent(entity) && immolations[entity].Active) value.Auras |= NetPlayerActions.Immolating;
            // Direction: 1 clockwise, -1 counter-clockwise (the second press), 0 off or paused for lack of resource
            float wind = galeWards.HasComponent(entity) ? galeWards[entity].Direction : 0f;
            if (wind != 0f) value.Auras |= NetPlayerActions.GaleWard;
            if (wind < 0f) value.Auras |= NetPlayerActions.GaleWardReversed;
            if (barrages.HasComponent(entity) && barrages[entity].Active) value.Auras |= NetPlayerActions.BarrageOn;
            if (Enabled(bloodRites, entity)) value.Auras |= NetPlayerActions.Empowered;
            if (Enabled(shadowsteps, entity)) value.Auras |= NetPlayerActions.Shadowed;

            value.Stance = 0;
            if (skills.HasBuffer(entity))
                foreach (var skill in skills[entity])
                {
                    // Stances' Variant: 0 Strength, 1 Agility, 2 Intelligence; it gives its bonus while usable
                    if (skill.Type == SkillType.Stances && skill.State.Usable)
                        value.Stance = (byte)(NetPlayerActions.StanceStrength + math.clamp(skill.State.Variant, 0, 2));
                    if (!skill.events.OnUsed) continue;
                    value.Casts++;
                    value.CastSkill = (byte)skill.Type;
                }
            value.QuarryTarget = quarries.HasComponent(entity) ? quarries[entity].Target : Entity.Null;

            var old = actions.ValueRO;
            if (value.States != old.States || value.Auras != old.Auras || value.Stance != old.Stance || value.Casts != old.Casts ||
                value.CastSkill != old.CastSkill || !value.HookPoint.Equals(old.HookPoint) || value.QuarryTarget != old.QuarryTarget)
                actions.ValueRW = value;
        }
    }

    static bool Enabled<T>(ComponentLookup<T> lookup, Entity entity) where T : unmanaged, IComponentData, IEnableableComponent =>
        lookup.HasComponent(entity) && lookup.IsComponentEnabled(entity);
}
