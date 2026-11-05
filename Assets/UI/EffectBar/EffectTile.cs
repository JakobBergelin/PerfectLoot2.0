using Effects;
using UnityEngine.UIElements;

/// <summary>One buff or debuff: icon (or short name), time sweep and stacks. Styled by EffectBar.uss.</summary>
public class EffectTile
{
    public readonly EffectDefinition Definition;
    public readonly VisualElement Element = new VisualElement();

    readonly CooldownSweep _sweep = new CooldownSweep { pickingMode = PickingMode.Ignore };
    readonly Label _stacks = new Label { pickingMode = PickingMode.Ignore };
    int _shownStacks = -1;

    // This frame's state
    public bool Active;
    public EffectDisplay Effect;
    public int TotalStacks => Effect.Stacks;
    public float TimeRemaining => Effect.TimeRemaining;
    public float Duration => Effect.Duration;

    public EffectTile(EffectDefinition definition)
    {
        Definition = definition;
        Element.AddToClassList("effect-tile");
        Element.AddToClassList(definition.Kind == EffectKind.Buff ? "effect-tile--buff" : "effect-tile--debuff");

        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
        icon.AddToClassList("effect-tile__icon");
        var fallback = new Label { pickingMode = PickingMode.Ignore };
        fallback.AddToClassList("effect-tile__fallback");
        if (definition.Icon != null) icon.style.backgroundImage = new StyleBackground(definition.Icon);
        else fallback.text = ShortName(definition.DisplayName);

        _sweep.AddToClassList("effect-tile__sweep");
        _stacks.AddToClassList("effect-tile__stacks");

        Element.Add(icon);
        Element.Add(fallback);
        Element.Add(_sweep);
        Element.Add(_stacks);
    }

    /// <summary>Shows this frame's Effect.</summary>
    public void Refresh()
    {
        // Darkens as it runs out; untimed effects have no sweep
        _sweep.Fraction = Duration > 0f ? 1f - TimeRemaining / Duration : 0f;

        if (TotalStacks != _shownStacks)
        {
            _shownStacks = TotalStacks;
            _stacks.text = TotalStacks > 1 ? TotalStacks.ToString() : "";
        }
    }

    static string ShortName(string name) => name.Length <= 3 ? name : name.Substring(0, 3);
}
