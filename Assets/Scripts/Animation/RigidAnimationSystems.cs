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
/// Clip changes cross-fade. Before transforms are calculated, like the other visual feedback.
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
        var attacks = SystemAPI.GetComponentLookup<EnemyMeleeAttack>(true);
        var behaviors = SystemAPI.GetComponentLookup<NpcBehavior>(true);
        var flashes = SystemAPI.GetComponentLookup<HitFlash>(true);
        var deaths = SystemAPI.GetComponentLookup<RigidDeath>(true);
        var velocities = SystemAPI.GetComponentLookup<PhysicsVelocity>(true);

        foreach (var (animatorRef, bones, entity) in SystemAPI.Query<RefRW<RigidAnimator>, DynamicBuffer<RigidBone>>().WithEntityAccess())
        {
            ref var animator = ref animatorRef.ValueRW;
            if (!animator.Animation.IsCreated || !transforms.HasComponent(entity)) continue;
            ref var animation = ref animator.Animation.Value;

            var position = transforms[entity].Position;
            float attackElapsed = attacks.HasComponent(entity) ? attacks[entity].Elapsed : 0f;
            float flashTimer = flashes.HasComponent(entity) ? flashes[entity].Timer : 0f;
            if (!animator.Started)
            {
                animator.Started = true;
                animator.LastPosition = position;
                animator.LastAttackElapsed = attackElapsed;
                animator.LastFlashTimer = flashTimer;
            }

            // Bodies: their velocity (physics steps at a fixed rate, so most rendered frames don't move them at all)
            float speed = velocities.HasComponent(entity)
                ? math.length(velocities[entity].Linear.xz)
                : math.length((position - animator.LastPosition).xz) / deltaTime;
            // The melee timer drops from full to 0 when the attack fires (and resets every frame while not attacking)
            bool attacked = attacks.HasComponent(entity) && behaviors.HasComponent(entity) && behaviors[entity].IsAttacking &&
                            attackElapsed < animator.LastAttackElapsed && animator.LastAttackElapsed >= attacks[entity].AttackSpeed - 0.001f;
            bool hit = flashTimer > animator.LastFlashTimer + 0.001f;
            animator.LastPosition = position;
            animator.LastAttackElapsed = attackElapsed;
            animator.LastFlashTimer = flashTimer;

            var wanted = animator.Clip;
            bool restart = false;
            if (deaths.HasComponent(entity) && animation.Has(RigidClip.Death))
            {
                wanted = RigidClip.Death;
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
                wanted = moving && animation.Has(RigidClip.Move) ? RigidClip.Move : RigidClip.Idle;
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

    // Attack and Hit play to their end before the unit goes back to Idle or Move
    static bool PlayingOnce(ref RigidAnimationBlob animation, RigidClip clip, float time) =>
        (clip == RigidClip.Attack || clip == RigidClip.Hit) && animation.Has(clip) && time < animation.Clips[(int)clip].Length;

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
            entityManager.AddComponentData(entity, new RigidDeath { Timer = length + Linger });
            if (entityManager.HasComponent<HealthBarUI>(entity))
            {
                var bars = entityManager.GetComponentData<HealthBarUI>(entity);
                HideHierarchy(entityManager, bars.PivotEntity);
                HideHierarchy(entityManager, bars.StickEntity);
            }
        }

        entityManager.RemoveComponent(entities, new ComponentTypeSet(new FixedList128Bytes<ComponentType>
        {
            ComponentType.ReadWrite<Health>(),
            ComponentType.ReadWrite<HealthRegeneration>(),
            ComponentType.ReadWrite<HealthBarUI>(),
            ComponentType.ReadWrite<Enemy>(),
            ComponentType.ReadWrite<NpcBehavior>(),
            ComponentType.ReadWrite<EnemyMeleeAttack>(),
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
