using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Shakes the camera when hits land: a skill's hit by its Crit Shake when it crits, else by its Hit Shake (ActiveSkill, in
/// its caster's Skill buffer), and a hit on a player that takes at least PlayerHitShare of their max health by
/// PlayerHitShake. A crit at BigHitShake or more is a big hit and also pulses (CameraFollow.Pulse). The strongest hit of
/// the frame counts, not their sum, so an area hit on a crowd, or four players' hits, shake like one. Companions' hits
/// count for their owner, their Source. Between working out the damage and dealing it, while the hits are still in
/// CalculatedDamageInstance. Impacts that hit nothing shake through their effect (VfxLibrary's Shake, VfxSystem).
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(CalculateDamageSystem))]
[UpdateBefore(typeof(DamagingSystem))]
[BurstCompile]
public partial struct HitShakeSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<CameraFollow>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var settings = SystemAPI.GetSingleton<CameraFollow>();
        var skills = SystemAPI.GetBufferLookup<Skill>(true);
        var players = SystemAPI.GetComponentLookup<Player>(true);
        var healths = SystemAPI.GetComponentLookup<Health>(true);
        float shake = 0f, pulse = 0f;

        foreach (var (taken, target) in SystemAPI.Query<DynamicBuffer<CalculatedDamageInstance>>().WithEntityAccess())
        {
            float maxHealth = players.HasComponent(target) && healths.HasComponent(target) ? healths[target].MaxValue : 0f;
            foreach (var instance in taken)
            {
                if (instance.Hierarchy != DamageHierarchy.Main || instance.Damage <= 0f) continue;
                if (maxHealth > 0f && instance.Damage >= settings.PlayerHitShare * maxHealth) shake = math.max(shake, settings.PlayerHitShake);
                if (!skills.HasBuffer(instance.Source)) continue;
                foreach (var skill in skills[instance.Source])
                {
                    if (skill.Label != instance.Origin.Label || skill.Label == DamageLabel.None) continue;
                    shake = math.max(shake, instance.IsCrit ? skill.CritShake : skill.HitShake);
                    if (instance.IsCrit && skill.CritShake > 0f && skill.CritShake >= settings.BigHitShake) pulse = math.max(pulse, settings.BigHitPulse);
                }
            }
        }

        if (shake <= 0f && pulse <= 0f) return;
        foreach (var follow in SystemAPI.Query<RefRW<CameraFollow>>())
        {
            follow.ValueRW.Trauma = math.max(follow.ValueRO.Trauma, shake);
            follow.ValueRW.Pulse = math.max(follow.ValueRO.Pulse, pulse);
        }
    }
}

/// <summary>
/// A boss's intro: the first time a CameraFocus (dungeon bosses, enemies with Camera Focus ticked) comes within the
/// camera's FocusRange of a player, the camera shakes by FocusIntroShake and pulses fully. Once per focus.
/// </summary>
[UpdateAfter(typeof(TransformSystemGroup))]
[BurstCompile]
public partial struct FocusIntroSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<CameraFollow>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var settings = SystemAPI.GetSingleton<CameraFollow>();
        var players = new NativeList<float3>(Allocator.Temp);
        foreach (var transform in SystemAPI.Query<RefRO<LocalToWorld>>().WithAll<Player>()) players.Add(transform.ValueRO.Position);
        if (players.Length == 0) return;

        var commands = new EntityCommandBuffer(Allocator.Temp);
        bool intro = false;
        float rangeSq = settings.FocusRange * settings.FocusRange;
        foreach (var (transform, entity) in SystemAPI.Query<RefRO<LocalToWorld>>().WithAll<CameraFocus>().WithNone<FocusIntroPlayed, RigidDeath>().WithEntityAccess())
        {
            float2 position = transform.ValueRO.Position.xz;
            foreach (var player in players)
            {
                if (math.distancesq(player.xz, position) > rangeSq) continue;
                intro = true;
                commands.AddComponent<FocusIntroPlayed>(entity);
                break;
            }
        }
        commands.Playback(state.EntityManager);

        if (!intro) return;
        foreach (var follow in SystemAPI.Query<RefRW<CameraFollow>>())
        {
            follow.ValueRW.Trauma = math.max(follow.ValueRO.Trauma, settings.FocusIntroShake);
            follow.ValueRW.Pulse = 1f;
        }
    }
}
