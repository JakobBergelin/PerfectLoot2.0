using UnityEngine;

/// <summary>
/// Couch co-op's rules (CoopManager pushes them to CoopRules every frame, so edits in Play mode apply live): how far
/// apart the players may get.
/// </summary>
[CreateAssetMenu(fileName = "CoopSettings", menuName = "Settings/CoopSettings")]
public class CoopSettings : ScriptableObject
{
    [Tooltip("Meters the players can be apart at most, along the ground")]
    [Min(2f)] public float MaxDistance = 20f;
    [Tooltip("Also never further apart than the camera fits at its widest (CameraSettings: Comfort Radius × Max Zoom Out " +
             "on each side), when that is less")]
    public bool KeepInCameraView = true;

    public void ApplyTo(ref CoopRules rules)
    {
        rules.MaxDistance = MaxDistance;
        rules.KeepInCameraView = KeepInCameraView;
    }
}
