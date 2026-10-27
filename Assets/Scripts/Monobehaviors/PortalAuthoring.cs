using System.Linq;
using System.Text.RegularExpressions;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Bakes a Portal to another scene. Works hand-placed in a subscene and as a Prop in a LevelPlacementSet (the prefab
/// from Tools → Create Portal Prefabs).
/// </summary>
public class PortalAuthoring : MonoBehaviour
{
#if UNITY_EDITOR
    [Tooltip("The scene Enter loads with the current character. Must be in Build Settings.")]
    public UnityEditor.SceneAsset Scene;
#endif
    [Tooltip("Shown above the Enter button. Empty: the scene's name with spaces (HauntedCathedral → Haunted Cathedral).")]
    public string DisplayName;
    [Tooltip("Meters (along the ground) within which the player gets the Enter button")]
    public float Range = 3f;
    [Tooltip("Scaled to Range, so the ring on the ground shows where Enter appears. Its mesh has a radius of 1.")]
    public Transform RangeRing;

#if UNITY_EDITOR
    void OnValidate()
    {
        Range = Mathf.Max(0.1f, Range);
        // Not during OnValidate itself: changing a transform there warns (SendMessage)
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null && RangeRing != null && RangeRing.localScale != new Vector3(Range, 1f, Range))
                RangeRing.localScale = new Vector3(Range, 1f, Range);
        };
    }

    public class Baker : Baker<PortalAuthoring>
    {
        public override void Bake(PortalAuthoring authoring)
        {
            DependsOn(authoring.Scene);
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            string sceneName = authoring.Scene != null ? authoring.Scene.name : "";
            if (string.IsNullOrEmpty(sceneName))
                Debug.LogWarning($"Portal {authoring.name} has no scene, so it can't be entered.", authoring);
            else if (!UnityEditor.EditorBuildSettings.scenes.Any(scene => scene.enabled && scene.path == UnityEditor.AssetDatabase.GetAssetPath(authoring.Scene)))
                Debug.LogWarning($"Portal {authoring.name} leads to {sceneName}, which isn't an enabled scene in Build Settings.", authoring);

            string displayName = string.IsNullOrWhiteSpace(authoring.DisplayName)
                ? Regex.Replace(sceneName, "(\\B[A-Z])", " $1")
                : authoring.DisplayName.Trim();

            var portal = new Portal { Range = authoring.Range };
            portal.Scene.CopyFromTruncated(sceneName);
            portal.DisplayName.CopyFromTruncated(displayName);
            if (portal.Scene.ToString() != sceneName)
                Debug.LogWarning($"Portal {authoring.name}: the scene name {sceneName} is too long for a Portal.", authoring);
            AddComponent(entity, portal);
        }
    }
#endif
}
