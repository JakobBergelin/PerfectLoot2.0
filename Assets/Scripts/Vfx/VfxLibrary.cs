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
    }

    public List<Entry> Entries = new List<Entry>();
}
