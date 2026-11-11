using System;
using System.Collections.Generic;
using ActiveSkills;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The active skills on a wheel by their requirements (SkillRequirements). Six spokes outside the middle circle:
/// Strength, Agility and Intelligence, and between them the skills that need two of them; the further out, the more
/// they need in total, on a log scale so the many low ones get room (rings at 5, 10, 20 and 30). Skills that need all
/// three sit inside the middle circle, placed by which way they lean. Skills that would overlap move sideways within
/// their spoke's sector, then outward. Each skill is a node with its icon and name: usable ones lit, the others greyed,
/// equipped ones with their slot's key. A green fill shows the character's attributes: on a one-attribute spoke out to
/// its value, on a two-attribute spoke up to the first skill they can't use yet. Pointer or controller focus highlights
/// a node, a click or A picks it. Always the same size; nothing moves when the highlight changes.
/// </summary>
public sealed class SkillWheel : VisualElement
{
    public const float Size = 640f;
    const float Center = Size * 0.5f;
    const float MiddleRadius = 72f;     // The circle the all-three skills sit in
    const float RingStart = MiddleRadius + 22f;
    const float MaxRadius = 276f;       // Where a total of MaxTotal lands; beyond it they stop here
    const float MaxTotal = 32f;
    const float LabelRadius = 304f;     // The attribute names, outside every skill
    const float RingLabelDegrees = -60f;   // Ring totals go between two spokes, where no skill sits
    const float Lean = 2.2f;            // How much an all-three skill's lean is exaggerated, so small leans show
    const float NodeSize = 40f;
    const float MinGap = 60f;           // Centres closer than this overlap (the node and its name under it)
    const float SectorHalf = 27f;       // Degrees a skill may move to either side of its spoke
    const float FillWidth = 10f;

    static readonly float[] Directions = { -90f, 150f, 30f };     // Strength up, Agility lower left, Intelligence lower right
    static readonly int[] Rings = { 5, 10, 20, 30 };
    static readonly Color Green = new Color(0.11f, 0.62f, 0.46f, 0.45f);

    /// <summary>A spoke: which attributes its skills need (x Str, y Agi, z Int: 1 when needed), its name and direction.</summary>
    public readonly struct Spoke
    {
        public readonly int3 Mask;
        public readonly string Name;
        public readonly float Degrees;      // NaN for the middle (all three)

        public Spoke(int3 mask, string name, float degrees)
        {
            Mask = mask;
            Name = name;
            Degrees = degrees;
        }
    }

    /// <summary>In the order LT/RT go through them: around the wheel, then the middle.</summary>
    public static readonly Spoke[] Spokes =
    {
        new Spoke(new int3(1, 0, 0), "Strength", -90f),
        new Spoke(new int3(1, 1, 0), "Strength + Agility", -150f),
        new Spoke(new int3(0, 1, 0), "Agility", 150f),
        new Spoke(new int3(0, 1, 1), "Agility + Intelligence", 90f),
        new Spoke(new int3(0, 0, 1), "Intelligence", 30f),
        new Spoke(new int3(1, 0, 1), "Intelligence + Strength", -30f),
        new Spoke(new int3(1, 1, 1), "All three", float.NaN),
    };

    /// <summary>The spoke a skill sits on (a skill without requirements counts as needing all three: the middle).</summary>
    public static int SpokeOf(ActiveSkill skill)
    {
        var mask = math.select(int3.zero, new int3(1), skill.Requirements > 0);
        if (math.all(mask == 0)) mask = new int3(1);
        return Array.FindIndex(Spokes, spoke => math.all(spoke.Mask == mask));
    }

    public event Action<ActiveSkill> Highlighted;
    public event Action<ActiveSkill> Picked;

    readonly VisualElement _nodes;
    readonly Dictionary<ActiveSkill, VisualElement> _nodeOf = new Dictionary<ActiveSkill, VisualElement>();
    readonly float[] _fills = new float[6];     // Per outer spoke: how far out the green fill reaches (0: none)

    public SkillWheel()
    {
        AddToClassList("sw-wheel");
        style.width = Size;
        style.height = Size;
        generateVisualContent += Draw;

        // Attribute names past the ends of their spokes, ring totals between two spokes
        for (int i = 0; i < 3; i++)
            AddLabel(SkillRequirements.Names[i], Point(Directions[i], LabelRadius), "sw-attribute");
        foreach (int total in Rings)
            AddLabel(total == 30 ? "30 · level 10" : total.ToString(), Point(RingLabelDegrees, RadiusOf(total)), "sw-ring-label");

        _nodes = new VisualElement { pickingMode = PickingMode.Ignore };
        _nodes.style.position = Position.Absolute;
        _nodes.style.left = 0;
        _nodes.style.top = 0;
        _nodes.style.right = 0;
        _nodes.style.bottom = 0;
        Add(_nodes);
    }

    /// <summary>The node of a skill on the wheel, or null.</summary>
    public VisualElement NodeOf(ActiveSkill skill) => skill != null && _nodeOf.TryGetValue(skill, out var node) ? node : null;

    /// <summary>Marks the skill waiting for a slot (null: none).</summary>
    public void SetPending(ActiveSkill skill)
    {
        foreach (var pair in _nodeOf) pair.Value.EnableInClassList("sw-node--pending", pair.Key == skill);
    }

    /// <summary>
    /// Places the skills. usable: whether the character can use one (lit, else greyed); badge: the key of the slot it's
    /// equipped in, or null. attributes: the character's Strength, Agility and Intelligence, for the green fill.
    /// </summary>
    public void Show(IReadOnlyList<ActiveSkill> skills, Func<ActiveSkill, bool> usable, Func<ActiveSkill, string> badge, float3 attributes)
    {
        _nodes.Clear();
        _nodeOf.Clear();
        var placed = new List<Vector2>();
        for (int spoke = 0; spoke < Spokes.Length; spoke++)
        {
            var onSpoke = new List<ActiveSkill>();
            foreach (var skill in skills)
                if (skill != null && SpokeOf(skill) == spoke) onSpoke.Add(skill);
            onSpoke.Sort((a, b) => SkillRequirements.Total(a.Requirements).CompareTo(SkillRequirements.Total(b.Requirements)));

            bool middle = float.IsNaN(Spokes[spoke].Degrees);
            float fill = 0f;
            bool blocked = false;
            foreach (var skill in onSpoke)
            {
                var position = middle ? PlaceFree(MiddlePoint(skill.Requirements), placed)
                                      : PlaceOnSpoke(Spokes[spoke].Degrees, RadiusOf(SkillRequirements.Total(skill.Requirements)), placed);
                placed.Add(position);
                bool canUse = usable(skill);
                AddNode(skill, position, canUse, badge(skill));

                // Two-attribute spokes: the fill reaches past every usable skill up to the first one that isn't
                if (middle || blocked) continue;
                float distance = (position - new Vector2(Center, Center)).magnitude;
                if (canUse) fill = distance + NodeSize * 0.6f;
                else
                {
                    blocked = true;
                    fill = fill > 0f ? Mathf.Min(fill, distance - NodeSize * 0.7f) : 0f;
                }
            }
            if (middle) continue;

            // One-attribute spokes: out to the attribute itself
            var mask = Spokes[spoke].Mask;
            if (mask.x + mask.y + mask.z == 1)
            {
                float value = math.csum(attributes * (float3)mask);
                fill = value > 0f ? RadiusOf(value) + NodeSize * 0.5f : 0f;
            }
            else if (!blocked && onSpoke.Count > 0) fill = MaxRadius;
            _fills[spoke] = Mathf.Clamp(fill, 0f, MaxRadius);
        }
        MarkDirtyRepaint();
    }

    void AddNode(ActiveSkill skill, Vector2 position, bool usable, string badge)
    {
        var node = new VisualElement();
        node.AddToClassList("sw-node");
        node.AddToClassList(usable ? "sw-node--usable" : "sw-node--locked");
        node.style.left = position.x - NodeSize * 0.5f;
        node.style.top = position.y - NodeSize * 0.5f;

        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
        icon.AddToClassList("sw-node__icon");
        if (skill.Icon != null) icon.style.backgroundImage = new StyleBackground(skill.Icon);
        node.Add(icon);

        var name = new Label(string.IsNullOrEmpty(skill.Name) ? skill.name : skill.Name) { pickingMode = PickingMode.Ignore };
        name.AddToClassList("sw-node__name");
        node.Add(name);

        if (!string.IsNullOrEmpty(badge))
        {
            node.AddToClassList("sw-node--equipped");
            var key = new Label(badge) { pickingMode = PickingMode.Ignore };
            key.AddToClassList("sw-node__badge");
            node.Add(key);
        }

        node.RegisterCallback<ClickEvent>(_ => Picked?.Invoke(skill));
        UiNav.Clickable(node, () => Picked?.Invoke(skill));
        UiNav.Hover(node, () =>
        {
            foreach (var other in _nodeOf.Values) other.RemoveFromClassList("sw-node--highlighted");
            node.AddToClassList("sw-node--highlighted");
            Highlighted?.Invoke(skill);
        }, () => { });
        _nodes.Add(node);
        _nodeOf[skill] = node;
    }

    // ---------- Layout ----------

    // Log scale: the low totals, where most skills are, get the most room
    static float RadiusOf(float total) =>
        RingStart + (MaxRadius - RingStart) * Mathf.Min(Mathf.Log(1f + Mathf.Max(0f, total)) / Mathf.Log(1f + MaxTotal), 1f);

    static Vector2 Point(float degrees, float radius)
    {
        float radians = degrees * Mathf.Deg2Rad;
        return new Vector2(Center + Mathf.Cos(radians) * radius, Center + Mathf.Sin(radians) * radius);
    }

    // On its spoke if that's free; else the nearest free spot to either side within the sector, then a little further out
    static Vector2 PlaceOnSpoke(float degrees, float radius, List<Vector2> placed)
    {
        for (float r = radius; r <= MaxRadius + MinGap; r += MinGap * 0.4f)
        {
            float step = MinGap / r * Mathf.Rad2Deg * 0.75f;
            for (int k = 0; k * step <= SectorHalf; k++)
            {
                foreach (float side in k == 0 ? new[] { 0f } : new[] { 1f, -1f })
                {
                    var candidate = Point(degrees + side * k * step, r);
                    if (IsFree(candidate, placed)) return candidate;
                }
            }
        }
        return Point(degrees, radius);
    }

    // Inside the middle: the nearest free spot, searched in growing circles around where it leans
    static Vector2 PlaceFree(Vector2 position, List<Vector2> placed)
    {
        if (IsFree(position, placed)) return position;
        for (float distance = MinGap * 0.5f; distance <= MinGap * 2f; distance += MinGap * 0.25f)
            for (int i = 0; i < 12; i++)
            {
                float radians = i * 30f * Mathf.Deg2Rad;
                var candidate = position + new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * distance;
                if (IsFree(candidate, placed)) return candidate;
            }
        return position;
    }

    static bool IsFree(Vector2 position, List<Vector2> placed)
    {
        foreach (var other in placed)
            if ((other - position).sqrMagnitude < MinGap * MinGap) return false;
        return true;
    }

    // Each attribute pulls toward its direction by its share of the total; the lean is exaggerated so small ones show
    static Vector2 MiddlePoint(int3 requirements)
    {
        float total = SkillRequirements.Total(requirements);
        if (total <= 0f) return new Vector2(Center, Center);
        var pull = Vector2.zero;
        for (int i = 0; i < 3; i++)
        {
            float radians = Directions[i] * Mathf.Deg2Rad;
            pull += new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * (requirements[i] / total);
        }
        float length = pull.magnitude;
        if (length < 1e-4f) return new Vector2(Center, Center);
        return new Vector2(Center, Center) + pull / length * Mathf.Min(length * Lean, 0.7f) * MiddleRadius;
    }

    void AddLabel(string text, Vector2 position, string className)
    {
        var label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.AddToClassList(className);
        label.style.position = Position.Absolute;
        label.style.left = position.x;
        label.style.top = position.y;
        label.style.translate = new Translate(Length.Percent(-50f), Length.Percent(-50f));
        Add(label);
    }

    // The green fills, then the rings, the middle circle and the spokes
    void Draw(MeshGenerationContext context)
    {
        var painter = context.painter2D;
        var center = new Vector2(Center, Center);
        var faint = new Color(0.66f, 0.68f, 0.72f, 0.25f);
        var line = new Color(0.66f, 0.68f, 0.72f, 0.55f);

        painter.lineWidth = FillWidth;
        painter.lineCap = LineCap.Round;
        painter.strokeColor = Green;
        for (int i = 0; i < 6; i++)
        {
            if (_fills[i] <= MiddleRadius) continue;
            painter.BeginPath();
            painter.MoveTo(Point(Spokes[i].Degrees, MiddleRadius));
            painter.LineTo(Point(Spokes[i].Degrees, _fills[i]));
            painter.Stroke();
        }
        painter.lineCap = LineCap.Butt;

        painter.lineWidth = 1f;
        foreach (int total in Rings)
            if (total != 30) Circle(painter, center, RadiusOf(total), faint);
        painter.lineWidth = 1.5f;
        Circle(painter, center, RadiusOf(30), line);
        Circle(painter, center, MiddleRadius, line);

        painter.lineWidth = 1f;
        painter.strokeColor = faint;
        foreach (var spoke in Spokes)
        {
            if (float.IsNaN(spoke.Degrees)) continue;
            painter.BeginPath();
            painter.MoveTo(Point(spoke.Degrees, MiddleRadius));
            painter.LineTo(Point(spoke.Degrees, MaxRadius));
            painter.Stroke();
        }
        // Inside the middle: faint lines toward the three attributes
        foreach (float degrees in Directions)
        {
            painter.BeginPath();
            painter.MoveTo(center);
            painter.LineTo(Point(degrees, MiddleRadius));
            painter.Stroke();
        }
    }

    static void Circle(Painter2D painter, Vector2 center, float radius, Color color)
    {
        painter.strokeColor = color;
        painter.BeginPath();
        painter.Arc(center, radius, Angle.Degrees(0f), Angle.Degrees(360f));
        painter.Stroke();
    }
}
