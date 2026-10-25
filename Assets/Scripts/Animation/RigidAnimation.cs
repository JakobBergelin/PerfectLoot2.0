using Unity.Entities;
using Unity.Mathematics;

/// <summary>The clips a rigid model can play. Values index RigidAnimationBlob.Clips; keep them and add new ones at the end.</summary>
public enum RigidClip : byte
{
    Idle = 0,
    Move = 1,
    Attack = 2,
    Hit = 3,
    Death = 4,
}

public struct RigidAnimationBlob
{
    public int BoneCount;
    public BlobArray<RigidClipBlob> Clips;  // Indexed by RigidClip; Frames is 0 for clips the model doesn't have

    public bool Has(RigidClip clip) => (int)clip < Clips.Length && Clips[(int)clip].Frames > 0;
}

public struct RigidClipBlob
{
    public float Length;
    public float SampleRate;
    public bool Loop;
    public int Frames;
    public BlobArray<float3> Positions;
    public BlobArray<quaternion> Rotations;
    public BlobArray<float> Scales;
}

/// <summary>Plays RigidAnimationBlob clips on the RigidBone entities. RigidAnimationSystem picks the clip from gameplay state.</summary>
public struct RigidAnimator : IComponentData
{
    public BlobAssetReference<RigidAnimationBlob> Animation;
    public RigidClip Clip;
    public float Time;
    public RigidClip FromClip;      // Faded out over CrossFade seconds
    public float FromTime;
    public float Blend;             // 0 = FromClip, 1 = Clip
    public bool Started;
    // Last frame's values, to notice movement, attacks and hits
    public float3 LastPosition;
    public float LastAttackElapsed;
    public float LastFlashTimer;
}

/// <summary>The animated bones, in RigidAnimationSet.Bones order.</summary>
public struct RigidBone : IBufferElementData
{
    public Entity Value;
}

/// <summary>A dead unit playing its Death clip: no longer a target or a body, destroyed when Timer runs out.</summary>
public struct RigidDeath : IComponentData
{
    public float Timer;
}

