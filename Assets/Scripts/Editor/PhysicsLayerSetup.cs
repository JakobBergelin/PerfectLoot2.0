using UnityEditor;
using UnityEngine;

/// <summary>
/// Names the EnemySwarm layer and sets which layers it collides with. Swarm enemies only collide with the world,
/// not with each other, the player or physics enemies, which keeps big swarms cheap. Also makes the Water layer
/// (generated levels) block units but not attacks or projectiles, so you can shoot across water.
/// </summary>
public static class PhysicsLayerSetup
{
    const int SwarmLayer = 14;
    const string SwarmLayerName = "EnemySwarm";
    const int WaterLayer = 4;   // Unity's built-in Water layer

    // Default, Water, Ground, Wall, PlayerProjectile
    static readonly int[] SwarmCollidesWith = { 0, WaterLayer, 6, 7, 10 };

    // PlayerProjectile, EnemyProjectile, PlayerAttack, EnemyAttack
    static readonly int[] WaterIgnores = { 10, 11, 12, 13 };

    [MenuItem("Tools/Setup Physics Layers")]
    static void Setup()
    {
        NameLayer(SwarmLayer, SwarmLayerName);

        for (int layer = 0; layer < 32; layer++)
            Physics.IgnoreLayerCollision(SwarmLayer, layer, System.Array.IndexOf(SwarmCollidesWith, layer) < 0);

        foreach (var layer in WaterIgnores)
            Physics.IgnoreLayerCollision(WaterLayer, layer, true);

        AssetDatabase.SaveAssets();
        Debug.Log($"Layer {SwarmLayer} is now '{SwarmLayerName}' and collides with Default, Water, Ground, Wall and PlayerProjectile only. " +
                  "Water no longer collides with projectiles or attacks. " +
                  "Colliders bake their collision filter, so reimport the Entities subscene (or clear the entity cache) to apply it.");
    }

    static void NameLayer(int layer, string name)
    {
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("layers");
        var current = layers.GetArrayElementAtIndex(layer);
        if (!string.IsNullOrEmpty(current.stringValue) && current.stringValue != name)
            Debug.LogWarning($"Layer {layer} was named '{current.stringValue}', renaming it to '{name}'.");
        current.stringValue = name;
        tagManager.ApplyModifiedProperties();
    }
}
