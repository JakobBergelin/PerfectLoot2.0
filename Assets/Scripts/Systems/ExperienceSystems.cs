using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

/// <summary>How much experience each level takes. The one place to tune the curve and the max level.</summary>
public static class Experience
{
    public const int MaxLevel = 10;

    // 2,500 from level 1 to 2, then 750 more for each level: 49,500 from 1 to 10, about 15 dungeons
    const int FirstLevel = 2500, MorePerLevel = 750;

    /// <summary>Experience from this level to the next; 0 at the max level.</summary>
    public static int ToNextLevel(int level) => level >= MaxLevel ? 0 : FirstLevel + MorePerLevel * (level - 1);

    /// <summary>Gives every player this much experience (a finished dungeon). ExperienceSystem levels them up.</summary>
    public static void GiveAllPlayers(EntityManager entityManager, int amount)
    {
        if (amount <= 0) return;
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>(), ComponentType.ReadWrite<Level>());
        using var players = query.ToEntityArray(Allocator.Temp);
        foreach (var player in players)
        {
            var level = entityManager.GetComponentData<Level>(player);
            level.Experience += amount;
            entityManager.SetComponentData(player, level);
        }
    }
}

/// <summary>
/// Every enemy that died this frame gives its experience to every player (once per enemy), then players with enough
/// experience level up, up to Experience.MaxLevel. Runs just before CheckDeathSystem, which removes dead enemies.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(DamageSystemGroup), OrderLast = true)]
[UpdateBefore(typeof(CheckDeathSystem))]
public partial struct ExperienceSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        int gained = 0;
        var given = new NativeList<Entity>(Allocator.Temp);
        foreach (var (health, reward, entity) in SystemAPI.Query<RefRO<Health>, RefRO<ExperienceReward>>()
                     .WithNone<ExperienceGiven>().WithEntityAccess())
        {
            if (health.ValueRO.CurrentValue > 0f) continue;
            gained += reward.ValueRO.Value;
            given.Add(entity);
        }
        if (!given.IsEmpty) state.EntityManager.AddComponent<ExperienceGiven>(given.AsArray());

        foreach (var level in SystemAPI.Query<RefRW<Level>>().WithAll<Player>())
        {
            ref var player = ref level.ValueRW;
            player.Experience += gained;
            while (player.Value < Experience.MaxLevel && player.Experience >= Experience.ToNextLevel(player.Value))
            {
                player.Experience -= Experience.ToNextLevel(player.Value);
                player.Value++;
            }
            // Nothing more to earn at the max level
            if (player.Value >= Experience.MaxLevel) player.Experience = 0;
        }
    }
}
