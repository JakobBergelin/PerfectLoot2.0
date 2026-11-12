using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

/// <summary>
/// A stand-in ghost for an effect that follows something only the host has (a projectile, a tornado, a soul): the host's
/// VfxSystem makes one per such effect, moves it along and destroys it with its entity. Other machines' VfxSystem plays the
/// same effect on it, and its impact where it goes away.
/// </summary>
public struct NetVfx : IComponentData
{
    [GhostField] public VfxType Type;
    [GhostField] public VfxType Impact;
    /// <summary>Above 0: the effect is sized sideways to it (a field's radius).</summary>
    [GhostField(Quantization = 100)] public float Scale;
    [GhostField] public bool Tinted;
    /// <summary>Tinted: its colour is this damage type's (VfxSystem.StrikeColor).</summary>
    [GhostField] public DamageType Tint;
}

/// <summary>On the NetVfx ghost prefab (Prefabs/Net/NetVfx).</summary>
public class NetVfxAuthoring : MonoBehaviour
{
    class Baker : Baker<NetVfxAuthoring>
    {
        public override void Bake(NetVfxAuthoring authoring) => AddComponent(GetEntity(TransformUsageFlags.Dynamic), new NetVfx());
    }
}
