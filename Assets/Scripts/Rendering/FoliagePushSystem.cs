using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Hands every player's position to the shaders (PL_Foliage.hlsl), so bending foliage (PL_Comic's Bend) leans out of
/// their way. Every machine sees every player, so the bushes move the same for all of them. Skills bend foliage through
/// the mist wake instead (VfxMist).
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.Presentation)]
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct FoliagePushSystem : ISystem, ISystemStartStop
{
    const int MaxPushers = 8;       // PL_MAX_PUSHERS
    const float Radius = 1.3f;      // Meters around a player that foliage leans away

    static readonly int PushersId = Shader.PropertyToID("_PL_Pushers"), CountId = Shader.PropertyToID("_PL_PusherCount");
    static readonly Vector4[] Pushers = new Vector4[MaxPushers];

    public void OnUpdate(ref SystemState state)
    {
        int count = 0;
        foreach (var transform in SystemAPI.Query<RefRO<LocalToWorld>>().WithAll<Player>())
        {
            if (count == MaxPushers) break;
            var p = transform.ValueRO.Position;
            Pushers[count++] = new Vector4(p.x, p.y, p.z, Radius);
        }
        Shader.SetGlobalVectorArray(PushersId, Pushers);
        Shader.SetGlobalInt(CountId, count);
    }

    // Global shader values outlive the world: nothing pushes once it's gone
    public void OnStartRunning(ref SystemState state) { }
    public void OnStopRunning(ref SystemState state) => Clear();
    public void OnDestroy(ref SystemState state) => Clear();

    static void Clear() => Shader.SetGlobalInt(CountId, 0);
}
