using UnityEditor;
using UnityEngine;

/// <summary>
/// Names the EnemySwarm layer and sets which layers it collides with. Swarm enemies only collide with the world,
/// not with each other, the player or physics enemies, which keeps big swarms cheap.
/// </summary>
public static class PhysicsLayerSetup
{
    const int SwarmLayer = 14;
    const string SwarmLayerName = "EnemySwarm";

    // Default, Ground, Wall, PlayerProjectile
    static readonly int[] SwarmCollidesWith = { 0, 6, 7, 10 };

    [MenuItem("Tools/Setup Physics Layers")]
    static void Setup()
    {
        NameLayer(SwarmLayer, SwarmLayerName);

        for (int layer = 0; layer < 32; layer++)
            Physics.IgnoreLayerCollision(SwarmLayer, layer, System.Array.IndexOf(SwarmCollidesWith, layer) < 0);

        AssetDatabase.SaveAssets();
        Debug.Log($"Layer {SwarmLayer} is now '{SwarmLayerName}' and collides with Default, Ground, Wall and PlayerProjectile only. " +
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
