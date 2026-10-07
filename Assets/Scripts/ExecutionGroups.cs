using Unity.Entities;

[UpdateInGroup(typeof(InitializationSystemGroup))]
[UpdateAfter(typeof(BeginInitializationEntityCommandBufferSystem))]
public partial class CalculateStatsSystemGroup : ComponentSystemGroup
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