using System;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// On a rigid model's visual prefab root (next to the collider): bakes its RigidAnimationSet and links the bones.
/// Bones are the model's transforms in RigidAnimationSet.Bones order (RigidModelBuilder fills both).
/// </summary>
public class RigidAnimationAuthoring : MonoBehaviour
{
    public RigidAnimationSet Animation;
    public Transform[] Bones = Array.Empty<Transform>();

    public class Baker : Baker<RigidAnimationAuthoring>
    {
        public override void Bake(RigidAnimationAuthoring authoring)
        {
            DependsOn(authoring.Animation);
            if (authoring.Animation == null) return;
            if (authoring.Bones.Length != authoring.Animation.Bones.Count)
            {
                Debug.LogWarning($"{authoring.name}: {authoring.Bones.Length} bones but its animation has {authoring.Animation.Bones.Count}. Rebuild it with RigidModelBuilder.", authoring);
                return;
            }

            var entity = GetEntity(TransformUsageFlags.Dynamic);
            var blob = BuildBlob(authoring.Animation);
            AddBlobAsset(ref blob, out _);
            AddComponent(entity, new RigidAnimator { Animation = blob, Clip = RigidClip.Idle, FromClip = RigidClip.Idle, Blend = 1f });

            var bones = AddBuffer<RigidBone>(entity);
            foreach (var bone in authoring.Bones)
                bones.Add(new RigidBone { Value = bone != null ? GetEntity(bone, TransformUsageFlags.Dynamic) : Entity.Null });
        }

        static BlobAssetReference<RigidAnimationBlob> BuildBlob(RigidAnimationSet set)
        {
            using var builder = new BlobBuilder(Unity.Collections.Allocator.Temp);
            ref var root = ref builder.ConstructRoot<RigidAnimationBlob>();
            root.BoneCount = set.Bones.Count;
            int kinds = Enum.GetValues(typeof(RigidClip)).Length;
            var clips = builder.Allocate(ref root.Clips, kinds);
            foreach (var clip in set.Clips)
            {
                int samples = clip.Frames * set.Bones.Count;
                if (clip.Frames <= 0 || clip.Positions.Count != samples || clip.Rotations.Count != samples || clip.Scales.Count != samples) continue;
                ref var target = ref clips[(int)clip.Kind];
                target.Length = clip.Length;
                target.SampleRate = clip.SampleRate;
                target.Loop = clip.Loop;
                target.Frames = clip.Frames;
                var positions = builder.Allocate(ref target.Positions, samples);
                var rotations = builder.Allocate(ref target.Rotations, samples);
                var scales = builder.Allocate(ref target.Scales, samples);
                for (int i = 0; i < samples; i++)
                {
                    positions[i] = clip.Positions[i];
                    rotations[i] = clip.Rotations[i];
                    scales[i] = clip.Scales[i];
                }
            }
            return builder.CreateBlobAssetReference<RigidAnimationBlob>(Unity.Collections.Allocator.Persistent);
        }
    }
}
