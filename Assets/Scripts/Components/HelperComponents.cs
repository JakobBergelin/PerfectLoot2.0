using System;
using Unity.Entities;

public struct DestroyWithChildren : IComponentData {}

public struct Ground : IComponentData {}

public struct StandingStill : IComponentData {}

public struct Channeling : IComponentData{}
public struct DisableTurning : IComponentData{}

public struct InitializeFromSource : IComponentData{}

[Flags]
public enum CollisionLayerHelper
{
    None = 0,
    Ground = 1 << 6,
    Wall = 1 << 7,
    Player = 1 << 8,
    Enemy = 1 << 9,
    PlayerAttack = 1 << 10,
    EnemyAttack = 1 << 11,
    EnemySwarm = 1 << 14,
    AnyEnemy = Enemy | EnemySwarm,
    Unit = Player | AnyEnemy,
    Attack = PlayerAttack | EnemyAttack,
}
