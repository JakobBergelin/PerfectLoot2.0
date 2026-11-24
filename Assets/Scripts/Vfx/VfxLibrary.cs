using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>The effect prefab for each VfxType, used by VfxManager. Built by VfxBuilder.</summary>
[CreateAssetMenu(fileName = "VfxLibrary", menuName = "VFX/Library")]
public class VfxLibrary : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public VfxType Type;
        [Tooltip("Root has a VfxInstance; its particle systems are started on spawn and stopped on release")]
        public GameObject Prefab;
        [Min(0)] public int Prewarm = 4;
        [Tooltip("How hard a one-shot of it shakes the camera when it plays on screen (an impact: meteors, slams), 0 to 1 like a " +
                 "skill's Crit Shake; for any caster, players, enemies and bosses, whether it hits or not. Kept when the library is rebuilt.")]
        [Range(0f, 1f)] public float Shake;
    }

    public List<Entry> Entries = new List<Entry>();
}
