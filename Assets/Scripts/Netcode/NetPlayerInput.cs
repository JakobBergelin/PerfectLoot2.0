using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

/// <summary>
/// On every player ghost (baked by NetReplicatedAuthoring): which local player it is on the machine that owns it
/// (0 or 1 with couch co-op), so that machine's CharacterManager with the same PlayerIndex adopts it.
/// </summary>
public struct NetPlayer : IComponentData
{
    [GhostField] public int LocalIndex;
}

/// <summary>
/// A player's input as a network command, sent every tick by the machine that owns the player ghost
/// (NetInputSendSystem) and turned back into its InputsData on the host (NetInputReceiveSystem).
/// </summary>
public struct NetPlayerInput : IInputComponentData
{
    public float2 Move;
    public float3 Aim;          // InputsData.MousePosition
    public int Held;            // Skill slots held, bit per slot
}

/// <summary>On the host, a player controlled by a remote client; remembers last tick's held skills to find presses.</summary>
public struct RemotePlayer : IComponentData
{
    public int LastHeld;
}

/// <summary>
/// On a machine that owns player ghosts, copies this frame's InputsData (filled by InputsDataSystem, as for any local
/// player) into their command.
/// </summary>
[UpdateInGroup(typeof(GhostInputSystemGroup))]
public partial struct NetInputSendSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (input, data) in SystemAPI.Query<RefRW<NetPlayerInput>, RefRO<InputsData>>().WithAll<GhostOwnerIsLocal>())
        {
            int held = 0;
            for (int i = 0; i < 4; i++)
                if (data.ValueRO.SkillHeld[i]) held |= 1 << i;
            input.ValueRW = new NetPlayerInput { Move = data.ValueRO.Move, Aim = data.ValueRO.MousePosition, Held = held };
        }
    }
}

/// <summary>
/// On the host, after Netcode has applied this tick's commands and before skills run, a remote player's command becomes
/// its InputsData and DesiredDirection. Presses are held skills that weren't held last tick, so a press is never lost or
/// doubled whatever the client's frame rate.
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(PredictedSimulationSystemGroup))]
[UpdateBefore(typeof(SkillSystemGroup))]
public partial struct NetInputReceiveSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (input, remote, data, direction, transform) in
                 SystemAPI.Query<RefRO<NetPlayerInput>, RefRW<RemotePlayer>, RefRW<InputsData>, RefRW<DesiredDirection>, RefRO<Unity.Transforms.LocalTransform>>())
        {
            var i = input.ValueRO;
            ref var d = ref data.ValueRW;
            d.Move = i.Move;
            d.MousePosition = i.Aim;
            for (int slot = 0; slot < 4; slot++)
            {
                bool held = (i.Held & (1 << slot)) != 0;
                d.SkillHeld[slot] = held;
                d.SkillPressedThisFrame[slot] = held && (remote.ValueRO.LastHeld & (1 << slot)) == 0;
            }
            remote.ValueRW.LastHeld = i.Held;

            var toAim = i.Aim - transform.ValueRO.Position;
            toAim.y = 0f;
            if (math.lengthsq(toAim) < 1e-6f) continue;
            direction.ValueRW.Direction = math.normalize(toAim);
            direction.ValueRW.Quaternion = quaternion.LookRotationSafe(direction.ValueRO.Direction, math.up());
        }
    }
}
