using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Floating combat numbers in screen space. Listens to the damage/heal/status events from ECS, and shows
/// each number as a pooled Label that follows its world position, rises and fades out.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class CombatTextHUD : MonoBehaviour
{
    enum Kind { Damage, Crit, Blocked, Bleed, Burn, Poison, Heal }

    static readonly string[] KindClasses =
    {
        null,                       // Damage: base .combat-text style
        "combat-text--crit",
        "combat-text--blocked",
        "combat-text--bleed",
        "combat-text--burn",
        "combat-text--poison",
        "combat-text--heal",
    };
    const string LabelClass = "combat-text";
    const string HiddenClass = "combat-text--hidden";

    [Tooltip("Seconds a number stays on screen")]
    public float Duration = 1f;
    [Tooltip("World units per second the number rises")]
    public float RiseSpeed = 0.7f;
    [Tooltip("Height above the hit where the number starts, in world units")]
    public float StartHeight = 1.5f;
    [Tooltip("Max numbers on screen; past this the oldest is reused")]
    public int MaxLabels = 150;
    [Tooltip("Fraction of the duration (at the end) spent fading out")]
    [Range(0f, 1f)] public float FadeFraction = 0.4f;

    struct Pending
    {
        public float Amount;
        public float3 Position;
        public Kind Kind;
    }

    class Entry
    {
        public Label Label;
        public Vector3 Start;
        public Vector3 Velocity;
        public float Age;
        public Kind Kind;
    }

    PanelRenderer _panelRenderer;
    VisualElement _layer;
    int _boundVersion = -1;
    Unity.Mathematics.Random _random;

    // Events fire during the ECS update; numbers are only queued there and shown in LateUpdate
    readonly List<Pending> _pending = new List<Pending>();
    readonly List<Entry> _active = new List<Entry>();
    readonly Stack<Entry> _free = new Stack<Entry>();

    DamagingSystem _damagingSystem;
    CalculateDamageSystem _calculateDamageSystem;
    BleedingUISystem _bleedingSystem;
    BurningUISystem _burningSystem;
    PoisonedUISystem _poisonedSystem;
    ApplyHealingSystem _healingSystem;

    void Awake() => _random = Unity.Mathematics.Random.CreateFromIndex((uint)System.Environment.TickCount);

    void OnEnable()
    {
        _panelRenderer = GetComponent<PanelRenderer>();
        _panelRenderer.RegisterUIReloadCallback(OnUIReload);
        SubscribeToSystems();
    }

    void OnDisable()
    {
        _panelRenderer.UnregisterUIReloadCallback(OnUIReload);
        UnsubscribeFromSystems();
        _pending.Clear();
        ReleaseAll();
        _layer = null;
        _boundVersion = -1;
    }

    void OnUIReload(PanelRenderer panelRenderer, VisualElement root, int version)
    {
        if (_layer != null && version == _boundVersion) return;

        // The Panel Renderer's own container isn't part of the UXML; make it fill the screen
        root.style.position = Position.Absolute;
        root.style.left = 0;
        root.style.top = 0;
        root.style.right = 0;
        root.style.bottom = 0;

        // New tree: labels from the old one are gone, so start the pool over
        _active.Clear();
        _free.Clear();
        _layer = root.Q<VisualElement>("combatTextLayer");
        if (_layer == null)
            Debug.LogError("CombatTextHUD: no element named combatTextLayer. Is CombatText.uxml assigned as the Panel Renderer's Source Asset?", this);
        _boundVersion = version;
    }

    void SubscribeToSystems()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null) return;

        _damagingSystem = world.GetExistingSystemManaged<DamagingSystem>();
        _calculateDamageSystem = world.GetExistingSystemManaged<CalculateDamageSystem>();
        _bleedingSystem = world.GetExistingSystemManaged<BleedingUISystem>();
        _burningSystem = world.GetExistingSystemManaged<BurningUISystem>();
        _poisonedSystem = world.GetExistingSystemManaged<PoisonedUISystem>();
        _healingSystem = world.GetExistingSystemManaged<ApplyHealingSystem>();

        if (_damagingSystem != null) _damagingSystem.OnDamageTaken += OnDamageTaken;
        if (_calculateDamageSystem != null) _calculateDamageSystem.OnDamageBlocked += OnBlocked;
        if (_bleedingSystem != null) _bleedingSystem.OnBleeding += OnBleeding;
        if (_burningSystem != null) _burningSystem.OnBurning += OnBurning;
        if (_poisonedSystem != null) _poisonedSystem.OnPoisoned += OnPoisoned;
        if (_healingSystem != null) _healingSystem.OnHealingReceived += OnHealing;
    }

    void UnsubscribeFromSystems()
    {
        if (_damagingSystem != null) _damagingSystem.OnDamageTaken -= OnDamageTaken;
        if (_calculateDamageSystem != null) _calculateDamageSystem.OnDamageBlocked -= OnBlocked;
        if (_bleedingSystem != null) _bleedingSystem.OnBleeding -= OnBleeding;
        if (_burningSystem != null) _burningSystem.OnBurning -= OnBurning;
        if (_poisonedSystem != null) _poisonedSystem.OnPoisoned -= OnPoisoned;
        if (_healingSystem != null) _healingSystem.OnHealingReceived -= OnHealing;
    }

    void OnDamageTaken(float amount, float3 position, bool isCrit) => Queue(amount, position, isCrit ? Kind.Crit : Kind.Damage);
    void OnBlocked(float amount, float3 position) => Queue(amount, position, Kind.Blocked);
    void OnBleeding(float amount, float3 position) => Queue(amount, position, Kind.Bleed);
    void OnBurning(float amount, float3 position) => Queue(amount, position, Kind.Burn);
    void OnPoisoned(float amount, float3 position) => Queue(amount, position, Kind.Poison);
    void OnHealing(float amount, float3 position) => Queue(amount, position, Kind.Heal);

    void Queue(float amount, float3 position, Kind kind) =>
        _pending.Add(new Pending { Amount = amount, Position = position, Kind = kind });

    void LateUpdate()
    {
        var camera = Camera.main;
        if (_layer == null || camera == null || _layer.panel == null)
        {
            _pending.Clear();
            return;
        }

        foreach (var pending in _pending) Show(pending);
        _pending.Clear();

        float deltaTime = Time.deltaTime;
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var entry = _active[i];
            entry.Age += deltaTime;
            if (entry.Age >= Duration)
            {
                Release(i);
                continue;
            }

            var worldPosition = entry.Start + entry.Velocity * entry.Age;
            bool behindCamera = camera.WorldToViewportPoint(worldPosition).z < 0f;
            entry.Label.EnableInClassList(HiddenClass, behindCamera);
            if (behindCamera) continue;

            // Position and opacity change every frame, so they're set from code; everything else is USS
            Vector2 panelPosition = RuntimePanelUtils.CameraTransformWorldToPanel(_layer.panel, worldPosition, camera);
            entry.Label.style.translate = new Translate(panelPosition.x, panelPosition.y);

            float fadeStart = Duration * (1f - FadeFraction);
            entry.Label.style.opacity = entry.Age <= fadeStart ? 1f : 1f - (entry.Age - fadeStart) / (Duration - fadeStart);
        }
    }

    void Show(Pending pending)
    {
        Entry entry;
        if (_free.Count > 0)
        {
            entry = _free.Pop();
        }
        else if (_active.Count < MaxLabels)
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList(LabelClass);
            _layer.Add(label);
            entry = new Entry { Label = label };
        }
        else
        {
            // At the cap: reuse the oldest number on screen
            entry = _active[0];
            _active.RemoveAt(0);
        }

        // Same drift as before: start above the hit with a small random sideways offset, rise along that direction
        var direction = new Vector3(_random.NextFloat(-0.5f, 0.5f), StartHeight, 0f);
        entry.Start = (Vector3)pending.Position + direction;
        entry.Velocity = direction.normalized * RiseSpeed;
        entry.Age = 0f;

        SetKind(entry, pending.Kind);
        entry.Label.text = Format(pending.Amount);
        entry.Label.style.opacity = 1f;
        entry.Label.RemoveFromClassList(HiddenClass);
        // Newest on top
        entry.Label.BringToFront();
        _active.Add(entry);
    }

    void SetKind(Entry entry, Kind kind)
    {
        var oldClass = KindClasses[(int)entry.Kind];
        if (oldClass != null) entry.Label.RemoveFromClassList(oldClass);
        var newClass = KindClasses[(int)kind];
        if (newClass != null) entry.Label.AddToClassList(newClass);
        entry.Kind = kind;
    }

    void Release(int activeIndex)
    {
        var entry = _active[activeIndex];
        _active.RemoveAt(activeIndex);
        entry.Label.AddToClassList(HiddenClass);
        _free.Push(entry);
    }

    void ReleaseAll()
    {
        for (int i = _active.Count - 1; i >= 0; i--) Release(i);
    }

    static string Format(float value)
    {
        if (value >= 1f) return value.ToString("F0");
        if (value > 0.2f) return "<1";
        return "~0";
    }
}
