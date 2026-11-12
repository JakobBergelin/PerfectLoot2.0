using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>
/// Picks each rigid model's clip from gameplay state and poses its bones: Death while RigidDeath, Hit when a hit flash
/// starts, Attack when a melee attack fires (both play once), otherwise Move or Idle from how fast it moves.
/// The player's skills are read from their own state: Dash while dashing, Spin while whirlwinding, a punch per Fists of
/// Thunder hit, Release / Shoot / Sweep when a fireball, barrage bolt or tornado goes out (once each), Cast while
/// Fireball is held. Clip changes cross-fade. Before transforms are calculated, like the other visual feedback.
/// </summary>
[UpdateInGroup(typeof(FeedbackSystemGroup))]
[BurstCompile]
public partial struct RigidAnimationSystem : ISystem
{
    const float CrossFade = 0.12f;
    // Horizontal speed (m/s) above which Move starts, and below which it goes back to Idle
    const float MoveSpeed = 0.5f;
    const float StopSpeed = 0.25f;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        if (deltaTime <= 0f) return;

        var transforms = SystemAPI.GetComponentLookup<LocalTransform>();
        var behaviors = SystemAPI.GetComponentLookup<NpcBehavior>(true);
        var feedbacks = SystemAPI.GetComponentLookup<NetUnitFeedback>(true);
        var flashes = SystemAPI.GetComponentLookup<HitFlash>(true);
        var deaths = SystemAPI.GetComponentLookup<RigidDeath>(true);
        var motions = SystemAPI.GetComponentLookup<UnitMotion>(true);
        var fireballs = SystemAPI.GetComponentLookup<FireBall>(true);
        var skills = SystemAPI.GetBufferLookup<Skill>(true);
        var fists = SystemAPI.GetComponentLookup<FistsOfThunder>(true);
        var whirlwinds = SystemAPI.GetComponentLookup<Whirlwind>(true);
        var dashes = SystemAPI.GetComponentLookup<Dash>(true);

        foreach (var (animatorRef, bones, entity) in SystemAPI.Query<RefRW<RigidAnimator>, DynamicBuffer<RigidBone>>().WithEntityAccess())
        {
            ref var animator = ref animatorRef.ValueRW;
            if (!animator.Animation.IsCreated || !transforms.HasComponent(entity)) continue;
            ref var animation = ref animator.Animation.Value;

            var position = transforms[entity].Position;
            // In someone else's game the host's count comes with the ghost
            int attackCount = behaviors.HasComponent(entity) ? behaviors[entity].AttackCount : feedbacks.HasComponent(entity) ? feedbacks[entity].Attacks : 0;
            float flashTimer = flashes.HasComponent(entity) ? flashes[entity].Timer : 0f;
            if (!animator.Started)
            {
                animator.Started = true;
                animator.LastPosition = position;
                animator.LastAttackCount = attackCount;
                animator.LastFlashTimer = flashTimer;
            }

            // Units: how fast UnitMoverSystem actually moved them; anything else from its position change
            float speed = motions.HasComponent(entity)
                ? math.length(motions[entity].Actual.xz)
                : math.length((position - animator.LastPosition).xz) / deltaTime;
            bool attacked = attackCount != animator.LastAttackCount;
            bool hit = flashTimer > animator.LastFlashTimer + 0.001f;
            animator.LastPosition = position;
            animator.LastAttackCount = attackCount;
            animator.LastFlashTimer = flashTimer;

            bool released = WasUsed(skills, entity, SkillType.Fireball);
            bool shot = WasUsed(skills, entity, SkillType.Barrage);
            bool swept = WasUsed(skills, entity, SkillType.Tornado);
            bool punched = WasUsed(skills, entity, SkillType.FistOfThunder);
            int punchCount = fists.HasComponent(entity) ? fists[entity].PunchCount : 0;
            bool dashing = dashes.HasComponent(entity) && dashes[entity].Dashing;
            bool spinning = whirlwinds.HasComponent(entity) && whirlwinds[entity].Spinning;
            bool winding = fireballs.HasComponent(entity) && fireballs.IsComponentEnabled(entity);

            var wanted = animator.Clip;
            bool restart = false;
            if (deaths.HasComponent(entity) && animation.Has(RigidClip.Death))
            {
                wanted = RigidClip.Death;
            }
            else if (dashing && animation.Has(RigidClip.Dash))
            {
                wanted = RigidClip.Dash;
            }
            else if (spinning && animation.Has(RigidClip.Spin))
            {
                wanted = RigidClip.Spin;
            }
            else if (punched && animation.Has(RigidClip.PunchL))
            {
                wanted = punchCount % 2 == 1 ? RigidClip.PunchR : RigidClip.PunchL;
                restart = true;
            }
            else if (released && animation.Has(RigidClip.Release))
            {
                wanted = RigidClip.Release;
                restart = true;
            }
            else if (shot && animation.Has(RigidClip.Shoot))
            {
                wanted = RigidClip.Shoot;
                restart = true;
            }
            else if (swept && animation.Has(RigidClip.Sweep))
            {
                wanted = RigidClip.Sweep;
                restart = true;
            }
            else if (hit && animation.Has(RigidClip.Hit))
            {
                wanted = RigidClip.Hit;
                restart = true;
            }
            else if (attacked && animation.Has(RigidClip.Attack))
            {
                wanted = RigidClip.Attack;
                restart = true;
            }
            else if (!PlayingOnce(ref animation, animator.Clip, animator.Time))
            {
                bool moving = speed > (animator.Clip == RigidClip.Move ? StopSpeed : MoveSpeed);
                wanted = winding && animation.Has(RigidClip.Cast) ? RigidClip.Cast
                    : moving && animation.Has(RigidClip.Move) ? RigidClip.Move : RigidClip.Idle;
            }

            if (wanted != animator.Clip || restart)
            {
                animator.FromClip = animator.Clip;
                animator.FromTime = animator.Time;
                animator.Clip = wanted;
                animator.Time = 0f;
                animator.Blend = 0f;
            }
            else
            {
                animator.Time += deltaTime;
            }
            animator.FromTime += deltaTime;
            animator.Blend = math.min(1f, animator.Blend + deltaTime / CrossFade);

            for (int b = 0; b < bones.Length && b < animation.BoneCount; b++)
            {
                var bone = bones[b].Value;
                if (!transforms.HasComponent(bone)) continue;
                var pose = Sample(ref animation, animator.Clip, animator.Time, b);
                if (animator.Blend < 1f && animation.Has(animator.FromClip))
                {
                    var from = Sample(ref animation, animator.FromClip, animator.FromTime, b);
                    pose.Position = math.lerp(from.Position, pose.Position, animator.Blend);
                    pose.Rotation = math.slerp(from.Rotation, pose.Rotation, animator.Blend);
                    pose.Scale = math.lerp(from.Scale, pose.Scale, animator.Blend);
                }
                transforms[bone] = pose;
            }
        }
    }

    // Attacks, hits, punches and casts play to their end before the unit goes back to Idle or Move
    static bool PlayingOnce(ref RigidAnimationBlob animation, RigidClip clip, float time) =>
        (clip == RigidClip.Attack || clip == RigidClip.Hit || clip == RigidClip.PunchL || clip == RigidClip.PunchR ||
         clip == RigidClip.Release || clip == RigidClip.Shoot || clip == RigidClip.Sweep) &&
        animation.Has(clip) && time < animation.Clips[(int)clip].Length;

    // A skill timer that was full last frame and dropped: the skill just fired
    // Skills raise this on the frame they go out
    static bool WasUsed(in BufferLookup<Skill> skills, Entity entity, SkillType type)
    {
        if (!skills.HasBuffer(entity)) return false;
        foreach (var skill in skills[entity])
            if (skill.Type == type && skill.events.OnUsed) return true;
        return false;
    }

    static LocalTransform Sample(ref RigidAnimationBlob animation, RigidClip kind, float time, int bone)
    {
        ref var clip = ref animation.Clips[(int)kind];
        if (clip.Frames <= 0) return LocalTransform.Identity;
        float t = clip.Loop && clip.Length > 0f ? time - math.floor(time / clip.Length) * clip.Length : math.min(time, clip.Length);
        float frame = math.min(t * clip.SampleRate, clip.Frames - 1);
        int a = (int)math.floor(frame);
        int b = math.min(a + 1, clip.Frames - 1);
        float f = frame - a;
        int count = animation.BoneCount;
        return new LocalTransform
        {
            Position = math.lerp(clip.Positions[a * count + bone], clip.Positions[b * count + bone], f),
            Rotation = math.slerp(clip.Rotations[a * count + bone], clip.Rotations[b * count + bone], f),
            Scale = math.lerp(clip.Scales[a * count + bone], clip.Scales[b * count + bone], f),
        };
    }
}

/// <summary>
/// Turns dying rigid models (CheckDeathSystem) into inert bodies playing their Death clip: they stop being targets,
/// bodies and AI, and their bars disappear. They sink into the ground at the end and are destroyed.
/// </summary>
public static class RigidDeathUtility
{
    const float Linger = 0.8f;      // Seconds the body stays after the clip ends
    public const float SinkTime = 0.6f;

    public static bool HasDeathClip(in RigidAnimator animator) =>
        animator.Animation.IsCreated && animator.Animation.Value.Has(RigidClip.Death);

    public static void StartDying(EntityManager entityManager, NativeArray<Entity> entities)
    {
        for (int i = 0; i < entities.Length; i++)
        {
            var entity = entities[i];
            var animator = entityManager.GetComponentData<RigidAnimator>(entity);
            float length = animator.Animation.Value.Clips[(int)RigidClip.Death].Length;
            ShowDying(entityManager, entity, length + Linger);
        }

        entityManager.RemoveComponent(entities, new ComponentTypeSet(new FixedList128Bytes<ComponentType>
        {
            ComponentType.ReadWrite<Health>(),
            ComponentType.ReadWrite<HealthRegeneration>(),
            ComponentType.ReadWrite<HealthBarUI>(),
            ComponentType.ReadWrite<Enemy>(),
            ComponentType.ReadWrite<NpcBehavior>(),
            ComponentType.ReadWrite<EnemyMeleeAttack>(),
            ComponentType.ReadWrite<EnemySkillCast>(),
            ComponentType.ReadWrite<EnemySkillFire>(),
            ComponentType.ReadWrite<Steering>(),
        }));
        // Out of the physics world: nothing hits it, pushes it or stands on it
        entityManager.RemoveComponent(entities, new ComponentTypeSet(new FixedList128Bytes<ComponentType>
        {
            ComponentType.ReadWrite<PhysicsCollider>(),
            ComponentType.ReadWrite<PhysicsVelocity>(),
            ComponentType.ReadWrite<PhysicsMass>(),
            ComponentType.ReadWrite<PhysicsDamping>(),
            ComponentType.ReadWrite<PhysicsGravityFactor>(),
            ComponentType.ReadWrite<PhysicsWorldIndex>(),
        }));
    }

    /// <summary>
    /// The look of dying: its Death clip (RigidDeath, sinking and destroyed after timer) and no bars. All another machine
    /// shows of a host's dying unit, with a timer that doesn't end (NetUnitFeedbackApplySystem).
    /// </summary>
    public static void ShowDying(EntityManager entityManager, Entity entity, float timer)
    {
        entityManager.AddComponentData(entity, new RigidDeath { Timer = timer });
        if (!entityManager.HasComponent<HealthBarUI>(entity)) return;
        var bars = entityManager.GetComponentData<HealthBarUI>(entity);
        HideHierarchy(entityManager, bars.BarEntity);
        HideHierarchy(entityManager, bars.StickEntity);
    }

    static void HideHierarchy(EntityManager entityManager, Entity entity)
    {
        if (entity == Entity.Null || !entityManager.Exists(entity)) return;
        if (entityManager.HasComponent<MaterialMeshInfo>(entity) && !entityManager.HasComponent<DisableRendering>(entity))
            entityManager.AddComponent<DisableRendering>(entity);
        if (!entityManager.HasBuffer<Child>(entity)) return;
        var children = entityManager.GetBuffer<Child>(entity).ToNativeArray(Allocator.Temp);
        foreach (var child in children) HideHierarchy(entityManager, child.Value);
    }
}

/// <summary>Counts dying bodies down, sinks them into the ground for the last moment and then destroys them.</summary>
[UpdateInGroup(typeof(FeedbackSystemGroup))]
[UpdateBefore(typeof(RigidAnimationSystem))]
[BurstCompile]
public partial struct RigidDeathSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        var done = new NativeList<Entity>(Allocator.Temp);
        foreach (var (death, transform, entity) in SystemAPI.Query<RefRW<RigidDeath>, RefRW<LocalTransform>>().WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            death.ValueRW.Timer -= deltaTime;
            if (death.ValueRO.Timer < RigidDeathUtility.SinkTime)
                transform.ValueRW.Position.y -= deltaTime * 2f * transform.ValueRO.Scale / RigidDeathUtility.SinkTime;
            if (death.ValueRO.Timer <= 0f) done.Add(entity);
        }
        if (!done.IsEmpty) state.EntityManager.AddComponent<DestroyWithChildren>(done.AsArray());
    }
}
