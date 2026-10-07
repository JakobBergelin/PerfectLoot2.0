using Unity.Entities;
using UnityEngine;

public class AttackAuthoring : MonoBehaviour
{
    public float Damage;

    public DamageType Type;
    public float Radius;

    public class Baker : Baker<AttackAuthoring>
    {
        public override void Bake(AttackAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Attack
            {
                Damage = authoring.Damage,
                Type = authoring.Type,
                Radius = authoring.Radius,
            });
        }
    }
}
