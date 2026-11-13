using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Writes keyframed clips straight into a RigidAnimationSet, for models and clips made in code rather than in Blender
/// (player skill clips, companion models). Keys are offsets from each bone's rest pose at 24 fps, like pl_rig.clip:
/// a rotation (Euler degrees in the bone's own local axes, applied after its rest rotation), a position offset (in the
/// parent's space, meters) and a scale. A bone without keys stays at rest; one with keys gets rest keys at frame 0 and,
/// for loops, at the end, unless given. Keys ease in and out (smoothstep), sampled at 30 Hz like RigidModelBuilder.
/// </summary>
public static class RigidClipWriter
{
    public const float KeyRate = 24f;
    const float SampleRate = 30f;

    public struct Key
    {
        public int Frame;
        public Vector3 Euler;
        public Vector3 Offset;
        public float Scale;

        public Key(int frame, Vector3 euler, Vector3 offset = default, float scale = 1f)
        {
            Frame = frame; Euler = euler; Offset = offset; Scale = scale;
        }
    }

    public struct Rest
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public float Scale;
    }

    /// <summary>Rest poses of a set's bones, from frame 0 of one of its clips (Idle is rest at frame 0 in every model so far).</summary>
    public static Rest[] RestFrom(RigidAnimationSet set, RigidClip clip = RigidClip.Idle)
    {
        var source = set.Clips.First(c => c.Kind == clip);
        int count = set.Bones.Count;
        var rest = new Rest[count];
        for (int b = 0; b < count; b++)
            rest[b] = new Rest { Position = source.Positions[b], Rotation = source.Rotations[b], Scale = source.Scales[b] };
        return rest;
    }

    /// <summary>A clip of length frames (at KeyRate); keys by bone index.</summary>
    public static RigidAnimationSet.Clip Make(RigidClip kind, int length, bool loop, Rest[] rest, Dictionary<int, Key[]> keys)
    {
        float seconds = length / KeyRate;
        int frames = Mathf.Max(1, Mathf.CeilToInt(seconds * SampleRate) + 1);
        var clip = new RigidAnimationSet.Clip { Kind = kind, Length = seconds, SampleRate = SampleRate, Loop = loop, Frames = frames };

        var tracks = new Key[rest.Length][];
        for (int b = 0; b < rest.Length; b++)
        {
            if (!keys.TryGetValue(b, out var given) || given.Length == 0) { tracks[b] = null; continue; }
            var list = given.OrderBy(k => k.Frame).ToList();
            if (list[0].Frame != 0) list.Insert(0, new Key(0, Vector3.zero));
            if (loop && list[list.Count - 1].Frame != length) list.Add(new Key(length, Vector3.zero));
            tracks[b] = list.ToArray();
        }

        for (int f = 0; f < frames; f++)
        {
            float keyFrame = Mathf.Min(f / SampleRate, seconds) * KeyRate;
            for (int b = 0; b < rest.Length; b++)
            {
                var (euler, offset, scale) = tracks[b] == null ? (Vector3.zero, Vector3.zero, 1f) : Evaluate(tracks[b], keyFrame);
                clip.Positions.Add(rest[b].Position + offset);
                clip.Rotations.Add(rest[b].Rotation * Quaternion.Euler(euler));
                clip.Scales.Add(rest[b].Scale * scale);
            }
        }
        return clip;
    }

    static (Vector3, Vector3, float) Evaluate(Key[] keys, float frame)
    {
        if (frame <= keys[0].Frame) return (keys[0].Euler, keys[0].Offset, keys[0].Scale);
        for (int i = 0; i + 1 < keys.Length; i++)
        {
            var a = keys[i]; var b = keys[i + 1];
            if (frame > b.Frame) continue;
            float t = Mathf.SmoothStep(0f, 1f, (frame - a.Frame) / Mathf.Max(1f, b.Frame - a.Frame));
            return (Vector3.Lerp(a.Euler, b.Euler, t), Vector3.Lerp(a.Offset, b.Offset, t), Mathf.Lerp(a.Scale, b.Scale, t));
        }
        var last = keys[keys.Length - 1];
        return (last.Euler, last.Offset, last.Scale);
    }

    /// <summary>Puts a clip in the set, replacing the one of the same kind; clips stay ordered by kind.</summary>
    public static void Put(RigidAnimationSet set, RigidAnimationSet.Clip clip)
    {
        set.Clips.RemoveAll(c => c.Kind == clip.Kind);
        set.Clips.Add(clip);
        set.Clips.Sort((x, y) => x.Kind.CompareTo(y.Kind));
    }
}
