using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Key = RigidClipWriter.Key;

/// <summary>
/// Builds the companion bodies (Stone Guardian, Wolf, Wisp) in code, as there is no Blender source for them: chunky
/// flat-shaded parts with smoothed outline normals in UV3 (PerfectLoot/Comic's ink outline), one mesh per bone like
/// RigidModelBuilder's, a RigidAnimationSet with Idle, Move, Attack, Hit and Death (RigidClipWriter) and Comic materials.
/// Rewrites each visuals prefab in place, keeping its GUID: the primitive mesh goes; the collider, rigidbody, bars, ghost
/// and other authoring stay. It gets a "Model" hierarchy, a RigidAnimationAuthoring and its hit and death effects.
/// Models are made in model space: +Z forward, feet at y = 0, about 2 m tall; the skill's Scale sizes them.
/// Safe to run again; the colours set here overwrite the materials' on every build.
/// </summary>
public static class CompanionModelBuilder
{
    const string Folder = "Assets/Data/Companions";
    const string MaterialsFolder = Folder + "/Materials";
    const string CharacterTemplate = "Assets/ArtStyleTest/Materials/ComicCathedral/CT_Pale.mat";
    const string GlowTemplate = "Assets/ArtStyleTest/Materials/ComicCathedral/TR_Summon.mat";
    const float CapsuleHalfHeight = 1f;     // Visual prefabs have a 2 m capsule centered on the root
    static readonly float[] Sides = { -1f, 1f };
    static readonly Vector3 Zero = Vector3.zero;

    [MenuItem("Tools/Companions/Build Companion Models")]
    public static void Build()
    {
        EnsureFolder(MaterialsFolder);
        var palette = new Palette();
        var models = new[] { Guardian(palette), Wolf(palette), Wisp(palette) };
        foreach (var model in models) Save(model);
        AssetDatabase.SaveAssets();
        Debug.Log($"Companion models built: {string.Join(", ", models.Select(m => m.Name))}.");
    }

    // ---------------------------------------------------------------- Stone Guardian

    /// <summary>A hulking stone golem: short legs, a barrel chest, boulder shoulders, long arms with big blocky fists,
    /// a small head sunk between the shoulders. Gold summoner glow in its eyes, chest rune, cracks and forearm bands.</summary>
    static Model Guardian(Palette m)
    {
        var rig = new Rig();
        rig.Bone("Hips", null, V(0, 0.72f, 0));
        rig.Bone("Torso", "Hips", V(0, 0.95f, 0));
        rig.Bone("Head", "Torso", V(0, 1.62f, 0.2f));
        rig.Bone("Arm_L", "Torso", V(-0.68f, 1.55f, 0));
        rig.Bone("Arm_R", "Torso", V(0.68f, 1.55f, 0));
        rig.Bone("Leg_L", "Hips", V(-0.3f, 0.66f, 0));
        rig.Bone("Leg_R", "Hips", V(0.3f, 0.66f, 0));

        rig.Box("Hips", m.StoneDark, V(0, 0.7f, 0), V(0.9f, 0.34f, 0.6f), bevel: 0.07f);
        rig.Box("Torso", m.Stone, V(0, 1f, 0.04f), V(0.86f, 0.4f, 0.62f), bevel: 0.08f);
        rig.Box("Torso", m.Stone, V(0, 1.38f, 0), V(1.1f, 0.64f, 0.8f), bevel: 0.1f, taper: 1.1f);
        rig.Blob("Torso", m.Stone, V(-0.3f, 1.62f, -0.3f), V(0.42f, 0.38f, 0.42f), 0.1f, 3);
        rig.Blob("Torso", m.Stone, V(0.32f, 1.6f, -0.34f), V(0.38f, 0.34f, 0.38f), 0.1f, 4);
        rig.Blob("Torso", m.StoneDark, V(0, 1.42f, -0.44f), V(0.5f, 0.44f, 0.3f), 0.08f, 5);
        // Gold rune and cracks on the chest, and a seam across the shoulders for the camera above
        rig.Box("Torso", m.Glow, V(0, 1.38f, 0.43f), V(0.26f, 0.26f, 0.06f), V(0, 0, 45), bevel: 0f);
        rig.Box("Torso", m.Glow, V(-0.32f, 1.22f, 0.41f), V(0.06f, 0.34f, 0.05f), V(0, 0, 30), bevel: 0f);
        rig.Box("Torso", m.Glow, V(0.33f, 1.5f, 0.43f), V(0.06f, 0.28f, 0.05f), V(0, 0, -35), bevel: 0f);
        rig.Box("Torso", m.Glow, V(0.16f, 0.98f, 0.35f), V(0.05f, 0.22f, 0.04f), V(0, 0, 15), bevel: 0f);
        rig.Box("Torso", m.Glow, V(0, 1.71f, -0.04f), V(0.56f, 0.04f, 0.07f), V(0, 18, 0), bevel: 0f);

        rig.Box("Head", m.Stone, V(0, 1.76f, 0.34f), V(0.44f, 0.34f, 0.42f), bevel: 0.06f);
        rig.Box("Head", m.StoneDark, V(0, 1.62f, 0.38f), V(0.36f, 0.1f, 0.34f), bevel: 0.03f);                   // jaw
        rig.Box("Head", m.StoneDark, V(0, 1.87f, 0.51f), V(0.5f, 0.1f, 0.16f), V(8, 0, 0), bevel: 0.03f);        // brow
        foreach (float s in Sides) rig.Box("Head", m.Glow, V(0.11f * s, 1.77f, 0.555f), V(0.11f, 0.06f, 0.05f), bevel: 0f);

        foreach (float s in Sides)
        {
            string arm = s < 0 ? "Arm_L" : "Arm_R", leg = s < 0 ? "Leg_L" : "Leg_R";
            rig.Blob(arm, m.Stone, V(0.72f * s, 1.62f, 0), V(0.6f, 0.52f, 0.6f), 0.08f, s < 0 ? 6 : 7);             // shoulder boulder
            rig.Segment(arm, m.Stone, V(0.78f * s, 1f, 0.04f), V(0.72f * s, 1.5f, 0), 0.32f, 0.34f, 1.1f);
            rig.Segment(arm, m.StoneDark, V(0.8f * s, 0.6f, 0.08f), V(0.79f * s, 1.04f, 0.05f), 0.34f, 0.36f, 1.2f);
            rig.Box(arm, m.Glow, V(0.8f * s, 0.84f, 0.065f), V(0.4f, 0.06f, 0.42f), bevel: 0f);                     // forearm band
            rig.Box(arm, m.Stone, V(0.82f * s, 0.36f, 0.1f), V(0.54f, 0.48f, 0.58f), bevel: 0.1f);                  // fist
            rig.Box(arm, m.Glow, V(0.82f * s, 0.44f, 0.395f), V(0.34f, 0.08f, 0.04f), bevel: 0f);                   // knuckle rune
            rig.Segment(leg, m.StoneDark, V(0.32f * s, 0.16f, 0), V(0.3f * s, 0.66f, 0), 0.46f, 0.48f, 0.9f);
            rig.Box(leg, m.Stone, V(0.33f * s, 0.1f, 0.08f), V(0.52f, 0.2f, 0.66f), bevel: 0.06f);                  // foot
        }

        var model = new Model { Name = "StoneGuardian", Prefab = "Assets/Prefabs/GuardianVisuals.prefab", Rig = rig };
        // Slow breathing and looking around
        model.Clips.Add(rig.Clip(RigidClip.Idle, 48, true,
            T("Torso", K(0), K(24, V(3, 0, 0), V(0, -0.02f, 0)), K(48)),
            T("Head", K(0), K(16, 0, 8, 0), K(32, 0, -8, 0), K(48)),
            T("Arm_L", K(0), K(24, -3, 0, -3), K(48)),
            T("Arm_R", K(0), K(24, -3, 0, 3), K(48))));
        // A slow heavy walk: lurching hips, dipping on each step, arms swinging against the legs
        model.Clips.Add(rig.Clip(RigidClip.Move, 36, true,
            T("Leg_L", K(0, -22, 0, 0), K(18, 22, 0, 0), K(36, -22, 0, 0)),
            T("Leg_R", K(0, 22, 0, 0), K(18, -22, 0, 0), K(36, 22, 0, 0)),
            T("Hips", K(0, V(0, 6, 4), V(0, -0.05f, 0)), K(9, Zero, V(0, 0.02f, 0)), K(18, V(0, -6, -4), V(0, -0.05f, 0)),
                      K(27, Zero, V(0, 0.02f, 0)), K(36, V(0, 6, 4), V(0, -0.05f, 0))),
            T("Torso", K(0, 10, -5, 0), K(18, 10, 5, 0), K(36, 10, -5, 0)),
            T("Head", K(0, -8, 4, 0), K(18, -8, -4, 0), K(36, -8, 4, 0)),
            T("Arm_L", K(0, 16, 0, -4), K(18, -16, 0, -4), K(36, 16, 0, -4)),
            T("Arm_R", K(0, -16, 0, 4), K(18, 16, 0, 4), K(36, -16, 0, 4))));
        // Both fists up overhead and slammed down in front
        model.Clips.Add(rig.Clip(RigidClip.Attack, 26, false,
            T("Arm_L", K(0), K(6, -190, 0, -12), K(10, -55, 0, -6), K(16, -55, 0, -6), K(26)),
            T("Arm_R", K(0), K(6, -190, 0, 12), K(10, -55, 0, 6), K(16, -55, 0, 6), K(26)),
            T("Torso", K(0), K(6, -14, 0, 0), K(10, 30, 0, 0), K(16, 28, 0, 0), K(26)),
            T("Head", K(0), K(6, -10, 0, 0), K(10, -22, 0, 0), K(16, -22, 0, 0), K(26)),
            T("Hips", K(0), K(6, Zero, V(0, 0.04f, 0)), K(10, Zero, V(0, -0.08f, 0.05f)), K(16, Zero, V(0, -0.08f, 0.05f)), K(26)),
            T("Leg_L", K(0), K(10, -12, 0, -4), K(16, -12, 0, -4), K(26)),
            T("Leg_R", K(0), K(10, 10, 0, 4), K(16, 10, 0, 4), K(26))));
        model.Clips.Add(rig.Clip(RigidClip.Hit, 12, false,
            T("Torso", K(0), K(3, -14, 6, 0), K(12)),
            T("Head", K(0), K(3, -12, -8, 0), K(12)),
            T("Hips", K(0), K(3, Zero, V(0, 0, -0.06f)), K(12)),
            T("Arm_L", K(0), K(3, 12, 0, -8), K(12)),
            T("Arm_R", K(0), K(3, 12, 0, 8), K(12))));
        // Knees give, it slumps forward into a heap with its arms splayed, crumbling a little
        model.Clips.Add(rig.Clip(RigidClip.Death, 40, false,
            T("Hips", K(0), K(14, Zero, V(0, -0.3f, 0)), K(40, V(0, 0, 6), V(0, -0.5f, 0.05f))),
            T("Leg_L", K(0), K(14, -45, 0, -10), K(40, -80, 0, -18)),
            T("Leg_R", K(0), K(14, -40, 0, 10), K(40, -78, 0, 16)),
            T("Torso", K(0), K(14, 20, 0, 0), K(40, V(55, 8, 0), V(0, -0.04f, 0), 0.92f)),
            T("Head", K(0), K(20, 25, 10, 0), K(40, V(30, 15, 0), V(0, -0.12f, 0.08f), 0.85f)),
            T("Arm_L", K(0), K(14, -15, 0, -30), K(40, -20, 0, -70)),
            T("Arm_R", K(0), K(14, -12, 0, 30), K(40, -20, 0, 70))));
        return model;
    }

    // ---------------------------------------------------------------- Wolf

    /// <summary>A low, long wolf: deep chest, pale ruff and muzzle, chunky head and paws, a bushy tail. Dark grey-blue fur
    /// with gold glowing eyes, a rune on its shoulders and a rune tag at its throat.</summary>
    static Model Wolf(Palette m)
    {
        var rig = new Rig();
        rig.Bone("Body", null, V(0, 0.85f, 0));
        rig.Bone("Head", "Body", V(0, 1.02f, 0.78f));
        rig.Bone("Jaw", "Head", V(0, 0.96f, 1.08f));
        rig.Bone("Tail", "Body", V(0, 0.98f, -0.76f));
        rig.Bone("Leg_FL", "Body", V(-0.24f, 0.74f, 0.5f));
        rig.Bone("Leg_FR", "Body", V(0.24f, 0.74f, 0.5f));
        rig.Bone("Leg_BL", "Body", V(-0.22f, 0.76f, -0.5f));
        rig.Bone("Leg_BR", "Body", V(0.22f, 0.76f, -0.5f));

        rig.Box("Body", m.Fur, V(0, 0.9f, 0.32f), V(0.62f, 0.6f, 0.78f), bevel: 0.08f);                              // chest
        rig.Box("Body", m.Fur, V(0, 0.86f, -0.42f), V(0.54f, 0.52f, 0.72f), bevel: 0.08f, taper: 0.92f);             // hindquarters
        rig.Box("Body", m.FurLight, V(0, 0.63f, 0.22f), V(0.46f, 0.14f, 0.66f), bevel: 0.04f);                       // belly
        rig.Blob("Body", m.FurLight, V(0, 1.05f, 0.6f), V(0.78f, 0.66f, 0.5f), 0.08f, 11);                           // ruff
        rig.Cone("Body", m.Fur, V(0, 1.12f, -0.12f), V(0, 1.3f, -0.3f), 0.11f, 4);                                   // fur tufts
        rig.Cone("Body", m.Fur, V(0, 1.08f, -0.42f), V(0, 1.24f, -0.6f), 0.1f, 4);
        rig.Box("Body", m.Glow, V(0, 1.205f, 0.16f), V(0.18f, 0.04f, 0.18f), V(0, 45, 0), bevel: 0f);              // shoulder rune
        rig.Box("Body", m.Glow, V(0, 0.74f, 0.8f), V(0.17f, 0.17f, 0.06f), V(0, 0, 45), bevel: 0f);                // throat tag

        rig.Box("Head", m.Fur, V(0, 1.1f, 0.98f), V(0.46f, 0.4f, 0.42f), bevel: 0.07f);                              // skull
        rig.Box("Head", m.FurLight, V(0, 1.02f, 1.3f), V(0.28f, 0.2f, 0.34f), bevel: 0.05f, taper: 0.85f);          // muzzle
        rig.Box("Head", m.Dark, V(0, 1.1f, 1.47f), V(0.12f, 0.08f, 0.06f), bevel: 0.02f);                            // nose
        rig.Box("Head", m.Fur, V(0, 1.25f, 1.12f), V(0.44f, 0.08f, 0.14f), V(10, 0, 0), bevel: 0.03f);              // brow
        foreach (float s in Sides)
        {
            rig.Box("Head", m.Glow, V(0.12f * s, 1.17f, 1.19f), V(0.1f, 0.05f, 0.04f), bevel: 0f);                   // eyes
            rig.Cone("Head", m.Fur, V(0.15f * s, 1.27f, 0.92f), V(0.2f * s, 1.52f, 0.86f), 0.1f, 4);                 // ears
            rig.Cone("Head", m.Bone, V(0.08f * s, 0.95f, 1.38f), V(0.08f * s, 0.86f, 1.39f), 0.035f, 4);             // fangs
        }
        rig.Box("Jaw", m.FurLight, V(0, 0.88f, 1.26f), V(0.24f, 0.09f, 0.34f), bevel: 0.03f);

        rig.Segment("Tail", m.Fur, V(0, 0.8f, -1.18f), V(0, 0.98f, -0.78f), 0.2f, 0.2f, 1.2f);
        rig.Blob("Tail", m.FurLight, V(0, 0.74f, -1.3f), V(0.26f, 0.26f, 0.36f), 0.08f, 12, euler: V(-25, 0, 0));

        foreach (float s in Sides)
        {
            string front = s < 0 ? "Leg_FL" : "Leg_FR", back = s < 0 ? "Leg_BL" : "Leg_BR";
            float x = 0.24f * s;
            rig.Segment(front, m.Fur, V(x, 0.36f, 0.52f), V(x, 0.82f, 0.48f), 0.2f, 0.22f, 1.25f);
            rig.Segment(front, m.Fur, V(x, 0.1f, 0.54f), V(x, 0.4f, 0.52f), 0.16f, 0.16f);
            rig.Box(front, m.FurLight, V(x, 0.07f, 0.6f), V(0.22f, 0.14f, 0.28f), bevel: 0.04f);                    // paw
            rig.Box(front, m.Dark, V(x, 0.04f, 0.75f), V(0.18f, 0.06f, 0.05f), bevel: 0.01f);                       // claws
            x = 0.22f * s;
            rig.Segment(back, m.Fur, V(x, 0.42f, -0.62f), V(x, 0.84f, -0.48f), 0.22f, 0.26f, 1.3f);
            rig.Segment(back, m.Fur, V(x, 0.1f, -0.58f), V(x, 0.44f, -0.64f), 0.15f, 0.15f);
            rig.Box(back, m.FurLight, V(x, 0.07f, -0.52f), V(0.21f, 0.14f, 0.26f), bevel: 0.04f);
            rig.Box(back, m.Dark, V(x, 0.04f, -0.38f), V(0.17f, 0.06f, 0.05f), bevel: 0.01f);
        }

        // Bigger than its 0.8 skill scale alone: it's low, so it read too small from the gameplay camera
        var model = new Model { Name = "Wolf", Prefab = "Assets/Prefabs/WolfVisuals.prefab", Rig = rig, Size = 1.25f };
        // Breathing, panting, looking around, wagging
        model.Clips.Add(rig.Clip(RigidClip.Idle, 48, true,
            T("Body", K(0), K(24, Zero, V(0, -0.015f, 0)), K(48)),
            T("Head", K(0), K(12, -4, 10, 0), K(30, 2, -8, 0), K(48)),
            T("Jaw", K(0), K(12, 6, 0, 0), K(24), K(36, 6, 0, 0), K(48)),
            T("Tail", K(0), K(12, 0, 14, 0), K(24, 0, -14, 0), K(36, 0, 14, 0), K(48))));
        // Trot: diagonal leg pairs together, the body dipping when the legs reach out
        model.Clips.Add(rig.Clip(RigidClip.Move, 16, true,
            T("Leg_FL", K(0, -28, 0, 0), K(8, 26, 0, 0), K(16, -28, 0, 0)),
            T("Leg_BR", K(0, -28, 0, 0), K(8, 26, 0, 0), K(16, -28, 0, 0)),
            T("Leg_FR", K(0, 26, 0, 0), K(8, -28, 0, 0), K(16, 26, 0, 0)),
            T("Leg_BL", K(0, 26, 0, 0), K(8, -28, 0, 0), K(16, 26, 0, 0)),
            T("Body", K(0, Zero, V(0, -0.05f, 0)), K(4), K(8, Zero, V(0, -0.05f, 0)), K(12), K(16, Zero, V(0, -0.05f, 0))),
            T("Head", K(0, 4, 0, 0), K(4, -2, 0, 0), K(8, 4, 0, 0), K(12, -2, 0, 0), K(16, 4, 0, 0)),
            T("Tail", K(0, 12, 10, 0), K(8, 12, -10, 0), K(16, 12, 10, 0))));
        // Crouch with the jaw open, lunge forward and snap it shut
        model.Clips.Add(rig.Clip(RigidClip.Attack, 16, false,
            T("Body", K(0), K(3, V(-8, 0, 0), V(0, -0.06f, -0.12f)), K(6, V(6, 0, 0), V(0, 0.02f, 0.38f)), K(10, V(4, 0, 0), V(0, 0, 0.3f)), K(16)),
            T("Head", K(0), K(3, -22, 0, 0), K(6, 12, 0, 0), K(10, 8, 0, 0), K(16)),
            T("Jaw", K(0), K(3, 38, 0, 0), K(6), K(16)),
            T("Leg_FL", K(0), K(3, 10, 0, 0), K(6, -40, 0, 0), K(10, -30, 0, 0), K(16)),
            T("Leg_FR", K(0), K(3, 10, 0, 0), K(6, -40, 0, 0), K(10, -30, 0, 0), K(16)),
            T("Leg_BL", K(0), K(3, -12, 0, 0), K(6, 30, 0, 0), K(10, 20, 0, 0), K(16)),
            T("Leg_BR", K(0), K(3, -12, 0, 0), K(6, 30, 0, 0), K(10, 20, 0, 0), K(16)),
            T("Tail", K(0), K(3, 20, 0, 0), K(6, -5, 0, 0), K(16))));
        model.Clips.Add(rig.Clip(RigidClip.Hit, 10, false,
            T("Body", K(0), K(3, V(-10, 8, 0), V(0, 0.04f, -0.14f)), K(10)),
            T("Head", K(0), K(3, -18, 12, 0), K(10)),
            T("Tail", K(0), K(3, -15, 0, 0), K(10))));
        // Rolls over onto its side, legs stiff
        model.Clips.Add(rig.Clip(RigidClip.Death, 32, false,
            T("Body", K(0), K(10, V(-10, 0, 40), V(0, -0.15f, 0)), K(20, V(0, 0, 88), V(0, -0.48f, 0)), K(32, V(0, 0, 90), V(0, -0.5f, 0))),
            T("Leg_FL", K(0), K(20, -30, 0, 0), K(32, -30, 0, 0)),
            T("Leg_FR", K(0), K(20, -20, 0, 0), K(32, -20, 0, 0)),
            T("Leg_BL", K(0), K(20, 25, 0, 0), K(32, 25, 0, 0)),
            T("Leg_BR", K(0), K(20, 30, 0, 0), K(32, 30, 0, 0)),
            T("Head", K(0), K(12, -20, 0, 0), K(24, 10, 0, -10), K(32, 12, 0, -12)),
            T("Jaw", K(0), K(24, 14, 0, 0), K(32, 14, 0, 0)),
            T("Tail", K(0), K(20, -10, 20, 0), K(32, -10, 20, 0))));
        return model;
    }

    // ---------------------------------------------------------------- Wisp

    /// <summary>A floating flame spirit: a white-hot core with two dark eyes, gold flame tongues on top, a flame tail
    /// trailing behind and below, three ember chunks orbiting it. No legs: it hovers well above its feet (the body's
    /// root stays at capsule height, its rigidbody's Y is frozen).</summary>
    static Model Wisp(Palette m)
    {
        var rig = new Rig();
        rig.Bone("Body", null, V(0, 1.35f, 0));
        rig.Bone("Flame", "Body", V(0, 1.55f, 0));
        rig.Bone("Tail", "Body", V(0, 1.25f, -0.05f));
        rig.Bone("Embers", "Body", V(0, 1.35f, 0));

        rig.Blob("Body", m.WispCore, V(0, 1.35f, 0), V(0.84f, 0.84f, 0.84f), 0.03f, 21);
        foreach (float s in Sides) rig.Box("Body", m.Dark, V(0.11f * s, 1.4f, 0.4f), V(0.08f, 0.13f, 0.05f), bevel: 0.01f);
        rig.Cone("Flame", m.Glow, V(0, 1.55f, 0), V(0, 2.12f, 0.04f), 0.28f);
        foreach (float s in Sides) rig.Cone("Flame", m.Glow, V(0.22f * s, 1.48f, 0.04f), V(0.4f * s, 1.86f, 0.08f), 0.14f, 5);
        rig.Cone("Tail", m.Glow, V(0, 1.25f, -0.05f), V(0, 0.55f, -0.55f), 0.22f);
        float[] heights = { 0.1f, -0.08f, 0.02f };
        for (int i = 0; i < 3; i++)
        {
            float a = i * Mathf.PI * 2f / 3f;
            rig.Blob("Embers", m.Glow, V(Mathf.Cos(a) * 0.64f, 1.35f + heights[i], Mathf.Sin(a) * 0.64f), V(0.2f, 0.2f, 0.2f), 0.15f, 30 + i, detail: 0);
        }

        var model = new Model { Name = "Wisp", Prefab = "Assets/Prefabs/WispVisuals.prefab", Rig = rig, Size = 1.35f };
        // Hovering bob, flickering flame, swaying tail; the embers orbit in every clip
        var idle = rig.Clip(RigidClip.Idle, 48, true,
            T("Body", K(0), K(24, Zero, V(0, 0.1f, 0)), K(48)),
            T("Flame", K(0), K(8, V(0, 0, 5), Zero, 1.1f), K(16, V(-4, 0, -3), Zero, 0.94f), K(28, V(3, 0, 4), Zero, 1.08f), K(38, V(0, 0, -4), Zero, 0.96f), K(48)),
            T("Tail", K(0), K(12, 6, 14, 0), K(24, -4, 0, 0), K(36, 6, -14, 0), K(48)));
        // Leaning into the flight, flame blown back, tail streaming behind
        var move = rig.Clip(RigidClip.Move, 24, true,
            T("Body", K(0, 14, 0, 0), K(12, V(14, 0, 0), V(0, 0.08f, 0)), K(24, 14, 0, 0)),
            T("Flame", K(0, V(-18, 0, 4), Zero, 1.05f), K(12, V(-22, 0, -4), Zero, 1.12f), K(24, V(-18, 0, 4), Zero, 1.05f)),
            T("Tail", K(0, 26, 12, 0), K(12, 30, -12, 0), K(24, 26, 12, 0)));
        // Draws back, then pulses forward with its flame and embers flaring
        var attack = rig.Clip(RigidClip.Attack, 14, false,
            T("Body", K(0), K(4, V(-10, 0, 0), V(0, 0.02f, -0.16f), 0.88f), K(7, V(12, 0, 0), V(0, 0, 0.32f), 1.22f), K(14)),
            T("Flame", K(0), K(4, V(-10, 0, 0), Zero, 0.85f), K(7, V(18, 0, 0), Zero, 1.35f), K(14)),
            T("Embers", K(0), K(4, Zero, Zero, 0.8f), K(7, Zero, Zero, 1.45f), K(14)),
            T("Tail", K(0), K(4, -10, 0, 0), K(7, 30, 0, 0), K(14)));
        var hit = rig.Clip(RigidClip.Hit, 10, false,
            T("Body", K(0), K(3, V(-14, 0, 0), V(0, 0.06f, -0.16f), 0.86f), K(10)),
            T("Flame", K(0), K(3, V(-24, 0, 0), Zero, 0.8f), K(10)),
            T("Tail", K(0), K(3, -12, 0, 0), K(10)));
        // Flares up, then gutters out: flame, tail and core shrink away while the embers burst outward and fade
        var death = rig.Clip(RigidClip.Death, 30, false,
            T("Body", K(0), K(6, Zero, V(0, 0.12f, 0), 1.2f), K(30, Zero, V(0, -0.35f, 0), 0.1f)),
            T("Flame", K(0), K(6, Zero, Zero, 1.4f), K(22, Zero, Zero, 0.05f), K(30, Zero, Zero, 0.05f)),
            T("Embers", K(0), K(10, Zero, V(0, 0.1f, 0), 1.9f), K(24, Zero, Zero, 0.05f), K(30, Zero, Zero, 0.05f)),
            T("Tail", K(0), K(18, Zero, Zero, 0.05f), K(30, Zero, Zero, 0.05f)));
        Spin(rig, idle, "Embers", 0.5f);
        Spin(rig, move, "Embers", 1f);
        Spin(rig, attack, "Embers", 0.5f);
        Spin(rig, hit, "Embers", 0.5f);
        Spin(rig, death, "Embers", 1f);
        model.Clips.AddRange(new[] { idle, move, attack, hit, death });
        return model;
    }

    // ---------------------------------------------------------------- Saving

    class Model
    {
        public string Name, Prefab;
        public Rig Rig;
        public float Size = 1f;     // Scales the whole model (not the collider) on top of the skill's Scale
        public readonly List<RigidAnimationSet.Clip> Clips = new List<RigidAnimationSet.Clip>();
    }

    static void Save(Model model)
    {
        var rig = model.Rig;
        string setPath = $"{Folder}/{model.Name}_Rig.asset";
        var set = AssetDatabase.LoadAssetAtPath<RigidAnimationSet>(setPath);
        if (set == null)
        {
            set = ScriptableObject.CreateInstance<RigidAnimationSet>();
            AssetDatabase.CreateAsset(set, setPath);
        }
        // The part meshes are sub-assets of the set, as RigidModelBuilder does it
        foreach (var old in AssetDatabase.LoadAllAssetRepresentationsAtPath(setPath).OfType<Mesh>()) UnityEngine.Object.DestroyImmediate(old, true);
        var meshes = rig.Bones.Select(b => b.BuildMesh($"{model.Name}_{b.Name}")).ToArray();
        foreach (var mesh in meshes)
            if (mesh != null) AssetDatabase.AddObjectToAsset(mesh, set);
        set.Bones = Enumerable.Range(0, rig.Bones.Count).Select(rig.Path).ToList();
        set.Clips = model.Clips.OrderBy(c => c.Kind).ToList();
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();

        RewritePrefab(model, set, meshes);
    }

    // Same asset, same GUID and root fileID (the skills and CompanionPrefabsAuthoring point at it)
    static void RewritePrefab(Model model, RigidAnimationSet set, Mesh[] meshes)
    {
        var root = PrefabUtility.LoadPrefabContents(model.Prefab);
        try
        {
            // The primitive stand-in goes; collider, rigidbody, bars, ghost and the other authoring stay
            if (root.TryGetComponent<MeshRenderer>(out var renderer)) UnityEngine.Object.DestroyImmediate(renderer);
            if (root.TryGetComponent<MeshFilter>(out var filter)) UnityEngine.Object.DestroyImmediate(filter);
            var old = root.transform.Find("Model");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);

            // The model's feet at the bottom of the capsule
            var modelRoot = new GameObject("Model") { layer = root.layer };
            modelRoot.transform.SetParent(root.transform, false);
            modelRoot.transform.localPosition = new Vector3(0f, -CapsuleHalfHeight, 0f);
            modelRoot.transform.localScale = Vector3.one * model.Size;

            var rig = model.Rig;
            var bones = new Transform[rig.Bones.Count];
            float top = 0f;
            for (int i = 0; i < bones.Length; i++)
            {
                var bone = rig.Bones[i];
                var transform = new GameObject(bone.Name) { layer = root.layer }.transform;
                transform.SetParent(bone.Parent < 0 ? modelRoot.transform : bones[bone.Parent], false);
                transform.localPosition = rig.Local(i);
                bones[i] = transform;
                if (meshes[i] == null) continue;
                transform.gameObject.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                transform.gameObject.AddComponent<MeshRenderer>().sharedMaterials = bone.Materials.ToArray();
                top = Mathf.Max(top, bone.Position.y + meshes[i].bounds.max.y);
            }
            top *= model.Size;

            // Bars just above the head
            foreach (var name in new[] { "HealthBar/Stick", "ResourceBar/Stick" })
            {
                var stick = root.transform.Find(name);
                if (stick == null) continue;
                var position = stick.localPosition;
                position.y = top - CapsuleHalfHeight + (name.StartsWith("Health") ? 0.35f : 0.05f);
                stick.localPosition = position;
            }

            var animation = root.GetComponent<RigidAnimationAuthoring>();
            if (animation == null) animation = root.AddComponent<RigidAnimationAuthoring>();
            animation.Animation = set;
            animation.Bones = bones;

            var vfx = root.GetComponent<UnitVfxAuthoring>();
            if (vfx == null) vfx = root.AddComponent<UnitVfxAuthoring>();
            vfx.Hit = VfxType.UnitHit;
            vfx.Death = VfxType.UnitDeath;

            PrefabUtility.SaveAsPrefabAsset(root, model.Prefab);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// Turns a bone around its Y axis at a steady rate on top of its keys (keyframes ease in and out, so they can't spin
    /// steadily). Whole turns over a loop, so it doesn't jump where it wraps.
    /// </summary>
    static void Spin(Rig rig, RigidAnimationSet.Clip clip, string bone, float turnsPerSecond)
    {
        int b = rig.Index(bone), count = rig.Bones.Count;
        float turns = clip.Loop ? Mathf.Max(1f, Mathf.Round(clip.Length * turnsPerSecond)) : clip.Length * turnsPerSecond;
        for (int f = 0; f < clip.Frames; f++)
        {
            float t = Mathf.Min(f / clip.SampleRate, clip.Length) / clip.Length;
            int i = f * count + b;
            clip.Rotations[i] = clip.Rotations[i] * Quaternion.Euler(0f, 360f * turns * t, 0f);
        }
    }

    // ---------------------------------------------------------------- Materials

    class Palette
    {
        public readonly Material Stone = Make("CO_Stone", "#8A8F99");          // armour grey
        public readonly Material StoneDark = Make("CO_StoneDark", "#5E636E");
        public readonly Material Fur = Make("CO_WolfFur", "#6E7A98");          // grey-blue, kept brighter than the floor
        public readonly Material FurLight = Make("CO_WolfFurLight", "#C4CBDA");
        public readonly Material Bone = Make("CO_Bone", "#E6DFCC");
        public readonly Material Dark = Make("CO_Dark", "#24212B");
        public readonly Material Glow = Make("CO_SummonGlow", "#FFC93A", "#FFB21E", 1.4f, 0.3f);    // summoner gold, saturated so bloom keeps it gold
        public readonly Material WispCore = Make("CO_WispCore", "#FFF6DC", "#FFF0C4", 1.8f, 0.15f);
    }

    // A copy of a character material (or the summoner glow one), recoloured; the colours here win on every build
    static Material Make(string name, string color, string glow = null, float strength = 0f, float pulse = 0f)
    {
        string path = $"{MaterialsFolder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            string templatePath = glow != null ? GlowTemplate : CharacterTemplate;
            var template = AssetDatabase.LoadAssetAtPath<Material>(templatePath);
            if (template == null) throw new FileNotFoundException(templatePath);
            material = new Material(template);
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", Hex(color));
        if (glow != null)
        {
            // The glow is the emission mask times its colour, and the mask defaults to black
            material.SetTexture("_EmissionMap", GlowMask());
            material.SetColor("_EmissionColor", Hex(glow) * strength);
            material.SetFloat("_EmissionPulse", pulse);
            material.SetFloat("_PatternStrength", 0f);   // Glowing parts never get hatched
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    static Texture2D GlowMask()
    {
        string path = $"{MaterialsFolder}/CO_GlowMask.asset";
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (texture != null) return texture;
        texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "CO_GlowMask" };
        texture.SetPixels(Enumerable.Repeat(Color.white, 16).ToArray());
        texture.Apply();
        AssetDatabase.CreateAsset(texture, path);
        return texture;
    }

    // ---------------------------------------------------------------- Rig and part meshes

    class RigBone
    {
        public string Name;
        public int Parent;
        public Vector3 Position;    // Model space; rest rotations are all identity
        public readonly List<Material> Materials = new List<Material>();
        readonly List<Surface> _surfaces = new List<Surface>();

        public Surface For(Material material)
        {
            int index = Materials.IndexOf(material);
            if (index >= 0) return _surfaces[index];
            Materials.Add(material);
            _surfaces.Add(new Surface());
            return _surfaces[_surfaces.Count - 1];
        }

        /// <summary>All this bone's parts as one mesh in its space, a submesh per material; null without parts.</summary>
        public Mesh BuildMesh(string name)
        {
            if (_surfaces.Count == 0) return null;
            var mesh = new Mesh { name = name };
            var positions = _surfaces.SelectMany(s => s.Positions).ToList();
            mesh.SetVertices(positions);
            mesh.SetNormals(_surfaces.SelectMany(s => s.Normals).ToList());
            mesh.SetTangents(_surfaces.SelectMany(s => s.Tangents).ToList());
            mesh.SetUVs(0, Enumerable.Repeat(Vector2.zero, positions.Count).ToList());
            mesh.SetUVs(3, _surfaces.SelectMany(s => s.Outline).ToList());
            mesh.subMeshCount = _surfaces.Count;
            int start = 0;
            for (int s = 0; s < _surfaces.Count; s++)
            {
                int count = _surfaces[s].Positions.Count;
                mesh.SetTriangles(Enumerable.Range(start, count).ToArray(), s);
                start += count;
            }
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    // Flat-shaded triangles, three vertices each, with the outline normals in UV3
    class Surface
    {
        public readonly List<Vector3> Positions = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector4> Tangents = new List<Vector4>();
        public readonly List<Vector3> Outline = new List<Vector3>();
    }

    /// <summary>Bones and their parts, all placed in model space (+Z forward, feet at y = 0).</summary>
    class Rig
    {
        public readonly List<RigBone> Bones = new List<RigBone>();

        public void Bone(string name, string parent, Vector3 position) =>
            Bones.Add(new RigBone { Name = name, Parent = parent == null ? -1 : Index(parent), Position = position });

        public int Index(string name)
        {
            int index = Bones.FindIndex(b => b.Name == name);
            if (index < 0) throw new ArgumentException($"No bone named {name}");
            return index;
        }

        public Vector3 Local(int bone) => Bones[bone].Parent < 0 ? Bones[bone].Position : Bones[bone].Position - Bones[Bones[bone].Parent].Position;

        public string Path(int bone) => Bones[bone].Parent < 0 ? Bones[bone].Name : Path(Bones[bone].Parent) + "/" + Bones[bone].Name;

        public RigidAnimationSet.Clip Clip(RigidClip kind, int length, bool loop, params (string Bone, Key[] Keys)[] tracks)
        {
            var rest = Enumerable.Range(0, Bones.Count)
                .Select(i => new RigidClipWriter.Rest { Position = Local(i), Rotation = Quaternion.identity, Scale = 1f }).ToArray();
            return RigidClipWriter.Make(kind, length, loop, rest, tracks.ToDictionary(t => Index(t.Bone), t => t.Keys));
        }

        /// <summary>A box with chamfered edges; taper scales its top half (x and z).</summary>
        public void Box(string bone, Material material, Vector3 center, Vector3 size, Vector3 euler = default, float bevel = 0.05f, float taper = 1f)
        {
            var (points, faces) = BoxShape(size, bevel, taper);
            Add(bone, material, points, faces, Matrix4x4.TRS(center, Quaternion.Euler(euler), Vector3.one));
        }

        /// <summary>A box from bottom to top (a limb, a tail), width and depth at the bottom, topScale times that at the top.</summary>
        public void Segment(string bone, Material material, Vector3 bottom, Vector3 top, float width, float depth, float topScale = 1f, float bevel = 0.04f)
        {
            var axis = top - bottom;
            var (points, faces) = BoxShape(new Vector3(width, axis.magnitude, depth), bevel, topScale);
            Add(bone, material, points, faces, Matrix4x4.TRS((top + bottom) * 0.5f, Quaternion.FromToRotation(Vector3.up, axis), Vector3.one));
        }

        /// <summary>A faceted lump (icosphere, roughened by jitter), size its diameters.</summary>
        public void Blob(string bone, Material material, Vector3 center, Vector3 size, float jitter = 0.06f, int seed = 1, int detail = 1, Vector3 euler = default)
        {
            var (points, faces) = IcoShape(detail, jitter, seed);
            Add(bone, material, points, faces, Matrix4x4.TRS(center, Quaternion.Euler(euler), size));
        }

        /// <summary>A pointed cone from its base to its tip: ears, tufts, fangs, flame tongues.</summary>
        public void Cone(string bone, Material material, Vector3 baseCenter, Vector3 tip, float radius, int sides = 6)
        {
            var axis = tip - baseCenter;
            var points = new List<Vector3>();
            var faces = new List<int[]> { Enumerable.Range(0, sides).ToArray() };
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                points.Add(new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
                faces.Add(new[] { i, (i + 1) % sides, sides });
            }
            points.Add(new Vector3(0f, axis.magnitude, 0f));
            Add(bone, material, points, faces, Matrix4x4.TRS(baseCenter, Quaternion.FromToRotation(Vector3.up, axis), Vector3.one));
        }

        // A convex part: faces are turned outward from its center, each gets its own flat normal, and every corner's
        // outline normal is the average of the faces meeting there (in tangent space, as PerfectLoot/Comic reads UV3)
        void Add(string boneName, Material material, List<Vector3> points, List<int[]> faces, Matrix4x4 placement)
        {
            var bone = Bones[Index(boneName)];
            var p = points.Select(v => placement.MultiplyPoint3x4(v) - bone.Position).ToList();
            var center = p.Aggregate(Vector3.zero, (sum, v) => sum + v) / p.Count;
            var smooth = new Vector3[p.Count];
            var polygons = new List<(int[] Face, Vector3 Normal)>();
            foreach (var face in faces)
            {
                var normal = Vector3.zero;
                var middle = Vector3.zero;
                for (int i = 1; i + 1 < face.Length; i++) normal += Vector3.Cross(p[face[i]] - p[face[0]], p[face[i + 1]] - p[face[0]]);
                foreach (int i in face) middle += p[i];
                if (normal.sqrMagnitude < 1e-12f) continue;
                var ordered = face;
                if (Vector3.Dot(normal, middle / face.Length - center) < 0f)
                {
                    ordered = face.Reverse().ToArray();
                    normal = -normal;
                }
                normal.Normalize();
                foreach (int i in ordered) smooth[i] += normal;
                polygons.Add((ordered, normal));
            }

            var surface = bone.For(material);
            foreach (var (face, normal) in polygons)
            {
                var tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.forward).normalized;
                var bitangent = Vector3.Cross(normal, tangent);
                for (int i = 1; i + 1 < face.Length; i++)
                {
                    foreach (int v in new[] { face[0], face[i], face[i + 1] })
                    {
                        var s = smooth[v].normalized;
                        surface.Positions.Add(p[v]);
                        surface.Normals.Add(normal);
                        surface.Tangents.Add(new Vector4(tangent.x, tangent.y, tangent.z, 1f));
                        surface.Outline.Add(new Vector3(Vector3.Dot(s, tangent), Vector3.Dot(s, bitangent), Vector3.Dot(s, normal)));
                    }
                }
            }
        }
    }

    // A box centered on the origin. With a bevel, each corner has three vertices (one per face it touches), joined by
    // a quad along every edge and a triangle at every corner.
    static (List<Vector3>, List<int[]>) BoxShape(Vector3 size, float bevel, float taper)
    {
        var half = size * 0.5f;
        float b = Mathf.Min(bevel, Mathf.Min(half.x, Mathf.Min(half.y, half.z)) * 0.45f);
        bool chamfer = b > 0.001f;
        var points = new List<Vector3>();
        var faces = new List<int[]>();
        for (int c = 0; c < 8; c++)
        {
            var signs = Signs(c);
            var corner = Vector3.Scale(signs, half);
            if (!chamfer) { points.Add(corner); continue; }
            for (int axis = 0; axis < 3; axis++)
            {
                var point = corner;
                for (int k = 0; k < 3; k++)
                    if (k != axis) point[k] -= signs[k] * b;
                points.Add(point);
            }
        }

        int Vertex(int corner, int axis) => chamfer ? corner * 3 + axis : corner;
        int[] around = { -1, -1, 1, -1, 1, 1, -1, 1 };     // (u, w) signs in order around a face
        for (int a = 0; a < 3; a++)
        {
            int u = (a + 1) % 3, w = (a + 2) % 3;
            foreach (int side in new[] { -1, 1 })
            {
                var face = new int[4];
                for (int i = 0; i < 4; i++) face[i] = Vertex(Corner(a, side, u, around[i * 2], w, around[i * 2 + 1]), a);
                faces.Add(face);
            }
            if (!chamfer) continue;
            // The bevels along the four edges running along axis a
            for (int i = 0; i < 4; i++)
            {
                int c0 = Corner(a, -1, u, around[i * 2], w, around[i * 2 + 1]);
                int c1 = Corner(a, 1, u, around[i * 2], w, around[i * 2 + 1]);
                faces.Add(new[] { Vertex(c0, u), Vertex(c0, w), Vertex(c1, w), Vertex(c1, u) });
            }
        }
        if (chamfer)
            for (int c = 0; c < 8; c++) faces.Add(new[] { Vertex(c, 0), Vertex(c, 1), Vertex(c, 2) });

        if (!Mathf.Approximately(taper, 1f))
            for (int i = 0; i < points.Count; i++)
                if (points[i].y > 0f) points[i] = new Vector3(points[i].x * taper, points[i].y, points[i].z * taper);
        return (points, faces);
    }

    static Vector3 Signs(int corner) => new Vector3((corner & 1) != 0 ? 1f : -1f, (corner & 2) != 0 ? 1f : -1f, (corner & 4) != 0 ? 1f : -1f);

    static int Corner(int a, int signA, int u, int signU, int w, int signW)
    {
        var signs = new int[3];
        signs[a] = signA;
        signs[u] = signU;
        signs[w] = signW;
        return (signs[0] > 0 ? 1 : 0) | (signs[1] > 0 ? 2 : 0) | (signs[2] > 0 ? 4 : 0);
    }

    // Icosahedron of diameter 1, subdivided detail times, each vertex pushed in or out by up to jitter
    static (List<Vector3>, List<int[]>) IcoShape(int detail, float jitter, int seed)
    {
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        var points = new List<Vector3>
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
        };
        var triangles = new List<int> { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                                        3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
        for (int d = 0; d < detail; d++)
        {
            var middles = new Dictionary<(int, int), int>();
            int Middle(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                if (middles.TryGetValue(key, out int m)) return m;
                points.Add((points[a] + points[b]) * 0.5f);
                return middles[key] = points.Count - 1;
            }
            var subdivided = new List<int>();
            for (int i = 0; i < triangles.Count; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                int ab = Middle(a, b), bc = Middle(b, c), ca = Middle(c, a);
                subdivided.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            triangles = subdivided;
        }
        var random = new System.Random(seed);
        for (int i = 0; i < points.Count; i++)
            points[i] = points[i].normalized * 0.5f * (1f + jitter * (float)(random.NextDouble() * 2.0 - 1.0));
        var faces = new List<int[]>();
        for (int i = 0; i < triangles.Count; i += 3) faces.Add(new[] { triangles[i], triangles[i + 1], triangles[i + 2] });
        return (points, faces);
    }

    // ---------------------------------------------------------------- Helpers

    static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
    static (string, Key[]) T(string bone, params Key[] keys) => (bone, keys);
    static Key K(int frame) => new Key(frame, Vector3.zero);
    static Key K(int frame, float x, float y, float z) => new Key(frame, new Vector3(x, y, z));
    static Key K(int frame, Vector3 euler, Vector3 offset, float scale = 1f) => new Key(frame, euler, offset, scale);

    static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
