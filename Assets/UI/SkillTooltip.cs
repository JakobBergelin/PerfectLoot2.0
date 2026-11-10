using System;
using ActiveSkills;
using Equipment;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// An active skill's tooltip body, shared by the skills panel and the skill bar: tag chips, cost and cooldown after
/// the caster's reductions (base greyed), damage lines and other effects (ActiveSkill.Describe), then the description.
/// Text takes its font, size and color from the container's style.
/// </summary>
public static class SkillTooltip
{
    const float IconSize = 16f;
    const string MutedColor = "#8A8F99";

    /// <summary>A local player's current stats (player 1's by default), or null without them (e.g. in a menu).</summary>
    public static StatBlock? CurrentStats(int playerIndex = 0)
    {
        var world = World.DefaultGameObjectInjectionWorld;
        var characterManager = CharacterManager.For(playerIndex);
        if (world == null || !world.IsCreated || characterManager == null) return null;
        var entityManager = world.EntityManager;
        var player = characterManager.Player;
        if (player == Entity.Null || !entityManager.Exists(player) || !entityManager.HasComponent<CalculatedStats>(player)) return null;
        return entityManager.GetComponentData<CalculatedStats>(player).Value;
    }

    public static void Fill(VisualElement container, ActiveSkill skill, StatBlock? stats)
    {
        container.Clear();

        // Tag chips
        var chips = Row();
        chips.style.flexWrap = Wrap.Wrap;
        chips.style.marginBottom = 4;
        foreach (SkillTags tag in Enum.GetValues(typeof(SkillTags)))
            if (tag != SkillTags.None && (skill.Tags & tag) != 0) chips.Add(Chip(ItemText.SplitWords(tag.ToString())));
        if (chips.childCount > 0) container.Add(chips);

        // Cost and cooldown, after the caster's reductions
        var resourceIcon = GameIcons.Affix(Affix.Resource);
        if (skill.ResourceCost > 0f)
            container.Add(Line(Reduced(new SkillLine().Icon(resourceIcon).Text("Cost: "), skill.ResourceCost,
                                       stats is StatBlock s ? SkillUtil.Cost(skill.ResourceCost, s, skill.Tags) : (float?)null, "")));
        if (skill.ResourceCostPerSecond > 0f)
            container.Add(Line(Reduced(new SkillLine().Icon(resourceIcon).Text("Cost: "), skill.ResourceCostPerSecond,
                                       stats is StatBlock s ? SkillUtil.Cost(skill.ResourceCostPerSecond, s, skill.Tags) : (float?)null, " per second")));
        if (skill.Cooldown > 0f)
            container.Add(Line(Reduced(new SkillLine().Icon(GameIcons.Stat(StatIcon.Cooldown)).Text("Cooldown: "), skill.Cooldown,
                                       stats is StatBlock s ? skill.CooldownFor(s) : (float?)null, " s")));

        var description = new SkillDescription();
        skill.Describe(description, stats);
        AddGroup(container, description.Damage);
        AddGroup(container, description.Effects);

        if (!string.IsNullOrEmpty(skill.Description))
        {
            var text = Text(skill.Description);
            text.style.marginTop = container.childCount > 0 ? 6 : 0;
            container.Add(text);
        }
    }

    // "7.5 (10)": the value after reductions, base greyed when they differ
    static SkillLine Reduced(SkillLine line, float baseValue, float? current, string unit)
    {
        if (current is float value && !Mathf.Approximately(value, baseValue))
            return line.Text($"{value:0.##}{unit} ").Muted($"({baseValue:0.##}{unit})");
        return line.Text($"{baseValue:0.##}{unit}");
    }

    static void AddGroup(VisualElement container, System.Collections.Generic.List<SkillLine> lines)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            var row = Line(lines[i]);
            if (i == 0 && container.childCount > 0) row.style.marginTop = 4;
            container.Add(row);
        }
    }

    static VisualElement Line(SkillLine line)
    {
        var row = Row();
        row.style.flexWrap = Wrap.Wrap;
        foreach (var piece in line.Pieces)
        {
            if (piece.Icon != null)
            {
                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                icon.style.width = IconSize;
                icon.style.height = IconSize;
                icon.style.flexShrink = 0;
                icon.style.marginLeft = 2;
                icon.style.marginRight = 3;
                icon.style.backgroundImage = new StyleBackground(piece.Icon);
                row.Add(icon);
            }
            else
            {
                var label = Text(piece.Text);
                if (piece.Muted) label.style.color = Muted;
                row.Add(label);
            }
        }
        return row;
    }

    static VisualElement Row()
    {
        var row = new VisualElement { pickingMode = PickingMode.Ignore };
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        return row;
    }

    static Label Chip(string text)
    {
        var chip = Text(text);
        chip.style.fontSize = 11;
        chip.style.color = Muted;
        chip.style.backgroundColor = new Color(1f, 1f, 1f, 0.08f);
        chip.style.borderTopLeftRadius = chip.style.borderTopRightRadius = chip.style.borderBottomLeftRadius = chip.style.borderBottomRightRadius = 3;
        chip.style.paddingLeft = chip.style.paddingRight = 6;
        chip.style.paddingTop = chip.style.paddingBottom = 1;
        chip.style.marginRight = 4;
        chip.style.marginBottom = 2;
        return chip;
    }

    static Label Text(string text)
    {
        var label = new Label(text) { pickingMode = PickingMode.Ignore, enableRichText = true };
        label.style.paddingLeft = label.style.paddingRight = label.style.paddingTop = label.style.paddingBottom = 0;
        label.style.marginLeft = label.style.marginRight = label.style.marginTop = label.style.marginBottom = 0;
        label.style.whiteSpace = WhiteSpace.Normal;
        return label;
    }

    static Color Muted => ColorUtility.TryParseHtmlString(MutedColor, out var color) ? color : Color.gray;
}
