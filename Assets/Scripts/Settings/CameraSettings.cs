using UnityEngine;

/// <summary>
/// The camera's framing, as an orbit around the player: it aims at a point LookHeight above the player's feet from
/// Distance away, tilted down Pitch and turned Yaw. Baked into CameraFollow by CameraFollowAuthoring. In the editor
/// CameraMonoBehavior also pushes these values to the camera every frame, so editing this asset in Play mode moves the
/// camera live, and the edits are kept after Play mode since they live in the asset.
/// </summary>
[CreateAssetMenu(fileName = "CameraSettings", menuName = "Settings/CameraSettings")]
public class CameraSettings : ScriptableObject
{
    [Tooltip("Degrees the camera tilts down; 90 looks straight down")]
    [Range(0f, 90f)] public float Pitch = 30f;
    [Tooltip("Degrees the camera turns around the player; 0 looks along +Z")]
    [Range(-180f, 180f)] public float Yaw = 0f;
    [Tooltip("Meters from the camera to the point it aims at")]
    [Min(0f)] public float Distance = 17.3205f;
    [Tooltip("Meters above the player's feet the camera aims at")]
    public float LookHeight = 1.3397f;
    [Tooltip("Vertical field of view in degrees (perspective camera)")]
    [Range(10f, 120f)] public float FieldOfView = 60f;

    [Header("Smoothing")]
    [Tooltip("Roughly the seconds the camera takes to catch up with the player")]
    public float SmoothTime = 0.15f;
    [Tooltip("Meters per second the camera can move at most; keep it above the player's fastest walk")]
    public float MaxSpeed = 30f;
    [Tooltip("When the player is further than this (meters) from where the camera is heading, it jumps there instead")]
    public float SnapDistance = 20f;

    [Header("Zoom")]
    [Tooltip("Meters around the camera's center (the middle of the players) that fit on screen without zooming out. " +
             "When a player or a Camera Focus (bosses) reaches further, the camera moves back")]
    [Min(1f)] public float ComfortRadius = 7f;
    [Tooltip("How far back the camera can move at most, as a multiple of Distance; 1 never zooms out")]
    [Range(1f, 2f)] public float MaxZoomOut = 1.35f;
    [Tooltip("A Camera Focus (bosses) only counts while a player is within this many meters of it")]
    [Min(0f)] public float FocusRange = 20f;
    [Tooltip("Roughly the seconds the camera takes to zoom out")]
    [Min(0.01f)] public float ZoomOutTime = 0.4f;
    [Tooltip("Roughly the seconds the camera takes to zoom back in")]
    [Min(0.01f)] public float ZoomInTime = 1.2f;
    [Tooltip("Seconds the camera waits before zooming back in, so it doesn't pump in and out")]
    [Min(0f)] public float ZoomInDelay = 1f;

    [Header("Shake")]
    [Tooltip("Meters the camera moves at most when a skill with Crit Shake 1 crits; 0 turns shaking off")]
    [Min(0f)] public float ShakeStrength = 1f;
    [Tooltip("How fast a shake dies out: the share of a full shake lost per second")]
    [Min(0.01f)] public float ShakeDecay = 2.5f;
    [Tooltip("How fast the camera wobbles while shaking")]
    [Min(0f)] public float ShakeFrequency = 18f;

    public Quaternion Rotation => Quaternion.Euler(Pitch, Yaw, 0f);
    public Vector3 Offset => Vector3.up * LookHeight - Rotation * Vector3.forward * Distance;

    /// <summary>Writes the framing, smoothing, zoom and shake into the component; leaves its running state (Velocity, HasTarget, Zoom, Trauma) alone.</summary>
    public void ApplyTo(ref CameraFollow follow)
    {
        follow.Offset = Offset;
        follow.LookHeight = LookHeight;
        follow.ComfortRadius = ComfortRadius;
        follow.MaxZoomOut = MaxZoomOut;
        follow.FocusRange = FocusRange;
        follow.ZoomOutTime = ZoomOutTime;
        follow.ZoomInTime = ZoomInTime;
        follow.ZoomInDelay = ZoomInDelay;
        follow.Rotation = Rotation;
        follow.SmoothTime = SmoothTime;
        follow.MaxSpeed = MaxSpeed;
        follow.SnapDistance = SnapDistance;
        follow.ShakeStrength = ShakeStrength;
        follow.ShakeDecay = ShakeDecay;
        follow.ShakeFrequency = ShakeFrequency;
    }
}
