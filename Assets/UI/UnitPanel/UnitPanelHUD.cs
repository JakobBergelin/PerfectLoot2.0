using System.Collections.Generic;
using Effects;
using Equipment;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// At the top of the screen while the mouse is over an enemy or companion (InputsData.Hovered): its name, Health, the
/// stats it has, its buffs and debuffs (HoveredEffect), passives and skills. Most of a companion's stats are its owner's,
/// as CompanionStatsSystem copies them; this shows what the game uses. Never takes the mouse from the game.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class UnitPanelHUD : MonoBehaviour
{
    const string VisibleClass = "unit-panel--visible";
    const string HiddenClass = "unit-panel--hidden";

    [Tooltip("The same library the game bakes; used for names, icons and descriptions")]
    public EffectLibrary Library;

    class StatRow
    {
        public VisualElement Element;
        public Label Name, Value;
    }

    class EffectRow
    {
        public VisualElement Element;
        public EffectTile Tile;
        public Label Text;
    }

    PanelRenderer _panelRenderer;
    bool _bound;
    int _boundVersion = -1;

    VisualElement _panel, _healthFill, _stats, _effects, _passives;
    Label _name, _healthText, _skills;
    readonly List<StatRow> _statRows = new List<StatRow>();
    readonly Dictionary<EffectType, EffectRow> _effectRows = new Dictionary<EffectType, EffectRow>();
    readonly List<string> _skillNames = new List<string>();
    int _usedStatRows;
    Entity _shown;

    void OnEnable()
    {
        _panelRenderer = GetComponent<PanelRenderer>();
        _panelRenderer.RegisterUIReloadCallback(OnUIReload);
    }

    void OnDisable()
    {
        _panelRenderer.UnregisterUIReloadCallback(OnUIReload);
        _bound = false;
        _boundVersion = -1;
    }

    void OnUIReload(PanelRenderer panelRenderer, VisualElement root, int version)
    {
        if (_bound && version == _boundVersion) return;

        // The Panel Renderer's own container isn't part of the UXML; make it fill the screen
        root.style.position = Position.Absolute;
        root.style.left = 0;
        root.style.top = 0;
        root.style.right = 0;
        root.style.bottom = 0;
        root.pickingMode = PickingMode.Ignore;

        _panel = root.Q<VisualElement>("panel");
        _name = root.Q<Label>("unitName");
        _healthFill = root.Q<VisualElement>("healthFill");
        _healthText = root.Q<Label>("healthText");
        _stats = root.Q<VisualElement>("stats");
        _effects = root.Q<VisualElement>("effects");
        _passives = root.Q<VisualElement>("passives");
        _skills = root.Q<Label>("skills");
        _bound = _panel != null && _name != null && _healthFill != null && _healthText != null && _stats != null && _effects != null && _passives != null && _skills != null;
        if (!_bound) Debug.LogError("UnitPanelHUD: UnitPanel.uxml is missing elements. Is it the Panel Renderer's Source Asset?", this);

        // New tree: the old rows are gone
        _statRows.Clear();
        _effectRows.Clear();
        _shown = Entity.Null;
        _boundVersion = version;
    }

    void LateUpdate()
    {
        if (!_bound) return;

        var world = World.DefaultGameObjectInjectionWorld;
        var characterManager = CharacterManager.Instance;
        if (world == null || !world.IsCreated || characterManager == null || Library == null) return;

        var entityManager = world.EntityManager;
        var player = characterManager.Player;
        bool hasPlayer = entityManager.Exists(player) && entityManager.HasComponent<InputsData>(player) && entityManager.HasBuffer<HoveredEffect>(player);
        var unit = hasPlayer ? entityManager.GetComponentData<InputsData>(player).Hovered : Entity.Null;

        // A dying unit has lost its Health
        bool show = entityManager.Exists(unit) && entityManager.HasComponent<UnitInfo>(unit) && entityManager.HasComponent<Health>(unit);
        _panel.EnableInClassList(VisibleClass, show);
        if (!show)
        {
            _shown = Entity.Null;
            return;
        }

        if (unit != _shown)
        {
            _shown = unit;
            ShowDescription(entityManager.GetComponentData<UnitInfo>(unit).Source.Value as IUnitDescription);
        }
        UpdateHealth(entityManager.GetComponentData<Health>(unit));
        UpdateStats(entityManager, unit);
        UpdateEffects(entityManager.GetBuffer<HoveredEffect>(player, isReadOnly: true));
    }

    void ShowDescription(IUnitDescription unit)
    {
        _name.text = unit.DisplayName;

        _passives.Clear();
        foreach (var passive in unit.Passives)
        {
            if (passive == null) continue;
            _passives.Add(NewLabel(passive.Name, "unit-panel__passive-name"));
            _passives.Add(NewLabel(passive.Description, "unit-panel__text"));
        }

        _skillNames.Clear();
        foreach (var skill in unit.Skills)
            if (skill != null) _skillNames.Add(skill.DisplayName);
        _skills.text = _skillNames.Count > 0 ? "Skills: " + string.Join(", ", _skillNames) : "";
        _skills.EnableInClassList(HiddenClass, _skillNames.Count == 0);
    }

    void UpdateHealth(in Health health)
    {
        _healthFill.style.width = Length.Percent(100f * Mathf.Clamp01(health.CurrentValue / health.MaxValue));
        _healthText.text = $"{health.CurrentValue:0.#} / {health.MaxValue:0.#}";
    }

    void UpdateStats(EntityManager entityManager, Entity unit)
    {
        var stats = entityManager.GetComponentData<CalculatedStats>(unit).Value;
        _usedStatRows = 0;

        AddStat(Affix.HealthRegeneration, entityManager.GetComponentData<HealthRegeneration>(unit).Value);
        AddStat(Affix.Armor, entityManager.GetComponentData<Armor>(unit).Value);
        AddStat(Affix.BlockChance, stats.BlockChance);
        AddStat(Affix.DodgeChance, stats.DodgeChance);
        AddStat(Affix.AllResistance, stats.AllResistance);
        AddStat(Affix.FireResistance, stats.FireResistance);
        AddStat(Affix.ColdResistance, stats.ColdResistance);
        AddStat(Affix.LightningResistance, stats.LightningResistance);
        AddStat(Affix.PoisonResistance, stats.PoisonResistance);

        AddStat(Affix.AttackSpeed, stats.AttackSpeed);
        AddStat(Affix.Damage, stats.Damage);
        AddStat(Affix.ElementalDamage, stats.ElementalDamage);
        AddStat(Affix.FireDamage, stats.FireDamage);
        AddStat(Affix.ColdDamage, stats.ColdDamage);
        AddStat(Affix.LightningDamage, stats.LightningDamage);
        AddStat(Affix.PoisonDamage, stats.PoisonDamage);
        AddStat(Affix.CriticalChance, stats.CriticalChance);
        AddStat(Affix.CriticalDamage, stats.CriticalDamage);
        AddStat(Affix.ArmorPierce, stats.ArmorPierce);
        AddStat(Affix.AreaOfEffect, stats.AreaOfEffect);
        AddStat(Affix.LifeOnHit, stats.LifeOnHit);
        AddStat(Affix.ResourceOnHit, stats.ResourceOnHit);

        // For enemies it is a speed, for companions a bonus, as for players
        float speed = entityManager.GetComponentData<MovementSpeed>(unit).Value;
        if (entityManager.HasComponent<Enemy>(unit)) SetStatRow(ItemText.AffixName(Affix.MovementSpeed), $"{speed:0.#} m/s");
        else AddStat(Affix.MovementSpeed, speed);

        for (int i = _usedStatRows; i < _statRows.Count; i++)
            _statRows[i].Element.AddToClassList(HiddenClass);
    }

    void AddStat(Affix affix, float value)
    {
        if (!Mathf.Approximately(value, 0f)) SetStatRow(ItemText.AffixName(affix), ItemText.Format(affix, value));
    }

    void SetStatRow(string name, string value)
    {
        if (_usedStatRows == _statRows.Count) _statRows.Add(NewStatRow());
        var row = _statRows[_usedStatRows++];
        row.Element.RemoveFromClassList(HiddenClass);
        row.Name.text = name;
        row.Value.text = value;
    }

    StatRow NewStatRow()
    {
        var row = new StatRow
        {
            Element = new VisualElement { pickingMode = PickingMode.Ignore },
            Name = NewLabel("", "unit-panel__stat-name"),
            Value = NewLabel("", "unit-panel__stat-value"),
        };
        row.Element.AddToClassList("unit-panel__stat");
        row.Element.Add(row.Name);
        row.Element.Add(row.Value);
        _stats.Add(row.Element);
        return row;
    }

    void UpdateEffects(DynamicBuffer<HoveredEffect> effects)
    {
        foreach (var row in _effectRows.Values) row.Tile.Active = false;

        foreach (var effect in effects)
        {
            var row = GetEffectRow(effect.Value.Type);
            if (row == null) continue;
            row.Tile.Active = true;
            row.Tile.Effect = effect.Value;
            row.Tile.Refresh();
            row.Text.text = $"{row.Tile.Definition.DisplayName}: {row.Tile.Definition.Describe(effect.Value)}";
        }

        foreach (var row in _effectRows.Values)
            row.Element.EnableInClassList(HiddenClass, !row.Tile.Active);
    }

    EffectRow GetEffectRow(EffectType type)
    {
        if (_effectRows.TryGetValue(type, out var row)) return row;
        var definition = Library.Get(type);
        if (definition == null) return null;

        row = new EffectRow
        {
            Element = new VisualElement { pickingMode = PickingMode.Ignore },
            Tile = new EffectTile(definition),
            Text = NewLabel("", "unit-panel__text"),
        };
        row.Tile.Element.pickingMode = PickingMode.Ignore;
        row.Element.AddToClassList("unit-panel__effect");
        row.Element.Add(row.Tile.Element);
        row.Element.Add(row.Text);
        _effects.Add(row.Element);
        _effectRows.Add(type, row);
        return row;
    }

    static Label NewLabel(string text, string styleClass)
    {
        var label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.AddToClassList(styleClass);
        return label;
    }
}
