using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace LevelGeneration
{
    /// <summary>
    /// Puts units on the ground: a ray straight down against the Ground layer finds the floor, and the unit is placed so
    /// the lowest point of its collider is just above it. Spawning a capsule partly inside the floor makes physics push
    /// it out, launching it upwards.
    /// </summary>
    public static class GroundPlacement
    {
        /// <summary>Gap between the collider's lowest point and the ground.</summary>
        public const float Skin = 0.05f;
        const float RayStartHeight = 20f;
        const float RayLength = 60f;

        /// <summary>The current physics world (completes the jobs writing it), or false before physics has run.</summary>
        public static bool TryGetCollisionWorld(EntityManager entityManager, out CollisionWorld collisionWorld)
        {
            collisionWorld = default;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PhysicsWorldSingleton>());
            if (query.IsEmpty) return false;
            query.CompleteDependency();
            collisionWorld = query.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
            return true;
        }

        /// <summary>The ground point under (or near above) position: a ray down from RayStartHeight above it.</summary>
        public static bool TryFindGround(in CollisionWorld collisionWorld, float3 position, out float3 ground)
        {
            var input = new RaycastInput
            {
                Start = position + new float3(0f, RayStartHeight, 0f),
                End = position + new float3(0f, RayStartHeight - RayLength, 0f),
                Filter = new CollisionFilter { BelongsTo = ~0u, CollidesWith = (uint)CollisionLayerHelper.Ground },
            };
            bool hit = collisionWorld.CastRay(input, out var closest);
            ground = hit ? closest.Position : position;
            return hit;
        }

        /// <summary>How far below its position an entity's collider reaches (0 without a collider), at its rotation and scale.</summary>
        public static float BottomOffset(EntityManager entityManager, Entity entity)
        {
            if (!entityManager.HasComponent<PhysicsCollider>(entity)) return 0f;
            var collider = entityManager.GetComponentData<PhysicsCollider>(entity);
            if (!collider.IsValid) return 0f;
            var transform = entityManager.HasComponent<LocalTransform>(entity) ? entityManager.GetComponentData<LocalTransform>(entity) : LocalTransform.Identity;
            var aabb = collider.Value.Value.CalculateAabb(new RigidTransform(transform.Rotation, float3.zero), transform.Scale);
            return math.max(0f, -aabb.Min.y);
        }

        /// <summary>The position with its height set so a unit with this bottom offset stands on the ground. False when there's no ground.</summary>
        public static bool TryPlace(in CollisionWorld collisionWorld, float3 position, float bottomOffset, out float3 placed)
        {
            placed = position;
            if (!TryFindGround(collisionWorld, position, out var ground)) return false;
            placed.y = ground.y + bottomOffset + Skin;
            return true;
        }
    }
}
