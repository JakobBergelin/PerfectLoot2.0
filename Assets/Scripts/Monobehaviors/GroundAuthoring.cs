using Unity.Entities;
using UnityEngine;

public class GroundAuthoring : MonoBehaviour
{
    public class Baker : Baker<GroundAuthoring>
    {
        public override void Bake(GroundAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Ground());
        }
    }
}