using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pools the effect GameObjects in VfxLibrary. ECS systems (VfxSystem) spawn an effect, move it with the entity it
/// belongs to and release it when the entity goes away; one-shots (impacts, hits) are spawned and released at once.
/// Lives in the main scene, outside the Entities subscene.
/// </summary>
public class VfxManager : MonoBehaviour
{
    public static VfxManager Instance;
    public VfxLibrary Library;

    class Active
    {
        public VfxType Type;
        public VfxInstance Instance;
        public float ReturnAt = -1f;  // < 0: still following its entity
    }

    readonly Dictionary<VfxType, GameObject> _prefabs = new Dictionary<VfxType, GameObject>();
    readonly Dictionary<VfxType, Stack<VfxInstance>> _pools = new Dictionary<VfxType, Stack<VfxInstance>>();
    readonly Dictionary<int, Active> _active = new Dictionary<int, Active>();
    readonly List<int> _returning = new List<int>();
    int _nextHandle = 1;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (Library == null) return;
        foreach (var entry in Library.Entries)
        {
            if (entry.Prefab == null) continue;
            _prefabs[entry.Type] = entry.Prefab;
            var pool = _pools[entry.Type] = new Stack<VfxInstance>();
            for (int i = 0; i < entry.Prewarm; i++) pool.Push(Create(entry.Type));
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    VfxInstance Create(VfxType type)
    {
        var go = Instantiate(_prefabs[type], transform);
        go.SetActive(false);
        var instance = go.GetComponent<VfxInstance>();
        return instance != null ? instance : go.AddComponent<VfxInstance>();
    }

    /// <summary>Starts an effect; returns its handle (0 when the type has no prefab).</summary>
    public int Spawn(VfxType type, Vector3 position, Quaternion rotation)
    {
        if (!_prefabs.ContainsKey(type)) return 0;
        var pool = _pools[type];
        var instance = pool.Count > 0 ? pool.Pop() : Create(type);
        instance.transform.SetPositionAndRotation(position, rotation);
        instance.gameObject.SetActive(true);
        instance.Begin();
        int handle = _nextHandle++;
        _active[handle] = new Active { Type = type, Instance = instance };
        return handle;
    }

    /// <summary>
    /// A one-shot: plays out and returns to the pool after its Linger. Not stopped like Release does: a burst only emits
    /// on the particle system's first update, so stopping it in the frame it starts would cancel it.
    /// </summary>
    public void Burst(VfxType type, Vector3 position, Quaternion rotation)
    {
        int handle = Spawn(type, position, rotation);
        if (_active.TryGetValue(handle, out var active)) active.ReturnAt = Time.time + active.Instance.Linger;
    }

    public void Move(int handle, Vector3 position, Quaternion rotation)
    {
        if (_active.TryGetValue(handle, out var active) && active.ReturnAt < 0f)
            active.Instance.transform.SetPositionAndRotation(position, rotation);
    }

    /// <summary>Stops emitting; the effect returns to the pool once its particles have faded.</summary>
    public void Release(int handle)
    {
        if (!_active.TryGetValue(handle, out var active) || active.ReturnAt >= 0f) return;
        active.Instance.End();
        active.ReturnAt = Time.time + active.Instance.Linger;
    }

    void Update()
    {
        _returning.Clear();
        foreach (var (handle, active) in _active)
            if (active.ReturnAt >= 0f && Time.time >= active.ReturnAt) _returning.Add(handle);
        foreach (var handle in _returning)
        {
            var active = _active[handle];
            _active.Remove(handle);
            active.Instance.gameObject.SetActive(false);
            _pools[active.Type].Push(active.Instance);
        }
    }
}
