using System;
using System.Collections.Generic;
using ActiveSkills;
using Characters;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The Skills panel's skill wheel (SkillWheel) and the highlighted skill's card beside it: its requirements against
/// the character's attributes, when its class reaches it, then its tooltip (SkillTooltip). The card has a fixed size and
/// scrolls, so nothing moves when the highlight changes. Pointer or controller focus highlights a skill, a click or A
/// picks it (Picked), LT/RT go to the next spoke. Styles in SkillsPanel.uss.
/// </summary>
public sealed class ActiveSkillPicker
{
    public readonly SkillWheel Wheel;
    public readonly VisualElement Card;
    public Action<ActiveSkill> Picked;

    readonly Label _name, _spoke, _unlock, _status;
    readonly VisualElement _bars, _body;

    IReadOnlyList<ActiveSkill> _skills = Array.Empty<ActiveSkill>();
    float3 _attributes;
    CharacterClass _class;
    ActiveSkill _highlighted;
    StatBlock? _stats;

    public ActiveSkillPicker()
    {
        Wheel = new SkillWheel();
        Wheel.Highlighted += Highlight;
        Wheel.Picked += skill => Picked?.Invoke(skill);

        Card = new VisualElement();
        Card.AddToClassList("sw-card");
        _name = NewLabel("sw-card__name");
        _spoke = NewLabel("sw-card__spoke");
        _status = NewLabel("sw-card__status");
        _bars = new VisualElement();
        _bars.AddToClassList("sw-bars");
        _unlock = NewLabel("sw-card__unlock");
        var scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.AddToClassList("sw-card__scroll");
        scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
        scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        _body = new VisualElement();
        _body.AddToClassList("sw-card__body");
        scroll.Add(_body);
        Card.Add(_name);
        Card.Add(_spoke);
        Card.Add(_status);
        Card.Add(_bars);
        Card.Add(_unlock);
        Card.Add(scroll);
    }

    /// <summary>The highlighted skill, or null.</summary>
    public ActiveSkill Highlighted => _highlighted;

    /// <summary>
    /// Shows every skill. badge: the key of the slot a skill is equipped in, or null. attributes: the character's
    /// Strength, Agility and Intelligence now; characterClass for "reached at level". stats: for the tooltip, or null.
    /// Keeps the highlighted skill.
    /// </summary>
    public void Show(IReadOnlyList<ActiveSkill> skills, Func<ActiveSkill, string> badge, float3 attributes, CharacterClass characterClass, StatBlock? stats)
    {
        _skills = skills ?? Array.Empty<ActiveSkill>();
        _attributes = attributes;
        _class = characterClass;
        _stats = stats;
        Wheel.Show(_skills, Usable, badge, attributes);

        var start = _highlighted;
        if (start == null || !Contains(start))
            foreach (var skill in _skills)
                if (skill != null) { start = skill; break; }
        Highlight(start);
    }

    /// <summary>The node of a skill on the wheel, or null.</summary>
    public VisualElement NodeOf(ActiveSkill skill) => Wheel.NodeOf(skill);

    /// <summary>A line under the name, e.g. "Choose a slot"; empty hides it.</summary>
    public void SetStatus(string text)
    {
        _status.text = text;
        _status.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
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
            return Wheel.NodeOf(first[0]);
        }
        return null;
    }

    bool Usable(ActiveSkill skill) => SkillRequirements.Meets(skill.Requirements, _attributes);

    bool Contains(ActiveSkill skill)
    {
        foreach (var other in _skills) if (other == skill) return true;
        return false;
    }

    List<ActiveSkill> OnSpoke(int spoke)
    {
        var skills = new List<ActiveSkill>();
        foreach (var skill in _skills)
            if (skill != null && SkillWheel.SpokeOf(skill) == spoke) skills.Add(skill);
        skills.Sort((a, b) => SkillRequirements.Total(a.Requirements).CompareTo(SkillRequirements.Total(b.Requirements)));
        return skills;
    }

    // ---------- The highlighted skill ----------

    /// <summary>Shows a skill on the card (also for a slot pointed at).</summary>
    public void Highlight(ActiveSkill skill)
    {
        _highlighted = skill;
        if (skill == null)
        {
            _name.text = "";
            _spoke.text = "";
            _bars.Clear();
            _unlock.text = "";
            _body.Clear();
            return;
        }
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

    static Label NewLabel(string className, string text = "")
    {
        var label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.AddToClassList(className);
        return label;
    }
}
