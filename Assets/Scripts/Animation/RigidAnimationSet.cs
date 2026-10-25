using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sampled keyframes of a rigid model (one mesh per bone, no skinning): every bone's local pose at a fixed rate, per clip.
/// Written by RigidModelBuilder from the model's FBX clips (its part meshes are sub-assets of this asset) and baked into
/// a blob by RigidAnimationAuthoring.
/// </summary>
public class RigidAnimationSet : ScriptableObject
{
    [Serializable]
    public class Clip
    {
        public RigidClip Kind;
        public float Length;
        public float SampleRate = 30f;
        public bool Loop;
        public int Frames;
        // Frame-major: frame * BoneCount + bone
        public List<Vector3> Positions = new List<Vector3>();
        public List<Quaternion> Rotations = new List<Quaternion>();
        public List<float> Scales = new List<float>();
    }

    [Tooltip("Bone paths relative to the model root, in the order RigidAnimationAuthoring.Bones uses")]
    public List<string> Bones = new List<string>();
    public List<Clip> Clips = new List<Clip>();
}
