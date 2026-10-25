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

/// <summary>
/// Last in SkillSystemGroup: reactions to this frame's skill uses. SkillTagEventSystem first turns uses into events
/// (MeleeAttackUsed, MobilitySkillUsed), then e.g. legendary powers react. Casts they request (SkillCastRequest) are
/// handled by the skill systems next frame.
/// </summary>
[UpdateInGroup(typeof(SkillSystemGroup), OrderLast = true)]
public partial class SkillReactionSystemGroup : ComponentSystemGroup
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
/// knockback, buffs and debuffs (EffectSystemGroup), lightning charges and death.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(AttackSystemGroup))]
public partial class DamageSystemGroup : ComponentSystemGroup
{}

/// <summary>
/// Buffs and debuffs (enableable effect components): freezes fully chilled units, deals damage over time, works out
/// slows and stuns, and counts timed effects down (last). After DamagingSystem, which applies the status effects.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(DamagingSystem))]
public partial class EffectSystemGroup : ComponentSystemGroup
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
