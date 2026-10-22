using Unity.Entities;

[UpdateInGroup(typeof(InitializationSystemGroup))]
[UpdateAfter(typeof(BeginInitializationEntityCommandBufferSystem))]
public partial class CalculateStatsSystemGroup : ComponentSystemGroup
{}

// Stat calculation phases, in order:
// ResetStatsSystem (OrderFirst) -> Flat -> Derived -> Finalize (OrderLast)

/// <summary>Flat additions to CalculatedStats (class, equipment, resource types, buffs) and multiplier contributions.</summary>
[UpdateInGroup(typeof(CalculateStatsSystemGroup))]
public partial class FlatStatsSystemGroup : ComponentSystemGroup
{}

/// <summary>Systems that read CalculatedStats after all flat additions, e.g. Balance.</summary>
[UpdateInGroup(typeof(CalculateStatsSystemGroup))]
[UpdateAfter(typeof(FlatStatsSystemGroup))]
public partial class DerivedStatsSystemGroup : ComponentSystemGroup
{}

/// <summary>Writes CalculatedStats into the runtime components (Health, Resource, Armor, MovementSpeed).</summary>
[UpdateInGroup(typeof(CalculateStatsSystemGroup), OrderLast = true)]
public partial class FinalizeStatsSystemGroup : ComponentSystemGroup
{}

// Simulation phases, in order:
// Input (OrderFirst) -> Skill -> Movement -> Attack -> Damage -> HealthAndResource -> Feedback -> TransformSystemGroup
// UpdateAfter/UpdateBefore only work between systems in the same group, so systems order themselves inside their phase.

/// <summary>
/// Reads player input. First in SimulationSystemGroup because the Input System processes its events
/// just before Update, so this sees this frame's input (InitializationSystemGroup would see last frame's).
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
public partial class InputSystemGroup : ComponentSystemGroup
{}

/// <summary>Player and enemy skills: spend resources, turn the unit and spawn attacks.</summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(InputSystemGroup))]
public partial class SkillSystemGroup : ComponentSystemGroup
{}

/// <summary>Moves units and projectiles.</summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(SkillSystemGroup))]
public partial class MovementSystemGroup : ComponentSystemGroup
{}

/// <summary>Hit detection: turns attacks overlapping units into DamageInstances.</summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(MovementSystemGroup))]
public partial class AttackSystemGroup : ComponentSystemGroup
{}

/// <summary>
/// Resolves this frame's damage: costs paid in health (Huskar), resistances, damage passives, applying it,
/// knockback, status effect damage (SubDamageSystemGroup) and death.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(AttackSystemGroup))]
public partial class DamageSystemGroup : ComponentSystemGroup
{}

/// <summary>Status effect damage (bleed, chill, burn, poison, lightning). After DamagingSystem has applied the hits.</summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(DamagingSystem))]
public partial class SubDamageSystemGroup : ComponentSystemGroup
{}

/// <summary>Healing, regeneration and resource gains; UpdateResourcesSystem (last) applies this frame's resource changes.</summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(DamageSystemGroup))]
public partial class HealthAndResourceSystemGroup : ComponentSystemGroup
{}

/// <summary>Visual feedback: hit flash, health/resource bars and combat text events. Before transforms are calculated.</summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(HealthAndResourceSystemGroup))]
[UpdateBefore(typeof(Unity.Transforms.TransformSystemGroup))]
public partial class FeedbackSystemGroup : ComponentSystemGroup
{}
