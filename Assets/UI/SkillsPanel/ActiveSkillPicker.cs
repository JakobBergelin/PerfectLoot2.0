using System;
using System.Collections.Generic;
using ActiveSkills;
using Characters;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The Skills panel's active skill picker: the skill wheel (SkillWheel) on the left; on the right the highlighted
/// skill's card (its requirements against the character's attributes, when its class reaches it, then its tooltip:
/// SkillTooltip) and the other skills on its spoke. Pointer or controller focus highlights a skill, a click or A picks
/// it (Picked), LT/RT go to the next spoke. Skills the character can't use yet can be picked too: they're greyed out
/// until their requirements are met. Styles in SkillsPanel.uss.
/// </summary>
public sealed class ActiveSkillPicker
{
    public readonly VisualElement Element;
    public Action<ActiveSkill> Picked;

    readonly SkillWheel _wheel;
    readonly Label _name, _spoke, _unlock;
    readonly VisualElement _bars, _body, _list;
    readonly Label _listTitle;

    IReadOnlyList<ActiveSkill> _skills = Array.Empty<ActiveSkill>();
    float3 _attributes;
    CharacterClass _class;
    ActiveSkill _highlighted;
    StatBlock? _stats;

    public ActiveSkillPicker()
    {
        Element = new VisualElement();
        Element.AddToClassList("sw-picker");

        _wheel = new SkillWheel();
        _wheel.Highlighted += Highlight;
        _wheel.Picked += skill => Picked?.Invoke(skill);
        Element.Add(_wheel);

        var side = new VisualElement();
        side.AddToClassList("sw-side");
        var card = new VisualElement();
        card.AddToClassList("sw-card");
        _name = NewLabel("sw-card__name");
        _spoke = NewLabel("sw-card__spoke");
        _bars = new VisualElement();
        _bars.AddToClassList("sw-bars");
        _unlock = NewLabel("sw-card__unlock");
        _body = new VisualElement();
        _body.AddToClassList("sw-card__body");
        card.Add(_name);
        card.Add(_spoke);
        card.Add(_bars);
        card.Add(_unlock);
        card.Add(_body);

        var list = new VisualElement();
        list.AddToClassList("sw-list");
        _listTitle = NewLabel("sw-list__title");
        _list = new VisualElement();
        list.Add(_listTitle);
        list.Add(_list);

        side.Add(card);
        side.Add(list);
        Element.Add(side);
    }

    /// <summary>The element a controller starts on: the node of the slot's current skill (or the first).</summary>
    public VisualElement StartNode { get; private set; }

    /// <summary>
    /// Shows the skills for a slot. current: its skill now; badge: another slot's key for a skill equipped there, or
    /// null. attributes: the character's Strength, Agility and Intelligence now; characterClass for "reached at level".
    /// stats: for the tooltip, or null.
    /// </summary>
    public void Open(IReadOnlyList<ActiveSkill> skills, ActiveSkill current, Func<ActiveSkill, string> badge, float3 attributes,
                     CharacterClass characterClass, StatBlock? stats)
    {
        _skills = skills ?? Array.Empty<ActiveSkill>();
        _attributes = attributes;
        _class = characterClass;
        _stats = stats;
        _wheel.Show(_skills, Usable, badge, current);

        var start = current;
        if (start == null)
            foreach (var skill in _skills)
                if (skill != null) { start = skill; break; }
        StartNode = _wheel.NodeOf(start);
        Highlight(start);
    }

    /// <summary>LT/RT: the first skill on the next (1) or previous (-1) spoke that has any; returns its node to focus.</summary>
    public VisualElement TurnSpoke(int step)
    {
        int from = _highlighted != null ? SkillWheel.SpokeOf(_highlighted) : 0;
        for (int i = 1; i <= SkillWheel.Spokes.Length; i++)
        {
            int spoke = ((from + step * i) % SkillWheel.Spokes.Length + SkillWheel.Spokes.Length) % SkillWheel.Spokes.Length;
            var first = OnSpoke(spoke);
            if (first.Count == 0) continue;
            Highlight(first[0]);
            return _wheel.NodeOf(first[0]);
        }
        return null;
    }

    bool Usable(ActiveSkill skill) => SkillRequirements.Meets(skill.Requirements, _attributes);

    List<ActiveSkill> OnSpoke(int spoke)
    {
        var skills = new List<ActiveSkill>();
        foreach (var skill in _skills)
            if (skill != null && SkillWheel.SpokeOf(skill) == spoke) skills.Add(skill);
        skills.Sort((a, b) => SkillRequirements.Total(a.Requirements).CompareTo(SkillRequirements.Total(b.Requirements)));
        return skills;
    }

    // ---------- The highlighted skill ----------

    void Highlight(ActiveSkill skill)
    {
        _highlighted = skill;
        if (skill == null) return;
        var requirements = skill.Requirements;
        int spoke = SkillWheel.SpokeOf(skill);
        _name.text = string.IsNullOrEmpty(skill.Name) ? skill.name : skill.Name;
        _spoke.text = $"{SkillWheel.Spokes[spoke].Name} · total {SkillRequirements.Total(requirements)}";

        _bars.Clear();
        for (int i = 0; i < 3; i++)
            if (requirements[i] > 0) _bars.Add(Bar(SkillRequirements.Names[i], requirements[i], _attributes[i]));
        if (_bars.childCount == 0) _bars.Add(NewLabel("sw-card__unlock", "No requirements"));

        _unlock.text = UnlockText(requirements);
        SkillTooltip.Fill(_body, skill, _stats);
        ShowList(spoke);
    }

    VisualElement Bar(string attribute, int required, float have)
    {
        bool met = have >= required;
        var row = new VisualElement();
        row.AddToClassList("sw-bar");
        row.AddToClassList(met ? "sw-bar--met" : "sw-bar--short");
        var text = new VisualElement();
        text.AddToClassList("sw-bar__text");
        text.Add(NewLabel("sw-bar__label", $"{attribute} {required}"));
        text.Add(NewLabel("sw-bar__have", $"you {Mathf.FloorToInt(have)}"));
        var track = new VisualElement();
        track.AddToClassList("sw-bar__track");
        var fill = new VisualElement();
        fill.AddToClassList("sw-bar__fill");
        fill.style.width = Length.Percent(Mathf.Clamp01(have / required) * 100f);
        track.Add(fill);
        row.Add(text);
        row.Add(track);
        return row;
    }

    string UnlockText(int3 requirements)
    {
        if (SkillRequirements.Meets(requirements, _attributes)) return "Usable now";
        if (_class == null) return "Needs more attributes";
        string className = string.IsNullOrWhiteSpace(_class.Name) ? _class.name : _class.Name;
        int level = SkillRequirements.UnlockLevel(requirements, _class);
        if (level == int.MaxValue) return $"A {className} never reaches it alone: it needs items";
        return level <= Experience.MaxLevel ? $"A {className} reaches it at level {level}" : $"Needs items: a {className} would need level {level}";
    }

    // The skills on the highlighted one's spoke; pointing at a row highlights it, a click picks it
    void ShowList(int spoke)
    {
        _listTitle.text = spoke == SkillWheel.Spokes.Length - 1 ? "All three (the middle)" : $"{SkillWheel.Spokes[spoke].Name} spoke";
        _list.Clear();
        foreach (var skill in OnSpoke(spoke))
        {
            var row = new VisualElement();
            row.AddToClassList("sw-row");
            row.AddToClassList(Usable(skill) ? "sw-row--usable" : "sw-row--locked");
            if (skill == _highlighted) row.AddToClassList("sw-row--highlighted");
            row.Add(NewLabel("sw-row__name", string.IsNullOrEmpty(skill.Name) ? skill.name : skill.Name));
            row.Add(NewLabel("sw-row__requirements", RequirementsText(skill.Requirements)));
            var picked = skill;
            row.RegisterCallback<ClickEvent>(_ => Picked?.Invoke(picked));
            _list.Add(row);
        }
    }

    // "Str 3 · Agi 1", each green or red
    string RequirementsText(int3 requirements)
    {
        var parts = new List<string>();
        for (int i = 0; i < 3; i++)
            if (requirements[i] > 0)
                parts.Add($"<color={(_attributes[i] >= requirements[i] ? "#7FD07F" : "#E05050")}>{SkillRequirements.Short[i]} {requirements[i]}</color>");
        return parts.Count > 0 ? string.Join(" · ", parts) : "None";
    }

    static Label NewLabel(string className, string text = "")
    {
        var label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.AddToClassList(className);
        return label;
    }
}
