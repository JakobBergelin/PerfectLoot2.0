using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Clock-style cooldown overlay: a dark pie that starts full at 12 o'clock and recedes clockwise
/// as Fraction goes from 1 (just used) to 0 (ready). Color via the USS custom property --sweep-color.
/// </summary>
[UxmlElement]
public partial class CooldownSweep : VisualElement
{
    static readonly CustomStyleProperty<Color> s_SweepColor = new CustomStyleProperty<Color>("--sweep-color");

    float m_Fraction;
    Color m_Color = new Color(0f, 0f, 0f, 0.6f);

    /// <summary>Remaining part of the cooldown, 0..1.</summary>
    [UxmlAttribute]
    public float Fraction
    {
        get => m_Fraction;
        set
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(value, m_Fraction)) return;
            m_Fraction = value;
            MarkDirtyRepaint();
        }
    }

    public CooldownSweep()
    {
        generateVisualContent += OnGenerateVisualContent;
        RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
    }

    void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
    {
        if (evt.customStyle.TryGetValue(s_SweepColor, out var color))
        {
            m_Color = color;
            MarkDirtyRepaint();
        }
    }

    void OnGenerateVisualContent(MeshGenerationContext ctx)
    {
        float w = contentRect.width;
        float h = contentRect.height;
        if (w < 1f || h < 1f || m_Fraction <= 0f) return;

        var center = new Vector2(w * 0.5f, h * 0.5f);
        // Reach the corners; the slot's overflow: hidden clips it to the rounded square
        float radius = Mathf.Sqrt(w * w + h * h) * 0.5f;

        var painter = ctx.painter2D;
        painter.fillColor = m_Color;
        painter.BeginPath();
        painter.MoveTo(center);
        if (m_Fraction >= 1f)
        {
            painter.Arc(center, radius, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
        }
        else
        {
            // 12 o'clock is -90 degrees; the elapsed part (lit) grows clockwise from there
            float start = -90f + 360f * (1f - m_Fraction);
            painter.Arc(center, radius, Angle.Degrees(start), Angle.Degrees(270f), ArcDirection.Clockwise);
        }
        painter.ClosePath();
        painter.Fill();
    }
}
