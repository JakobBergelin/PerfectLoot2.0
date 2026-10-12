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

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class SubDamageSystemGroup : ComponentSystemGroup
{}

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class SkillSystemGroup : ComponentSystemGroup
{}

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class AttackSystemGroup : ComponentSystemGroup
{}
