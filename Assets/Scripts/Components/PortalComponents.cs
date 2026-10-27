using Unity.Collections;
using Unity.Entities;

/// <summary>
/// A way to another scene (PortalAuthoring). While the player stands within Range, PortalHUD shows an Enter button that
/// loads Scene with the current character (GameSceneLoader).
/// </summary>
public struct Portal : IComponentData
{
    public FixedString64Bytes Scene;        // Scene name, as in Build Settings
    public FixedString64Bytes DisplayName;  // Shown above the Enter button
    public float Range;                     // Meters, measured along the ground
}
