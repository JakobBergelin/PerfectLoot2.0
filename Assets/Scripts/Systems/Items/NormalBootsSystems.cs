//using System;
//using Unity.Burst;
//using Unity.Entities;
//using UnityEngine;

//[BurstCompile]
//[UpdateInGroup(typeof(CalculateStatsSystemGroup))]
//public partial struct CalculateNormalBootsSystem : ISystem
//{
//    [BurstCompile]
//    public void OnUpdate(ref SystemState state)
//    {
//        foreach (var (normalBoots, armor, movementSpeed) in SystemAPI.Query<RefRO<NormalBoots>, RefRW<Armor>, RefRW<MovementSpeed>>())
//        {
//            armor.ValueRW.Value += normalBoots.ValueRO.Armor;
//            movementSpeed.ValueRW.Value += normalBoots.ValueRO.MovementSpeed;
//        }
//    }
//}

//[BurstCompile]
//[UpdateAfter(typeof(InventorySystem))]
//public partial struct RandomizeNormalBootsSystem : ISystem
//{
//    [BurstCompile]
//    public void OnUpdate(ref SystemState state)
//    {
//        foreach (var (itemBuffer, normalBoots) in SystemAPI.Query<DynamicBuffer<InventoryItem>, RefRW<NormalBoots>>())
//        {
//            Guid seed = new Guid();
//            foreach(var item in itemBuffer)
//            {
//                if(item.Type == ItemType.NormalBoots)
//                {
//                    seed = item.Seed;
//                    break;
//                }
//            }
//            UnityEngine.Random.InitState(seed.GetHashCode());
//            var armorMax = Mathf.Lerp(Constants.Generate.Boots.MinArmor, Constants.Generate.Boots.MaxArmor, Constants.Generate.TierFactors.NormalMax);
//            var armorMin = Mathf.Lerp(Constants.Generate.Boots.MinArmor, Constants.Generate.Boots.MaxArmor, Constants.Generate.TierFactors.NormalMin);
//            var armorValue = UnityEngine.Random.Range(armorMin, armorMax);

//            normalBoots.ValueRW.Armor = (int)(armorValue + 0.5f);

//            var movementMax = Mathf.Lerp(Constants.Generate.Boots.MinMovementSpeed, Constants.Generate.Boots.MaxMovementSpeed, Constants.Generate.TierFactors.NormalMax);
//            var movementMin = Mathf.Lerp(Constants.Generate.Boots.MinMovementSpeed, Constants.Generate.Boots.MaxMovementSpeed, Constants.Generate.TierFactors.NormalMin);
//            normalBoots.ValueRW.MovementSpeed = UnityEngine.Random.Range(movementMin, movementMax);
//        }
//    }
//}
