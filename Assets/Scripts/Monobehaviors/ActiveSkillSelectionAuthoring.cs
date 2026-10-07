using UnityEngine;
using Unity.Entities;

public class ActiveSkillSelectionAuthoring : MonoBehaviour
{
    public SkillType[] SkillArray;  
    public class Baker : Baker<ActiveSkillSelectionAuthoring>
    {
        public override void Bake(ActiveSkillSelectionAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            var skillBuffer = AddBuffer<Skill>(entity);
            for (int i = 0; i < authoring.SkillArray.Length; i++)
            {
                skillBuffer.Add(new Skill{Type = authoring.SkillArray[i]});
            }
        }
    }
}