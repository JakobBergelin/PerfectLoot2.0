using Unity.Entities;
using UnityEngine;

public class PeriodicAttackAuthoring : MonoBehaviour
{
    public float DamagePerSecond;

    public DamageType Type;
    public float Radius;
    public float TimeRemaining;

    public class Baker : Baker<PeriodicAttackAuthoring>
    {
        public override void Bake(PeriodicAttackAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new PeriodicAttack
            {
                DamagePerSecond = authoring.DamagePerSecond,
                Type = authoring.Type,
                Radius = authoring.Radius,
                TimeRemaining = authoring.TimeRemaining
            });
        }
    }
}

