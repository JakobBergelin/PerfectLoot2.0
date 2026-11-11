using System;
using System.Collections.Generic;
using ActiveSkills;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The active skills on a wheel by their requirements (SkillRequirements). Six spokes outside the middle circle:
/// Strength, Agility and Intelligence, and between them the skills that need two of them; the further out, the more
/// they need in total (rings at 10, 20 and 30, what a class has at level 10). Skills that need all three sit inside
/// the middle circle, placed only by which way they lean (3/3/3 in the exact middle). Each skill is a node with its
/// icon: usable ones lit, the others greyed. Pointer or controller focus on a node highlights it, a click or A picks
/// it. Drawn with the Painter2D; ActiveSkillPicker puts it next to the highlighted skill's details.
/// </summary>
public sealed class SkillWheel : VisualElement
{
    public const float Size = 400f;
    const float Center = Size * 0.5f;
    const float MiddleRadius = 58f;     // The circle the all-three skills sit in
    const float RingStart = MiddleRadius + 10f;
    const float PerPoint = 3.3f;        // Pixels out per point of total requirement (30 lands well inside the edge)
    const float MaxRadius = 184f;       // Totals beyond about 35 stop here
    const float LabelRadius = 198f;     // The attribute names, outside every skill
    const float RingLabelDegrees = -60f;   // Ring totals go between two spokes, where no skill sits
    const float Lean = 2.2f;            // How much an all-three skill's lean is exaggerated, so small leans show
    const float NodeSize = 30f;
    const float SpreadDegrees = 10f;    // Skills on one spoke with close totals sit to either side of it
    const float MinGap = 26f;           // All-three skills closer than this are nudged apart

    static readonly float[] Directions = { -90f, 150f, 30f };     // Strength up, Agility lower left, Intelligence lower right

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

    public SkillWheel()
    {
        AddToClassList("sw-wheel");
        style.width = Size;
        style.height = Size;
        generateVisualContent += Draw;

        // Attribute names past the ends of their spokes, ring totals between two spokes
        for (int i = 0; i < 3; i++)
            AddLabel(SkillRequirements.Names[i], Point(Directions[i], LabelRadius), "sw-attribute");
        foreach (int total in new[] { 10, 20, 30 })
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

    /// <summary>
    /// Places the skills. usable: whether the character can use one (lit, else greyed); badge: the key of another slot
    /// it's equipped in, or null; current: the skill in the slot being changed (marked).
    /// </summary>
    public void Show(IReadOnlyList<ActiveSkill> skills, Func<ActiveSkill, bool> usable, Func<ActiveSkill, string> badge, ActiveSkill current)
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

            int lastTotal = int.MinValue;
            float side = 1f;
            foreach (var skill in onSpoke)
            {
                Vector2 position;
                if (float.IsNaN(Spokes[spoke].Degrees)) position = Nudge(MiddlePoint(skill.Requirements), placed);
                else
                {
                    // Close totals on one spoke: to either side of it, so they don't overlap
                    int total = SkillRequirements.Total(skill.Requirements);
                    float offset = 0f;
                    if (total - lastTotal < 4)
                    {
                        offset = side * SpreadDegrees;
                        side = -side;
                    }
                    else side = 1f;
                    lastTotal = total;
                    position = Point(Spokes[spoke].Degrees + offset, RadiusOf(total));
                }
                placed.Add(position);
                AddNode(skill, position, usable(skill), badge(skill), skill == current);
            }
        }
    }

    void AddNode(ActiveSkill skill, Vector2 position, bool usable, string badge, bool current)
    {
        var node = new VisualElement();
        node.AddToClassList("sw-node");
        node.AddToClassList(usable ? "sw-node--usable" : "sw-node--locked");
        if (current) node.AddToClassList("sw-node--current");
        node.style.left = position.x - NodeSize * 0.5f;
        node.style.top = position.y - NodeSize * 0.5f;
        if (skill.Icon != null) node.style.backgroundImage = new StyleBackground(skill.Icon);
        else
        {
            var name = new Label(string.IsNullOrEmpty(skill.Name) ? skill.name : skill.Name) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("sw-node__name");
            node.Add(name);
        }
        if (!string.IsNullOrEmpty(badge))
        {
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

    static float RadiusOf(int total) => Mathf.Min(RingStart + total * PerPoint, MaxRadius);

    static Vector2 Point(float degrees, float radius)
    {
        float radians = degrees * Mathf.Deg2Rad;
        return new Vector2(Center + Mathf.Cos(radians) * radius, Center + Mathf.Sin(radians) * radius);
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
        return new Vector2(Center, Center) + pull / length * Mathf.Min(length * Lean, 0.85f) * MiddleRadius;
    }

    // Skills that lean the same way: moved slightly apart
    static Vector2 Nudge(Vector2 position, List<Vector2> placed)
    {
        for (int tries = 0; tries < 8; tries++)
        {
            bool clear = true;
            foreach (var other in placed)
                if ((other - position).magnitude < MinGap) clear = false;
            if (clear) break;
            position.x += MinGap * 0.6f;
        }
        return position;
    }

    void AddLabel(string text, Vector2 position, string className, bool centered = true)
    {
        var label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.AddToClassList(className);
        label.style.position = Position.Absolute;
        label.style.left = position.x;
        label.style.top = position.y;
        if (centered) label.style.translate = new Translate(Length.Percent(-50f), Length.Percent(-50f));
        Add(label);
    }

    // Rings, the middle circle and the spokes
    void Draw(MeshGenerationContext context)
    {
        var painter = context.painter2D;
        var center = new Vector2(Center, Center);
        var faint = new Color(0.66f, 0.68f, 0.72f, 0.25f);
        var line = new Color(0.66f, 0.68f, 0.72f, 0.55f);

        painter.lineWidth = 1f;
        foreach (int total in new[] { 10, 20 })
            Circle(painter, center, RadiusOf(total), faint);
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
