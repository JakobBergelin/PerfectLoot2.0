using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace LevelGeneration
{
    /// <summary>
    /// Moves the level's small animals (AmbientCreature, placed by LevelGenerationSystem from AmbientLifeSet). Each rests a
    /// few seconds, then wanders near its home; a player within its flee radius sends it off fast:
    /// - Darters (squirrels, rabbits) hop away along the forest's edge, never far from home.
    /// - Flyers (birds) peck, hop a little, and fly off in an arc to another open spot (AmbientPerch), their new home.
    /// - Swimmers (fish) drift around their spot and dart across it.
    /// Only in worlds that render: decoration, local to each machine, nothing networked.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.Presentation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [BurstCompile]
    public partial struct AmbientCreatureSystem : ISystem
    {
        const float DarterRange = 3f, DarterLeash = 8f, SwimmerRange = 1.2f, FlyerHop = 0.6f;

        public void OnCreate(ref SystemState state) => state.RequireForUpdate<AmbientCreature>();

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var players = new NativeList<float3>(Allocator.Temp);
            foreach (var transform in SystemAPI.Query<RefRO<LocalToWorld>>().WithAll<Player>())
                players.Add(transform.ValueRO.Position);
            var perches = new NativeList<float3>(Allocator.Temp);
            foreach (var buffer in SystemAPI.Query<DynamicBuffer<AmbientPerch>>())
                foreach (var perch in buffer) perches.Add(perch.Position);

            float dt = SystemAPI.Time.DeltaTime, time = (float)SystemAPI.Time.ElapsedTime;
            foreach (var (creature, transform) in SystemAPI.Query<RefRW<AmbientCreature>, RefRW<LocalTransform>>())
                Step(ref creature.ValueRW, ref transform.ValueRW, players, perches, dt, time);
        }

        static void Step(ref AmbientCreature c, ref LocalTransform t, NativeList<float3> players, NativeList<float3> perches, float dt, float time)
        {
            var position = t.Position;

            // The nearest player close enough to scare it
            float nearest = float.MaxValue;
            var threat = float3.zero;
            foreach (var player in players)
            {
                float d = math.distance(player.xz, position.xz);
                if (d < nearest) { nearest = d; threat = player; }
            }
            if (nearest < c.FleeRadius && c.Now != AmbientCreature.State.Fleeing)
            {
                var away = math.normalizesafe(position.xz - threat.xz, c.Random.NextFloat2Direction());
                var to = Flee(ref c, position, away, threat, perches);
                Go(ref c, position, to, c.Speed * (c.Kind == AmbientLifeSet.Kind.Flyer ? 5f : 3f), AmbientCreature.State.Fleeing);
            }

            float lift = 0f;
            var pose = quaternion.identity;
            if (c.Now == AmbientCreature.State.Resting)
            {
                c.Rest -= dt;
                // At rest: a squirrel twitches, a bird pecks, a fish sways
                float phase = time * 2f + c.Home.x;
                pose = c.Kind == AmbientLifeSet.Kind.Flyer ? quaternion.RotateX(math.max(0f, math.sin(phase * 2.3f)) * 0.5f)
                     : c.Kind == AmbientLifeSet.Kind.Swimmer ? quaternion.RotateY(math.sin(phase) * 0.25f)
                     : quaternion.RotateX(math.max(0f, math.sin(phase * 1.7f) - 0.8f) * -1.5f);
                if (c.Rest <= 0f)
                {
                    var to = c.Kind switch
                    {
                        AmbientLifeSet.Kind.Darter => c.Home + Disc(ref c, DarterRange),
                        AmbientLifeSet.Kind.Swimmer => c.Home + Disc(ref c, SwimmerRange),
                        _ => position + Disc(ref c, FlyerHop),
                    };
                    to.y = c.Kind == AmbientLifeSet.Kind.Flyer ? position.y : c.Home.y;
                    Go(ref c, position, to, c.Speed, AmbientCreature.State.Moving);
                }
            }
            else
            {
                c.Elapsed += dt;
                float p = math.saturate(c.Elapsed / c.Duration);
                float distance = math.distance(c.From.xz, c.To.xz);
                position = math.lerp(c.From, c.To, p);
                if (c.Kind == AmbientLifeSet.Kind.Darter)
                    lift = math.abs(math.sin(p * math.PI * math.max(1f, math.round(distance / 0.6f)))) * 0.18f;
                else if (c.Kind == AmbientLifeSet.Kind.Flyer && c.Now == AmbientCreature.State.Fleeing)
                {
                    lift = math.sin(p * math.PI) * math.min(7f, distance * 0.35f) + math.sin(time * 30f) * 0.05f;
                    pose = quaternion.RotateZ(math.sin(time * 40f) * 0.35f);   // Flapping
                }
                else if (c.Kind == AmbientLifeSet.Kind.Flyer)
                    lift = math.sin(p * math.PI) * 0.12f;
                if (p >= 1f)
                {
                    position = c.To;
                    c.Now = AmbientCreature.State.Resting;
                    c.Rest = c.Random.NextFloat(1.5f, 5f);
                }
            }

            t.Position = position + new float3(0f, lift, 0f);
            t.Rotation = math.mul(quaternion.RotateY(c.Yaw), pose);
        }

        // Where a scared creature runs to
        static float3 Flee(ref AmbientCreature c, float3 position, float2 away, float3 threat, NativeList<float3> perches)
        {
            if (c.Kind == AmbientLifeSet.Kind.Swimmer)
                return c.Home + new float3(away.x, 0f, away.y) * SwimmerRange;
            if (c.Kind == AmbientLifeSet.Kind.Darter)
            {
                var to = position + new float3(away.x, 0f, away.y) * c.FleeRadius * 1.5f;
                var fromHome = to - c.Home;
                if (math.length(fromHome.xz) > DarterLeash) to = c.Home + math.normalize(fromHome) * DarterLeash;
                to.y = c.Home.y;
                return to;
            }
            // A bird: another open spot away from the player, not too far; it lives there now
            for (int attempt = 0; attempt < 12 && perches.Length > 0; attempt++)
            {
                var perch = perches[c.Random.NextInt(perches.Length)];
                float hop = math.distance(perch.xz, position.xz);
                if (hop > 8f && hop < 30f && math.distance(perch.xz, threat.xz) > 12f)
                {
                    c.Home = perch;
                    return perch;
                }
            }
            return position + new float3(away.x, 0f, away.y) * 15f;
        }

        static void Go(ref AmbientCreature c, float3 from, float3 to, float speed, AmbientCreature.State state)
        {
            c.From = from;
            c.To = to;
            c.Elapsed = 0f;
            c.Duration = math.max(0.3f, math.distance(from, to) / speed);
            c.Now = state;
            var heading = to.xz - from.xz;
            if (math.lengthsq(heading) > 1e-4f) c.Yaw = math.atan2(heading.x, heading.y);
        }

        static float3 Disc(ref AmbientCreature c, float radius)
        {
            var point = c.Random.NextFloat2Direction() * radius * math.sqrt(c.Random.NextFloat());
            return new float3(point.x, 0f, point.y);
        }
    }
}
