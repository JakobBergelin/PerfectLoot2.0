using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class CameraFollowAuthoring : MonoBehaviour
{
    [Tooltip("Framing and smoothing; edit this asset (also live in Play mode) to tune the camera")]
    public CameraSettings Settings;

    class Baker : Baker<CameraFollowAuthoring>
    {
        public override void Bake(CameraFollowAuthoring authoring)
        {
            DependsOn(authoring.Settings);
            if (authoring.Settings == null)
            {
                Debug.LogError("CameraFollowAuthoring has no CameraSettings assigned; the camera won't follow the player.", authoring);
                return;
            }

            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            var follow = new CameraFollow();
            authoring.Settings.ApplyTo(ref follow);
            AddComponent(entity, follow);
        }
    }
}

public struct CameraFollow : IComponentData
{
    public float3 Offset;
    public float LookHeight;        // The part of Offset that is the aim point's height; zooming moves the camera back from it
    public Quaternion Rotation;
    public float SmoothTime;
    public float MaxSpeed;
    public float SnapDistance;
    public float ComfortRadius;     // Meters around the center that fit without zooming out
    public float MaxZoomOut;        // Offset multiple at most
    public float FocusRange;        // A CameraFocus counts while a player is this close to it
    public float ZoomOutTime;
    public float ZoomInTime;
    public float ZoomInDelay;
    public float ShakeStrength;     // Meters the camera moves at most, at full trauma
    public float ShakeDecay;        // Trauma lost per second
    public float ShakeFrequency;    // How fast the shake wobbles
    public float4 HiddenOutline;    // Linear color and width in pixels of a hidden player's outline (HiddenOutlineSystem)
    public float PlayerHitShare;    // A hit on a player shakes when it takes at least this share of their max health
    public float PlayerHitShake;    // Trauma of such a hit
    public float BigHitShake;       // A crit whose Crit Shake is at least this also pulses
    public float BigHitPulse;       // Pulse of a big crit
    public float FocusIntroShake;   // Trauma when a CameraFocus (a boss) first comes within FocusRange of a player; it also pulses fully
    public float PulseTime;         // Seconds a full pulse takes to fade

    public float3 Velocity;     // SmoothDamp's running velocity
    public float Zoom;          // How far back the camera is now, as an Offset multiple (1 to MaxZoomOut; 0 until set)
    public float ZoomVelocity;  // SmoothDamp's running zoom speed
    public float ZoomInWait;    // Seconds left before it may zoom back in
    public bool HasTarget;      // False until a player is found (and while there is none), so the camera snaps to it first
    public float Trauma;        // 0 to 1, raised by shakes (HitShakeSystem, impacts in VfxSystem), wears off at ShakeDecay
    public float3 ShakeOffset;  // This frame's shake, added on top of the followed position by CameraMonoBehavior
    public float Pulse;         // 0 to 1, raised by big crits and boss intros, fades over PulseTime: the hit pulse volume's weight (CameraMonoBehavior)
}

/// <summary>A CameraFocus whose intro has played (FocusIntroSystem): it only shakes and pulses the first time.</summary>
public struct FocusIntroPlayed : IComponentData
{
}

/// <summary>
/// Something the camera zooms out to keep on screen while a player is near it (CameraFollow.FocusRange): dungeon bosses,
/// and enemies with Camera Focus ticked. Radius is how far it reaches from its position (its body).
/// </summary>
public struct CameraFocus : IComponentData
{
    public float Radius;
}
