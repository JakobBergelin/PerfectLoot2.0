using UnityEngine;
using Unity.Entities;
using System;

public class PassivesAuthoring : MonoBehaviour
{
    public class Baker : Baker<PassivesAuthoring>
    {
        public override void Bake(PassivesAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddBuffer<Passive>(entity);
            AddComponent<UnitResource>(entity);
            AddComponent<UnitClass>(entity); 
            AddComponent<Level>(entity);
        }
    }
}