using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The player's skill clips made in code (Blender isn't needed): Slash, Slam, Throw, Thrust, Nova, Summon, Bow (once) and
/// Channel, Guard, Tuck (loops), written into the player rig with RigidClipWriter. EquipmentSetup writes them again after
/// rebuilding the rig from the FBX.
///
/// Keys are in pl_player_equipment.py's notation, to compare with its skill_clips: K(frame at 24 fps, (pitch, yaw, roll))
/// or K(frame, rotation, (x, y, z) location offset), Blender pose angles on pl_rig's bones. Arm angles: pitch &lt; 0 swings
/// the arm forward (-90 level, -180 straight up), roll &gt; 0 swings it toward +X (out for arm_L, in for arm_R; for an arm
/// raised past level it's the other way), yaw &gt; 0 turns left (for a level arm, swings it left). Pitch &gt; 0 leans the chest
/// or head forward and swings a leg back. Locations: +x left, -y forward, z up. Bones without keys stay at rest.
/// hand_L / hand_R carry the weapon and off-hand; they only turn where the weapon has to: blade flat for Slash, in line
/// with the arm for Slam, Thrust and Bow, shield facing forward for Guard.
/// </summary>
public static class PlayerSkillClips
{
    const string RigPath = "Assets/Prefabs/Player/PL_Base_Visuals_Rig.asset";

    [MenuItem("Tools/Animation/Write Player Skill Clips")]
    public static void Write()
    {
        var set = AssetDatabase.LoadAssetAtPath<RigidAnimationSet>(RigPath);
        if (set == null)
        {
            Debug.LogError($"No player rig at {RigPath}: build it first (Tools/Equipment/Build Player Equipment).");
            return;
        }
        var rest = RigidClipWriter.RestFrom(set);
        CheckAxes(set, rest);

        float worst = 0f;
        foreach (var clip in Clips())
        {
            var keys = new Dictionary<int, RigidClipWriter.Key[]>();
            foreach (var bone in clip)
            {
                keys[Bone(set, bone.Key)] = bone.Value.Select(ToUnity).ToArray();
                foreach (var key in bone.Value)
                    worst = Mathf.Max(worst, Quaternion.Angle(Quaternion.Euler(UnityEuler(key.Rotation)), Rotation(key.Rotation)));
            }
            RigidClipWriter.Put(set, RigidClipWriter.Make(clip.Kind, clip.Length, clip.Loop, rest, keys));
        }
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        Debug.Log($"Player skill clips written to {RigPath} (keys converted within {worst:0.###} deg).");
    }

    // ---------- clips ----------

    static Clip[] Clips() => new[]
    {
        new Clip(RigidClip.Slash, 12)       // wide right-to-left swing, blade flat and leading, the torso turning with it
        {
            ["chest"] = new[] { K(0, (4, -40, 0)), K(2, (4, -45, 0)), K(5, (8, 45, 0)), K(12, (0, 0, 0)) },
            ["head"] = new[] { K(0, (-4, 25, 0)), K(5, (-6, -25, 0)), K(12, (0, 0, 0)) },
            ["hips"] = new[] { K(0, (0, -10, 0)), K(5, (0, 12, 0), (0, -0.04f, -0.02f)), K(12, (0, 0, 0)) },
            ["arm_R"] = new[] { K(0, (-85, -60, 0)), K(2, (-88, -65, 0)), K(5, (-92, 45, 0)), K(8, (-70, 40, 0)), K(12, (-15, 0, 0)) },
            ["hand_R"] = new[] { K(0, (0, 90, 0)), K(7, (0, 90, 0)), K(12, (0, 0, 0)) },
            ["arm_L"] = new[] { K(0, (-35, 0, 30)), K(5, (15, 0, 35)), K(12, (0, 0, 0)) },
        },
        new Clip(RigidClip.Slam, 13)        // both arms high overhead, then down in front with the body dropping into it
        {
            ["chest"] = new[] { K(0, (-14, 0, 0)), K(2, (-16, 0, 0)), K(4, (32, 0, 0)), K(7, (30, 0, 0)), K(13, (0, 0, 0)) },
            ["head"] = new[] { K(0, (-12, 0, 0)), K(4, (-20, 0, 0)), K(13, (0, 0, 0)) },
            ["hips"] = new[] { K(0, (0, 0, 0), (0, 0.03f, 0.04f)), K(2, (0, 0, 0), (0, 0.03f, 0.05f)), K(4, (0, 0, 0), (0, -0.06f, -0.1f)),
                               K(7, (0, 0, 0), (0, -0.06f, -0.1f)), K(13, (0, 0, 0)) },
            ["arm_R"] = new[] { K(0, (-165, 0, -14)), K(2, (-175, 0, -16)), K(4, (-92, 14, 0)), K(7, (-88, 14, 0)), K(13, (0, 0, 0)) },
            ["hand_R"] = new[] { K(0, (90, 0, 0)), K(8, (90, 0, 0)), K(13, (0, 0, 0)) },   // weapon in line: straight up, then into the ground
            ["arm_L"] = new[] { K(0, (-165, 0, 14)), K(2, (-175, 0, 16)), K(4, (-92, -14, 0)), K(7, (-88, -14, 0)), K(13, (0, 0, 0)) },
            ["leg_L"] = new[] { K(0, (0, 0, 0)), K(4, (-28, 0, 6)), K(7, (-28, 0, 6)), K(13, (0, 0, 0)) },
            ["leg_R"] = new[] { K(0, (0, 0, 0)), K(4, (22, 0, -6)), K(7, (22, 0, -6)), K(13, (0, 0, 0)) },
        },
        new Clip(RigidClip.Throw, 12)       // over-arm throw from behind the head, left arm aiming, left foot forward
        {
            ["chest"] = new[] { K(0, (-10, 30, 0)), K(1, (-12, 34, 0)), K(3, (18, -28, 0)), K(6, (12, -20, 0)), K(12, (0, 0, 0)) },
            ["head"] = new[] { K(0, (-4, -20, 0)), K(3, (-12, 18, 0)), K(12, (0, 0, 0)) },
            ["hips"] = new[] { K(0, (0, 10, 0), (0, 0.05f, 0)), K(3, (0, -10, 0), (0, -0.08f, -0.03f)), K(12, (0, 0, 0)) },
            ["arm_R"] = new[] { K(0, (-195, 0, -10)), K(1, (-205, 0, -12)), K(3, (-70, 0, 6)), K(6, (-35, 0, 4)), K(12, (0, 0, 0)) },
            ["arm_L"] = new[] { K(0, (-85, -20, 0)), K(3, (-10, 0, 25)), K(12, (0, 0, 0)) },
            ["leg_L"] = new[] { K(0, (-20, 0, 0)), K(6, (-22, 0, 0)), K(12, (0, 0, 0)) },
            ["leg_R"] = new[] { K(0, (15, 0, 0)), K(6, (16, 0, 0)), K(12, (0, 0, 0)) },
        },
        new Clip(RigidClip.Thrust, 10)      // lunge: weapon in line with the arm, driven forward by the shoulder and a long step
        {
            ["hips"] = new[] { K(0, (0, 0, 0), (0, 0.1f, 0)), K(1, (0, 0, 0), (0, 0.1f, 0)), K(3, (0, 0, 0), (0, -0.22f, -0.1f)),
                               K(5, (0, 0, 0), (0, -0.22f, -0.1f)), K(10, (0, 0, 0)) },
            ["chest"] = new[] { K(0, (-4, 25, 0)), K(1, (-4, 28, 0)), K(3, (20, -20, 0)), K(5, (20, -20, 0)), K(10, (0, 0, 0)) },
            ["head"] = new[] { K(0, (0, -20, 0)), K(3, (-18, 15, 0)), K(10, (0, 0, 0)) },
            ["arm_R"] = new[] { K(0, (-70, -20, 0)), K(1, (-72, -22, 0)), K(3, (-100, 18, 0)), K(5, (-98, 18, 0)), K(10, (0, 0, 0)) },
            ["hand_R"] = new[] { K(0, (90, 0, 0)), K(6, (90, 0, 0)), K(10, (0, 0, 0)) },
            ["arm_L"] = new[] { K(0, (-20, 0, 15)), K(3, (30, 0, 20)), K(10, (0, 0, 0)) },
            ["leg_L"] = new[] { K(0, (0, 0, 0)), K(3, (-38, 0, 0)), K(5, (-38, 0, 0)), K(10, (0, 0, 0)) },
            ["leg_R"] = new[] { K(0, (0, 0, 0)), K(3, (28, 0, 0)), K(5, (28, 0, 0)), K(10, (0, 0, 0)) },
        },
        new Clip(RigidClip.Nova, 12)        // gathered with arms crossed, then flung out and up, chest open
        {
            ["hips"] = new[] { K(0, (0, 0, 0), (0, 0, -0.04f)), K(3, (0, 0, 0), (0, 0, 0.04f)), K(6, (0, 0, 0), (0, 0, 0.03f)), K(12, (0, 0, 0)) },
            ["chest"] = new[] { K(0, (18, 0, 0)), K(3, (-12, 0, 0)), K(6, (-10, 0, 0)), K(12, (0, 0, 0)) },
            ["head"] = new[] { K(0, (12, 0, 0)), K(3, (-16, 0, 0)), K(12, (0, 0, 0)) },
            ["arm_L"] = new[] { K(0, (-55, -20, -10)), K(3, (-20, 0, 115)), K(6, (-18, 0, 120)), K(12, (0, 0, 0)) },
            ["arm_R"] = new[] { K(0, (-55, 20, 10)), K(3, (-20, 0, -115)), K(6, (-18, 0, -120)), K(12, (0, 0, 0)) },
            ["leg_L"] = new[] { K(0, (-8, 0, 6)), K(3, (0, 0, 10)), K(12, (0, 0, 0)) },
            ["leg_R"] = new[] { K(0, (8, 0, -6)), K(3, (0, 0, -10)), K(12, (0, 0, 0)) },
        },
        new Clip(RigidClip.Summon, 15)      // right hand raised high, then pointing down at the ground in front
        {
            ["chest"] = new[] { K(0, (0, 0, 0)), K(3, (-10, 12, 0)), K(5, (-12, 14, 0)), K(8, (22, -6, 0)), K(10, (22, -6, 0)), K(15, (0, 0, 0)) },
            ["head"] = new[] { K(0, (0, 0, 0)), K(3, (-18, -8, 0)), K(8, (18, 4, 0)), K(15, (0, 0, 0)) },
            ["hips"] = new[] { K(0, (0, 0, 0)), K(3, (0, 0, 0), (0, 0, 0.03f)), K(8, (0, 0, 0), (0, -0.03f, -0.05f)),
                               K(10, (0, 0, 0), (0, -0.03f, -0.05f)), K(15, (0, 0, 0)) },
            ["arm_R"] = new[] { K(0, (-40, 0, 0)), K(3, (-175, 0, 8)), K(5, (-178, 0, 8)), K(8, (-60, 6, 0)), K(10, (-58, 6, 0)), K(15, (0, 0, 0)) },
            ["arm_L"] = new[] { K(0, (0, 0, 0)), K(3, (-10, 0, 30)), K(8, (15, 0, 25)), K(15, (0, 0, 0)) },
        },
        new Clip(RigidClip.Bow, 12)         // side-on, aimed and drawn (left hand back), released: the aim kicks up, the left hand flies back
        {
            ["chest"] = new[] { K(0, (0, 28, 0)), K(2, (-8, 32, 0)), K(5, (-2, 30, 0)), K(12, (0, 0, 0)) },
            ["head"] = new[] { K(0, (0, -22, 0)), K(2, (-6, -26, 0)), K(12, (0, 0, 0)) },
            ["hips"] = new[] { K(0, (0, 0, 0)), K(2, (0, 0, 0), (0, 0.05f, 0)), K(12, (0, 0, 0)) },
            ["arm_R"] = new[] { K(0, (-92, -28, 0)), K(2, (-118, -28, 0)), K(5, (-95, -28, 0)), K(12, (0, 0, 0)) },
            ["hand_R"] = new[] { K(0, (92, 0, 0)), K(6, (92, 0, 0)), K(12, (0, 0, 0)) },
            ["arm_L"] = new[] { K(0, (25, 0, 18)), K(2, (45, 0, 35)), K(5, (30, 0, 25)), K(12, (0, 0, 0)) },
            ["leg_L"] = new[] { K(0, (-14, 0, 0)), K(6, (-14, 0, 0)), K(12, (0, 0, 0)) },
            ["leg_R"] = new[] { K(0, (12, 0, 0)), K(6, (12, 0, 0)), K(12, (0, 0, 0)) },
        },
        new Clip(RigidClip.Channel, 16, loop: true)     // both arms level forward, straining, feet braced
        {
            ["chest"] = new[] { K(0, (8, 0, 0)), K(8, (10, 0, 0)), K(16, (8, 0, 0)) },
            ["head"] = new[] { K(0, (-8, 0, 0)), K(16, (-8, 0, 0)) },
            ["hips"] = new[] { K(0, (0, 0, 0), (0, 0, -0.03f)), K(8, (0, 0, 0), (0, 0, -0.04f)), K(16, (0, 0, 0), (0, 0, -0.03f)) },
            ["arm_R"] = new[] { K(0, (-95, 10, 0)), K(4, (-99, 12, 0)), K(8, (-94, 9, 0)), K(12, (-98, 11, 0)), K(16, (-95, 10, 0)) },
            ["arm_L"] = new[] { K(0, (-95, -10, 0)), K(4, (-98, -9, 0)), K(8, (-94, -12, 0)), K(12, (-99, -10, 0)), K(16, (-95, -10, 0)) },
            ["leg_L"] = new[] { K(0, (-12, 0, 5)), K(16, (-12, 0, 5)) },
            ["leg_R"] = new[] { K(0, (10, 0, -5)), K(16, (10, 0, -5)) },
        },
        new Clip(RigidClip.Guard, 24, loop: true)       // braced low, off-hand held across the front facing out, weapon ready
        {
            ["chest"] = new[] { K(0, (12, -10, 0)), K(12, (14, -10, 0)), K(24, (12, -10, 0)) },
            ["head"] = new[] { K(0, (-12, 10, 0)), K(24, (-12, 10, 0)) },
            ["hips"] = new[] { K(0, (0, 0, 0), (0, 0, -0.05f)), K(12, (0, 0, 0), (0, 0, -0.06f)), K(24, (0, 0, 0), (0, 0, -0.05f)) },
            ["arm_L"] = new[] { K(0, (-90, -22, 0)), K(12, (-92, -22, 0)), K(24, (-90, -22, 0)) },
            ["hand_L"] = new[] { K(0, (78, 0, 0)), K(24, (78, 0, 0)) },
            ["arm_R"] = new[] { K(0, (-35, 0, -15)), K(12, (-38, 0, -15)), K(24, (-35, 0, -15)) },
            ["leg_L"] = new[] { K(0, (-22, 0, 8)), K(24, (-22, 0, 8)) },
            ["leg_R"] = new[] { K(0, (18, 0, -8)), K(24, (18, 0, -8)) },
        },
        new Clip(RigidClip.Tuck, 12, loop: true)        // curled into a ball: knees up, head down, arms wrapped in front
        {
            ["hips"] = new[] { K(0, (20, 0, 0), (0, 0, -0.42f)), K(6, (22, 0, 0), (0, 0, -0.44f)), K(12, (20, 0, 0), (0, 0, -0.42f)) },
            ["chest"] = new[] { K(0, (45, 0, 0)), K(6, (48, 0, 0)), K(12, (45, 0, 0)) },
            ["head"] = new[] { K(0, (25, 0, 0)), K(12, (25, 0, 0)) },
            ["arm_L"] = new[] { K(0, (-60, -25, -10)), K(12, (-60, -25, -10)) },
            ["arm_R"] = new[] { K(0, (-60, 25, 10)), K(12, (-60, 25, 10)) },
            ["leg_L"] = new[] { K(0, (-110, 0, 6)), K(12, (-110, 0, 6)) },
            ["leg_R"] = new[] { K(0, (-110, 0, -6)), K(12, (-110, 0, -6)) },
        },
    };

    // ---------- notation ----------

    struct Key
    {
        public int Frame;
        public Vector3 Rotation;    // Blender (pitch, yaw, roll), degrees
        public Vector3 Location;    // Blender (x, y, z), meters
    }

    class Clip : Dictionary<string, Key[]>
    {
        public readonly RigidClip Kind;
        public readonly int Length;
        public readonly bool Loop;

        public Clip(RigidClip kind, int length, bool loop = false)
        {
            Kind = kind; Length = length; Loop = loop;
        }
    }

    static Key K(int frame, (float p, float y, float r) rotation, (float x, float y, float z) location = default) => new Key
    {
        Frame = frame,
        Rotation = new Vector3(rotation.p, rotation.y, rotation.r),
        Location = new Vector3(location.x, location.y, location.z),
    };

    static int Bone(RigidAnimationSet set, string name)
    {
        int index = set.Bones.FindIndex(path => path == name || path.EndsWith("/" + name));
        if (index < 0) throw new KeyNotFoundException($"{RigPath} has no bone {name}.");
        return index;
    }

    // ---------- Blender to Unity ----------
    // pl_rig's bones all point up with roll 0, and after the FBX export every body bone of the Unity rig rests at the identity
    // rotation, so a sampled Blender clip frame is its pose itself. From the rig's Blender clips: Move leg_L pitch 30 is
    // Unity X +30, Move hips yaw 4 is Y -4, Spin leg_L roll 8 is Z -8, and Cast arm_R (-125, 0, -20) and Dash arm_L
    // (50, 0, 10) match only the order X first, then yaw, then roll (Blender's XYZ Euler). Locations: Blender's +x (left) is
    // Unity's -x, z (up) is y, and -y (forward) is z, as Dash hips z -0.06 is y -0.06. CheckAxes repeats this on every write.

    static Quaternion Rotation(Vector3 blender) =>
        Quaternion.AngleAxis(-blender.z, Vector3.forward) * Quaternion.AngleAxis(-blender.y, Vector3.up) * Quaternion.AngleAxis(blender.x, Vector3.right);

    static Vector3 Offset(Vector3 blender) => new Vector3(-blender.x, blender.z, -blender.y);

    // RigidClipWriter keys are Quaternion.Euler angles (Z first, then X, then Y): the same rotation, picking of its two angle
    // sets (and their turns of 360) the one closest to (pitch, -yaw, -roll), so keys blend the way they read
    static Vector3 UnityEuler(Vector3 blender)
    {
        var hint = new Vector3(blender.x, -blender.y, -blender.z);
        var euler = Rotation(blender).eulerAngles;
        var first = Near(euler, hint);
        var second = Near(new Vector3(180f - euler.x, euler.y + 180f, euler.z + 180f), hint);
        return (first - hint).sqrMagnitude <= (second - hint).sqrMagnitude ? first : second;
    }

    static Vector3 Near(Vector3 euler, Vector3 hint) => new Vector3(
        hint.x + Mathf.DeltaAngle(hint.x, euler.x), hint.y + Mathf.DeltaAngle(hint.y, euler.y), hint.z + Mathf.DeltaAngle(hint.z, euler.z));

    static RigidClipWriter.Key ToUnity(Key key) => new RigidClipWriter.Key(key.Frame, UnityEuler(key.Rotation), Offset(key.Location));

    // Frame 0 keys of clips Blender made (pl_player_equipment.py clips / skill_clips), against what the FBX import sampled
    static readonly (RigidClip Clip, string Bone, Key Key)[] Reference =
    {
        (RigidClip.Move, "leg_L", K(0, (30, 0, 0))), (RigidClip.Move, "hips", K(0, (0, 4, 0))), (RigidClip.Move, "chest", K(0, (9, -6, 0))),
        (RigidClip.Cast, "arm_R", K(0, (-125, 0, -20))), (RigidClip.Cast, "arm_L", K(0, (-70, 0, 6))), (RigidClip.Cast, "chest", K(0, (-5, 15, 0))),
        (RigidClip.Release, "arm_R", K(0, (-140, 0, -20))),
        (RigidClip.Dash, "arm_L", K(0, (50, 0, 10))), (RigidClip.Dash, "arm_R", K(0, (50, 0, -10))), (RigidClip.Dash, "hips", K(0, (0, 0, 0), (0, 0, -0.06f))),
        (RigidClip.Spin, "arm_L", K(0, (0, 0, 80))), (RigidClip.Spin, "arm_R", K(0, (-10, 0, -80))), (RigidClip.Spin, "leg_L", K(0, (0, 0, 8))),
        (RigidClip.Sweep, "chest", K(0, (0, 45, 0))), (RigidClip.Sweep, "arm_R", K(0, (-80, 0, 0))),
    };

    static void CheckAxes(RigidAnimationSet set, RigidClipWriter.Rest[] rest)
    {
        float angle = 0f, distance = 0f;
        int checks = 0;
        foreach (var (kind, name, key) in Reference)
        {
            var clip = set.Clips.FirstOrDefault(c => c.Kind == kind);
            if (clip == null || clip.Frames == 0) continue;
            int bone = Bone(set, name);
            var unity = ToUnity(key);
            angle = Mathf.Max(angle, Quaternion.Angle(rest[bone].Rotation * Quaternion.Euler(unity.Euler), clip.Rotations[bone]));
            distance = Mathf.Max(distance, Vector3.Distance(rest[bone].Position + unity.Offset, clip.Positions[bone]));
            checks++;
        }
        string result = $"Blender-to-Unity axes checked on {checks} sampled Blender keys: off by at most {angle:0.##} deg and {distance:0.###} m.";
        if (checks == 0 || angle > 1f || distance > 0.005f) Debug.LogWarning(result + " The rig's axes changed: fix PlayerSkillClips.Rotation / Offset.");
        else Debug.Log(result);
    }
}
