using Unity.Entities;
using UnityEngine;

/// <summary>
/// Passes the camera's Hidden Outline settings (CameraFollow) to PL_Comic's Player X-Ray passes as global shader values:
/// wherever anything stands between the camera and a player, the player shows as a colored outline. The renderer's
/// Player X-Ray features draw those passes for the Player layer only, and each camera tests its own depth, so it works
/// for every player and for split-screen.
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.Presentation)]
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct HiddenOutlineSystem : ISystem, ISystemStartStop
{
    static readonly int ColorId = Shader.PropertyToID("_PL_XRayColor");
    static readonly int WidthId = Shader.PropertyToID("_PL_XRayWidth");

    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton(out CameraFollow follow))
        {
            Clear();
            return;
        }
        Shader.SetGlobalVector(ColorId, follow.HiddenOutline);
        Shader.SetGlobalFloat(WidthId, follow.HiddenOutline.w);
    }

    // Global shader values outlive the world: no outline once it's gone
    public void OnStartRunning(ref SystemState state) { }
    public void OnStopRunning(ref SystemState state) => Clear();
    public void OnDestroy(ref SystemState state) => Clear();

    static void Clear() => Shader.SetGlobalFloat(WidthId, 0f);
}
