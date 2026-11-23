using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Marks left on the ground for a while: scorch where a fireball lands, frost from a Cold Nova, slime trails. URP decal
/// projectors from a pool, pointing down; they hold, then fade out. Only floor materials take them (PL_Comic's Receive
/// Decals), so units and props never get painted. At most Max at once: the oldest goes first. For the eyes only; every
/// machine places the same marks from what it shows.
/// </summary>
public static class GroundDecals
{
    const int Max = 64;
    const float Reach = 3.5f;   // Meters the projector reaches down from just above the point

    class Mark
    {
        public DecalProjector Projector;
        public float Born, Hold, Fade;
    }

    static readonly List<Mark> _live = new List<Mark>();
    static readonly Stack<DecalProjector> _free = new Stack<DecalProjector>();
    static Runner _runner;

    class Runner : MonoBehaviour
    {
        void Update() => Tick();
    }

    /// <param name="size">Meters across</param>
    /// <param name="hold">Seconds before it starts fading</param>
    /// <param name="fade">Seconds it takes to fade out</param>
    public static void Place(Material material, Vector3 position, float size, float yaw, float hold, float fade)
    {
        if (material == null) return;
        if (_runner == null)
        {
            var go = new GameObject("Ground Decals");
            Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<Runner>();
            _live.Clear();
            _free.Clear();
        }
        if (_live.Count >= Max) Remove(0);
        var projector = _free.Count > 0 ? _free.Pop() : Create();
        projector.transform.SetPositionAndRotation(position + Vector3.up * 0.5f, Quaternion.Euler(90f, yaw, 0f));
        projector.size = new Vector3(size, size, Reach);
        projector.pivot = new Vector3(0f, 0f, Reach * 0.5f);
        projector.material = material;
        projector.fadeFactor = 1f;
        projector.gameObject.SetActive(true);
        _live.Add(new Mark { Projector = projector, Born = Time.time, Hold = hold, Fade = Mathf.Max(fade, 0.01f) });
    }

    static DecalProjector Create()
    {
        var go = new GameObject("Decal");
        go.transform.SetParent(_runner.transform, false);
        var projector = go.AddComponent<DecalProjector>();
        projector.startAngleFade = 50f;     // Floors only, not steep sides
        projector.endAngleFade = 70f;
        return projector;
    }

    static void Remove(int index)
    {
        var mark = _live[index];
        _live.RemoveAt(index);
        if (mark.Projector == null) return;
        mark.Projector.gameObject.SetActive(false);
        _free.Push(mark.Projector);
    }

    static void Tick()
    {
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            var mark = _live[i];
            float age = Time.time - mark.Born - mark.Hold;
            if (age >= mark.Fade || mark.Projector == null) { Remove(i); continue; }
            mark.Projector.fadeFactor = age <= 0f ? 1f : 1f - age / mark.Fade;
        }
    }
}
