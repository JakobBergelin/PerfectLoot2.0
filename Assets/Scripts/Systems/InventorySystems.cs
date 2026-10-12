using System;
using System.Linq;
using Unity.Burst;
using Unity.Entities;
using UnityEngine;

//[BurstCompile]
//partial class InventorySystem : SystemBase
//{
//    // TODO use midframe commandbuffer
//    private BeginInitializationEntityCommandBufferSystem _beginInitializationEntityCommandBufferSystem;
    
//    [BurstCompile]
//    protected override void OnCreate()
//    {
//        _beginInitializationEntityCommandBufferSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<BeginInitializationEntityCommandBufferSystem>();
//    }

//    private void ManageItem<T>(ItemType type, T component, DynamicBuffer<InventoryItem> buffer, EntityCommandBuffer commandBuffer, EntityManager entityManager, Entity entity) where T : unmanaged, IComponentData
//    {
//        InventoryItem foundItem = new InventoryItem{Type = ItemType.None};
//        foreach (var item in buffer)
//        {
//            if(item.Type == type)
//            {
//                foundItem = item;
//                break;
//            }
//        }

//        if(foundItem.Type != ItemType.None)
//        {
//            if(!entityManager.HasComponent<T>(entity))
//            {
//                commandBuffer.AddComponent<T>(entity, component);
//            }   
//        }
//        else
//        {
//            if(entityManager.HasComponent<T>(entity))
//            {
//                commandBuffer.RemoveComponent<T>(entity);
//            }
//        }
//    }

//    [BurstCompile]
//    protected override void OnUpdate()
//    {
//        // TODO check if inventory updated
//        var commandBuffer = _beginInitializationEntityCommandBufferSystem.CreateCommandBuffer();
//        foreach (var (itemBuffer, entity) in SystemAPI.Query<DynamicBuffer<InventoryItem>>().WithEntityAccess())
//        {
//            // TODO move item balancing
//            ManageItem(ItemType.Vanguard, new Vanguard{Health = UnityEngine.Random.Range(3,5), BlockAmount = 1}, itemBuffer, commandBuffer, EntityManager, entity);
//            ManageItem(ItemType.NormalBoots, new NormalBoots{}, itemBuffer, commandBuffer, EntityManager, entity);
//            ManageItem(ItemType.MagicBoots, new MagicBoots{}, itemBuffer, commandBuffer, EntityManager, entity);
//            ManageItem(ItemType.RareBoots, new RareBoots{}, itemBuffer, commandBuffer, EntityManager, entity);
//            ManageItem(ItemType.NormalSword, new NormalSword{}, itemBuffer, commandBuffer, EntityManager, entity);
//            ManageItem(ItemType.NormalDagger, new NormalDagger{}, itemBuffer, commandBuffer, EntityManager, entity);
//            ManageItem(ItemType.NormalCrossbow, new NormalCrossbow{}, itemBuffer, commandBuffer, EntityManager, entity);
//            ManageItem(ItemType.BrokenPromises, new BrokenPromises{}, itemBuffer, commandBuffer, EntityManager, entity);
//        }

//        //foreach (var (itemBuffer, magicBoots) in SystemAPI.Query<DynamicBuffer<InventoryItem>, RefRW<MagicBoots>>())
//        //{
//        //    Guid seed = new Guid();
//        //    foreach(var item in itemBuffer)
//        //    {
//        //        if(item.Type == ItemType.MagicBoots)
//        //        {
//        //            seed = item.Seed;
//        //            break;
//        //        }
//        //    }
//        //    UnityEngine.Random.InitState(seed.GetHashCode());
//        //    System.Random random = new System.Random(seed.GetHashCode());
//        //    var armorMax = Mathf.Lerp(Constants.Generate.Boots.MinArmor, Constants.Generate.Boots.MaxArmor, Constants.Generate.TierFactors.MagicMax);
//        //    var armorMin = Mathf.Lerp(Constants.Generate.Boots.MinArmor, Constants.Generate.Boots.MaxArmor, Constants.Generate.TierFactors.MagicMin);
//        //    var armorValue = UnityEngine.Random.Range(armorMin, armorMax);

//        //    magicBoots.ValueRW.Armor = (int)(armorValue + 0.5f);

//        //    var movementMax = Mathf.Lerp(Constants.Generate.Boots.MinMovementSpeed, Constants.Generate.Boots.MaxMovementSpeed, Constants.Generate.TierFactors.MagicMax);
//        //    var movementMin = Mathf.Lerp(Constants.Generate.Boots.MinMovementSpeed, Constants.Generate.Boots.MaxMovementSpeed, Constants.Generate.TierFactors.MagicMin);
//        //    magicBoots.ValueRW.MovementSpeed = UnityEngine.Random.Range(movementMin, movementMax);

//        //    magicBoots.ValueRW.Affix = BootsAffix.RandomizeAffix(random, Constants.Generate.TierFactors.MagicAffixMax, Constants.Generate.TierFactors.MagicAffixMin);
//        //}

//        //foreach (var (itemBuffer, rareBoots) in SystemAPI.Query<DynamicBuffer<InventoryItem>, RefRW<RareBoots>>())
//        //{
//        //    Guid seed = new Guid();
//        //    foreach(var item in itemBuffer)
//        //    {
//        //        if(item.Type == ItemType.RareBoots)
//        //        {
//        //            seed = item.Seed;
//        //            break;
//        //        }
//        //    }
//        //    UnityEngine.Random.InitState(seed.GetHashCode());
//        //    System.Random random = new System.Random(seed.GetHashCode());
//        //    var armorMax = Mathf.Lerp(Constants.Generate.Boots.MinArmor, Constants.Generate.Boots.MaxArmor, Constants.Generate.TierFactors.RareMax);
//        //    var armorMin = Mathf.Lerp(Constants.Generate.Boots.MinArmor, Constants.Generate.Boots.MaxArmor, Constants.Generate.TierFactors.RareMin);
//        //    var armorValue = UnityEngine.Random.Range(armorMin, armorMax);

//        //    rareBoots.ValueRW.Armor = (int)(armorValue + 0.5f);

//        //    var movementMax = Mathf.Lerp(Constants.Generate.Boots.MinMovementSpeed, Constants.Generate.Boots.MaxMovementSpeed, Constants.Generate.TierFactors.RareMax);
//        //    var movementMin = Mathf.Lerp(Constants.Generate.Boots.MinMovementSpeed, Constants.Generate.Boots.MaxMovementSpeed, Constants.Generate.TierFactors.RareMin);
//        //    rareBoots.ValueRW.MovementSpeed = UnityEngine.Random.Range(movementMin, movementMax);

//        //    rareBoots.ValueRW.Affix0 = BootsAffix.RandomizeAffix(random, Constants.Generate.TierFactors.RareAffixMax, Constants.Generate.TierFactors.RareAffixMin);
//        //    rareBoots.ValueRW.Affix1 = BootsAffix.RandomizeAffix(random, Constants.Generate.TierFactors.RareAffixMax, Constants.Generate.TierFactors.RareAffixMin);
//        //    rareBoots.ValueRW.Affix2 = BootsAffix.RandomizeAffix(random, Constants.Generate.TierFactors.RareAffixMax, Constants.Generate.TierFactors.RareAffixMin);
//        //}
        
//    }
//}