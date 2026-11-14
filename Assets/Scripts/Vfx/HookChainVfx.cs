using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hookshot Slash's chain (VfxType.HookChain): chunky steel links from one end to the other, laid again whenever
/// VfxManager.SetEnds moves its ends (every frame while hooking). Links are copies of the inactive LinkTemplate child,
/// made as the chain first needs them and kept for the pooled instance's next use; each one is turned a quarter about the
/// chain from the last, so they read as interlocking. Every LineRenderer under it (a faint glint) draws the same ends.
/// SetEnds also puts the root at the first end, so the effect needs no Move.
/// </summary>
[RequireComponent(typeof(VfxInstance))]
public class HookChainVfx : MonoBehaviour, IVfxEnds
{
    [Tooltip("One chain link, an inactive child, long along its Z: copied along the chain")]
    public Transform LinkTemplate;
    [Tooltip("Meters between link centres (a little under a link's length, so they overlap)")]
    public float Spacing = 0.27f;
    [Tooltip("Most links drawn; a longer chain spaces them further apart")]
    public int MaxLinks = 64;

    readonly List<Transform> _links = new List<Transform>();
    LineRenderer[] _lines;

    void OnEnable()
    {
        // Back from the pool: nothing shows until the first SetEnds
        foreach (var link in _links) link.gameObject.SetActive(false);
        _lines ??= GetComponentsInChildren<LineRenderer>(true);
        foreach (var line in _lines) line.positionCount = 0;
    }

    public void SetEnds(Vector3 from, Vector3 to)
    {
        transform.SetPositionAndRotation(from, Quaternion.identity);
        var along = to - from;
        float length = along.magnitude;
        int count = LinkTemplate == null || length < 1e-3f ? 0 : Mathf.Clamp(Mathf.RoundToInt(length / Spacing), 1, MaxLinks);
        var direction = length > 1e-3f ? along / length : Vector3.forward;
        var look = Quaternion.LookRotation(direction, Mathf.Abs(direction.y) > 0.99f ? Vector3.forward : Vector3.up);

        while (_links.Count < count) _links.Add(Instantiate(LinkTemplate, transform));
        for (int i = 0; i < _links.Count; i++)
        {
            var link = _links[i];
            bool shown = i < count;
            if (link.gameObject.activeSelf != shown) link.gameObject.SetActive(shown);
            if (!shown) continue;
            // Tilted 45 degrees either way, so no link is edge-on to the top-down camera
            link.SetPositionAndRotation(from + along * ((i + 0.5f) / count), look * Quaternion.Euler(0f, 0f, i % 2 == 0 ? 45f : -45f));
        }

        _lines ??= GetComponentsInChildren<LineRenderer>(true);
        foreach (var line in _lines)
        {
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
        }
    }
}
