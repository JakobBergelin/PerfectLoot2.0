using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Put in the Entities subscene: bakes every listed item visual's part prefabs so EquipmentVisualSystem can attach them.
/// </summary>
public class EquipmentVisualsAuthoring : MonoBehaviour
{
    public List<EquipmentVisual> Visuals = new List<EquipmentVisual>();

    public class Baker : Baker<EquipmentVisualsAuthoring>
    {
        public override void Bake(EquipmentVisualsAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            var prefabs = AddBuffer<EquipmentPartPrefab>(entity);
            foreach (var visual in authoring.Visuals)
            {
                DependsOn(visual);
                if (visual == null || visual.Item == null) continue;
                foreach (var part in visual.Parts)
                {
                    if (part == null || part.Prefab == null || string.IsNullOrEmpty(part.Socket)) continue;
                    prefabs.Add(new EquipmentPartPrefab
                    {
                        Key = visual.Item.name,
                        Socket = part.Socket,
                        Prefab = GetEntity(part.Prefab, TransformUsageFlags.Dynamic),
                    });
                }
            }
        }
    }
}
