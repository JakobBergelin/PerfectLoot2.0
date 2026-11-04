using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class VfxBuilder
{
    // Unit hit and death effects (enemy phase); none yet
    static IEnumerable<(VfxType, GameObject)> UnitEffects()
    {
        yield break;
    }

    static Mesh SaveMesh(string name, List<Vector3> vertices, List<int> triangles, bool flat = true)
    {
        string path = $"{Root}/Meshes/{name}.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool created = mesh == null;
        if (created) mesh = new Mesh { name = name };
        mesh.Clear();
        if (flat)
        {
            // Split per triangle: hard edges for the chunky comic look
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int i = 0; i < triangles.Count; i++) { v.Add(vertices[triangles[i]]); t.Add(i); }
            vertices = v; triangles = t;
        }
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        if (created) AssetDatabase.CreateAsset(mesh, path);
        else EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>Low-poly sphere (icosahedron subdivided once), radius 0.5.</summary>
    static Mesh Ico(string name)
    {
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        var v = new List<Vector3>
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t),
            new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        };
        var f = new List<int> { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
        var mids = new Dictionary<(int, int), int>();
        int Mid(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            if (mids.TryGetValue(key, out int m)) return m;
            v.Add((v[a] + v[b]) * 0.5f);
            return mids[key] = v.Count - 1;
        }
        var subdivided = new List<int>();
        for (int i = 0; i < f.Count; i += 3)
        {
            int a = f[i], b = f[i + 1], c = f[i + 2], ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
            subdivided.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
        }
        for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized * 0.5f;
        return SaveMesh(name, v, subdivided);
    }

    /// <summary>Barrage bolt: a long diamond pointing along +Z.</summary>
    static Mesh Bolt(string name)
    {
        var v = new List<Vector3> { new(0, 0, 0.45f), new(0, 0, -0.3f), new(0.08f, 0, 0), new(-0.08f, 0, 0), new(0, 0.08f, 0), new(0, -0.08f, 0) };
        var t = new List<int> { 0,4,2, 0,2,5, 0,5,3, 0,3,4, 1,2,4, 1,5,2, 1,3,5, 1,4,3 };
        return SaveMesh(name, v, t);
    }

    /// <summary>Tornado funnel: twisted rings widening upward from a point at the ground, open at the top, both sides.</summary>
    static Mesh Funnel(string name)
    {
        const int levels = 8, around = 10;
        var v = new List<Vector3>();
        var t = new List<int>();
        for (int l = 0; l <= levels; l++)
        {
            float h = l / (float)levels;
            float radius = Mathf.Lerp(0.12f, 0.75f, Mathf.Pow(h, 1.4f));
            float twist = h * 2.2f;
            float lean = Mathf.Sin(h * 3f) * 0.12f;
            for (int a = 0; a < around; a++)
            {
                float angle = a / (float)around * Mathf.PI * 2f + twist;
                v.Add(new Vector3(Mathf.Cos(angle) * radius + lean, h * 2.4f, Mathf.Sin(angle) * radius));
            }
        }
        for (int l = 0; l < levels; l++)
            for (int a = 0; a < around; a++)
            {
                int i0 = l * around + a, i1 = l * around + (a + 1) % around, i2 = i0 + around, i3 = i1 + around;
                t.AddRange(new[] { i0, i2, i1, i1, i2, i3 });   // outside
                t.AddRange(new[] { i0, i1, i2, i1, i3, i2 });   // inside, so the open top shows its far wall
            }
        return SaveMesh(name, v, t);
    }

    /// <summary>Whirlwind blade arc: a flat crescent around the body, thick at its leading edge and thinning behind.</summary>
    static Mesh Arc(string name)
    {
        const int steps = 24;
        var v = new List<Vector3>();
        var t = new List<int>();
        for (int i = 0; i <= steps; i++)
        {
            float s = i / (float)steps;
            float angle = s * Mathf.PI * 1.6f;
            float width = Mathf.Lerp(0.05f, 0.45f, s * s);
            float outer = 1.35f;
            v.Add(new Vector3(Mathf.Cos(angle) * outer, 0f, Mathf.Sin(angle) * outer));
            v.Add(new Vector3(Mathf.Cos(angle) * (outer - width), 0.02f, Mathf.Sin(angle) * (outer - width)));
        }
        for (int i = 0; i < steps; i++)
        {
            int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
            t.AddRange(new[] { a, b, c, b, d, c });
        }
        return SaveMesh(name, v, t, flat: false);
    }
}
