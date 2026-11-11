using Unity.Collections;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// What an attack's shape covers (AttackShape; a sphere of its radius without one): the units it overlaps, which way a
/// hit pushes, and how far it reaches at most. Shared by AttackSystem and PeriodicAttackSystem.
/// </summary>
public static class AttackShapes
{
    /// <summary>The units the attack overlaps (hits gets cleared first).</summary>
    public static void Overlap(ref PhysicsWorldSingleton physics, in LocalTransform transform, float radius, bool hasShape, in AttackShape shape,
                               ref NativeList<DistanceHit> hits, CollisionFilter filter)
    {
        hits.Clear();
        if (hasShape && shape.Kind == AttackShapeKind.Rectangle)
        {
            physics.OverlapBox(transform.Position, transform.Rotation, shape.HalfExtents, ref hits, filter);
            return;
        }

        physics.OverlapSphere(transform.Position, radius, ref hits, filter);
        if (!hasShape || shape.Kind != AttackShapeKind.Cone) return;

        // Cone: only what's within its angle, along the ground (a unit counts when its nearest point is)
        float2 forward = Forward(transform);
        float minCos = math.cos(math.radians(math.clamp(shape.HalfAngle, 0f, 180f)));
        for (int i = hits.Length - 1; i >= 0; i--)
        {
            if (hits[i].Distance <= 0f) continue;   // The cone starts inside it
            float2 toHit = hits[i].Position.xz - transform.Position.xz;
            if (math.lengthsq(toHit) > 1e-6f && math.dot(math.normalize(toHit), forward) < minCos) hits.RemoveAt(i);
        }
    }

    /// <summary>
    /// The direction a hit pushes in, horizontally: away from a sphere's centre, and away from a rectangle's or cone's
    /// centre line (straight ahead when the target is on it).
    /// </summary>
    public static float3 HitDirection(in LocalTransform transform, bool hasShape, float3 targetPosition, ref Random random)
    {
        if (!hasShape) return AttackSystem.HitDirection(transform.Position, targetPosition, ref random);
        float2 forward = Forward(transform);
        float2 offset = targetPosition.xz - transform.Position.xz;
        float2 sideways = offset - forward * math.dot(offset, forward);
        float2 direction = math.lengthsq(sideways) > 1e-6f ? math.normalize(sideways) : forward;
        return new float3(direction.x, 0f, direction.y);
    }

    /// <summary>How far from its position the attack reaches at most (e.g. for enemies noticing a near miss).</summary>
    public static float OuterRadius(float radius, bool hasShape, in AttackShape shape) =>
        hasShape && shape.Kind == AttackShapeKind.Rectangle ? math.length(shape.HalfExtents) : radius;

    /// <summary>Area of effect: the whole shape grows (a cone's reach is its Radius, scaled with the attack).</summary>
    public static void Scale(ref AttackShape shape, float areaMultiplier) => shape.HalfExtents *= areaMultiplier;

    static float2 Forward(in LocalTransform transform)
    {
        float3 forward = math.mul(transform.Rotation, new float3(0f, 0f, 1f));
        return math.normalizesafe(forward.xz, new float2(0f, 1f));
    }
}
