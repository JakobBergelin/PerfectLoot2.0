using Unity.Entities;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// On a ghost prefab: bakes the gameplay components clients must see, because Netcode only replicates components the
/// prefab already has. Every other gameplay component is still added at runtime on the host only (PlayerEntitySetup,
/// EnemyEntitySetup), and is never sent.
/// </summary>
public class NetReplicatedAuthoring : MonoBehaviour
{
    public bool Health = true;
    public bool Resource = true;
    [Tooltip("A player: which local player it is on its owner's machine (NetPlayer), the input command its owner sends and its predicted walking (PredictedMove)")]
    [FormerlySerializedAs("PlayerInput")]
    public bool Player;
    [Tooltip("An enemy: which EnemyDefinition the host made it from (NetUnitSource), so other machines can show its name, passives and skills")]
    public bool Unit;

    class Baker : Baker<NetReplicatedAuthoring>
    {
        public override void Bake(NetReplicatedAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            if (authoring.Health) AddComponent(entity, new Health());
            if (authoring.Resource) AddComponent(entity, new Resource());
            if (authoring.Player)
            {
                AddComponent(entity, new NetPlayer());
                AddComponent(entity, new NetPlayerInput());
                AddComponent(entity, new PredictedMove());
            }
            if (authoring.Unit) AddComponent(entity, new NetUnitSource());
        }
    }
}
