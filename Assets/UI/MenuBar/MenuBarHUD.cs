using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The menu buttons in the bottom right (MenuBar.uxml): each opens its panel, or closes it when it's open, with the
/// mouse. A button is highlighted while its panel has something new for the player: a found item they haven't looked at
/// (NewItems), a new skill or an empty passive slot. The
/// review button has the number of items waiting to be reviewed, highlighted where they can be. A player on a controller only sees the highlighted ones. Read-only:
/// never changes ECS.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class MenuBarHUD : MonoBehaviour, IPerPlayer
{
    /// <summary>Whose HUD this is: 0 is player 1 (the rig's own); player 2's copy is made by CoopManager.</summary>
    public int PlayerIndex { get; set; }

    const string OpenClass = "menu-button--open", NewClass = "menu-button--new", PulseClass = "menu-button--pulse", MutedClass = "menu-button--muted";
    const string NoticesClass = "menu-bar--notices", NameShownClass = "menu-bar__name--shown";
    const float PulseInterval = 0.5f;

    class MenuButton
    {
        public string Name;
        public string Class;
        public string Action;           // The input action whose key it shows; null: Esc
        public Action<int> Toggle;      // Given the player's index
        public Func<int, bool> IsOpen;
        public Func<int, bool> HasNew;
        public Func<int> Count;         // A number in its corner while above 0; null: none
        public Func<bool> Enabled;      // Greyed out (it still opens) while false; null: never
        public Label CountLabel;
        public int ShownCount;
        public VisualElement Element;
        public Label Key;
    }

    readonly MenuButton[] _buttons =
    {
        Panel("Character", "character", "Player/Inventory", MenuTab.Character, NewItems.Any),
        Review(),
        Panel("Skills", "skills", "Player/Skills", MenuTab.Skills, HasNewSkills),
        Panel("Item Database", "itemdatabase", "Player/ItemDatabase", MenuTab.ItemDatabase, _ => false),
        Panel("Statistics", "statistics", "Player/Statistics", MenuTab.Statistics, _ => false),
        new MenuButton { Name = "Menu", Class = "gamemenu", Toggle = GameMenuHUD.Toggle, IsOpen = GameMenuHUD.IsOpenFor, HasNew = _ => false },
    };

    PanelRenderer _panelRenderer;
    VisualElement _bar;
    Label _name;
    int _boundVersion = -1;
    int _keyVersion;

    static MenuButton Panel(string name, string styleClass, string action, MenuTab tab, Func<int, bool> hasNew) => new MenuButton
    {
        Name = name, Class = styleClass, Action = action, HasNew = hasNew,
        Toggle = player => MenuPanels.For(player).ToggleTab(tab),
        IsOpen = player => MenuPanels.For(player).IsTabOpen(tab),
    };

    static MenuButton Review()
    {
        var button = Panel("Item Review", "itemreview", "Player/ItemReview", MenuTab.ItemReview, _ => Waiting() > 0 && GameArea.CanChangeLoadout);
        button.Count = Waiting;
        button.Enabled = () => GameArea.CanChangeLoadout;
        return button;
    }

    static int Waiting() => AccountManager.Instance != null ? AccountManager.Instance.PendingDrops.Count : 0;

    static bool HasNewSkills(int player)
    {
        var skills = ActiveSkillManager.For(player);
        var passives = PassiveSkillManager.For(player);
        return (skills != null && skills.HasNew) || (passives != null && passives.HasEmptySlot);
    }

    void OnEnable()
    {
        _panelRenderer = GetComponent<PanelRenderer>();
        _panelRenderer.RegisterUIReloadCallback(OnUIReload);
    }

    void OnDisable()
    {
        _panelRenderer.UnregisterUIReloadCallback(OnUIReload);
        MenuPanels.SetBlocking(this, false);
        _bar = null;
        _boundVersion = -1;
    }

    void OnUIReload(PanelRenderer panelRenderer, VisualElement root, int version)
    {
        if (_bar != null && version == _boundVersion) return;

        // The Panel Renderer's own container isn't part of the UXML; make it fill the screen without taking clicks
        root.style.position = Position.Absolute;
        root.style.left = 0;
        root.style.top = 0;
        root.style.right = 0;
        root.style.bottom = 0;
        root.pickingMode = PickingMode.Ignore;
        // The player's half of the screen in co-op
        PlayerHud.Register(root, PlayerIndex);

        _bar = root.Q<VisualElement>("bar");
        if (_bar == null)
        {
            Debug.LogError("MenuBarHUD: no bar. Is MenuBar.uxml assigned as the Panel Renderer's Source Asset?", this);
            return;
        }
        Build();
        _boundVersion = version;
    }

    void Build()
    {
        _bar.Clear();
        // Clicks on the bar aren't attacks
        _bar.RegisterCallback<PointerEnterEvent>(_ => MenuPanels.SetBlocking(this, true));
        _bar.RegisterCallback<PointerLeaveEvent>(_ => MenuPanels.SetBlocking(this, false));

        foreach (var button in _buttons)
        {
            button.Element = new VisualElement();
            button.Element.AddToClassList("menu-button");
            button.Element.AddToClassList("menu-button--" + button.Class);
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("menu-button__icon");
            var dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.AddToClassList("menu-button__dot");
            icon.Add(dot);
            button.CountLabel = new Label { pickingMode = PickingMode.Ignore };
            button.CountLabel.AddToClassList("menu-button__count");
            icon.Add(button.CountLabel);
            button.Element.EnableInClassList("menu-button--counted", button.Count != null);
            button.Key = new Label { pickingMode = PickingMode.Ignore };
            button.Key.AddToClassList("menu-button__key");
            button.ShownCount = -1;
            button.Element.Add(icon);
            button.Element.Add(button.Key);

            button.Element.RegisterCallback<ClickEvent>(_ => button.Toggle(PlayerIndex));
            button.Element.RegisterCallback<PointerEnterEvent>(_ => ShowName(button.Name));
            button.Element.RegisterCallback<PointerLeaveEvent>(_ => _name.RemoveFromClassList(NameShownClass));
            _bar.Add(button.Element);
        }

        _name = new Label { pickingMode = PickingMode.Ignore };
        _name.AddToClassList("menu-bar__name");
        _bar.Add(_name);
        _keyVersion = -1;
    }

    void ShowName(string text)
    {
        _name.text = text;
        _name.AddToClassList(NameShownClass);
    }

    void LateUpdate()
    {
        var controls = GameInput.For(PlayerIndex);
        if (_bar == null || controls == null) return;

        // The full bar is for the mouse: a controller player only sees what is new
        _bar.EnableInClassList(NoticesClass, controls.UsingGamepad || GameInput.MouseOwner != PlayerIndex);

        bool pulse = Mathf.FloorToInt(Time.unscaledTime / PulseInterval) % 2 == 0;
        foreach (var button in _buttons)
        {
            button.Element.EnableInClassList(OpenClass, button.IsOpen(PlayerIndex));
            button.Element.EnableInClassList(NewClass, button.HasNew(PlayerIndex));
            button.Element.EnableInClassList(PulseClass, pulse);
            button.Element.EnableInClassList(MutedClass, button.Enabled != null && !button.Enabled());
            int count = button.Count != null ? button.Count() : 0;
            if (button.ShownCount == count) continue;
            button.ShownCount = count;
            button.CountLabel.text = count.ToString();
            button.CountLabel.style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // After a rebind or a switch between keyboard and gamepad
        if (_keyVersion == GameInput.Version) return;
        _keyVersion = GameInput.Version;
        foreach (var button in _buttons)
        {
            string key = "Esc";
            if (button.Action != null)
            {
                var action = controls.Actions.asset.FindAction(button.Action);
                key = action != null ? controls.Label(action) : "";
            }
            button.Key.text = key;
            button.Key.style.visibility = string.IsNullOrEmpty(key) ? Visibility.Hidden : Visibility.Visible;
        }
    }
}
