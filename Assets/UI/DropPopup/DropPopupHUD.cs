using Equipment;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// The item review panel, toggled with the ItemReview action (J): AccountManager's oldest pending drop, while the game
/// keeps running, where found items can be reviewed (GameArea.CanChangeLoadout). A newly found item is shown with an OK
/// button (it's already in the database). A drop whose entry already has an item is shown next to it with the
/// differences, and the player takes the new one or keeps the current one. Esc or B closes it and leaves the rest waiting; it also closes
/// after the last one. The drops are the account's, so it's one for both players: whoever opens it has it until they close
/// it; the other player can't open it meanwhile.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class DropPopupHUD : MonoBehaviour
{
    const string HiddenClass = "dp-root--hidden";
    const string EmptyTitleClass = "dp-card__title--empty";

    PanelRenderer _panelRenderer;
    bool _bound;
    int _boundVersion = -1;
    bool _dirty = true;
    bool _open;
    int _owner;     // The player who has it open (or had it last)
    readonly System.Action<object>[] _openChanged = new System.Action<object>[GameInput.MaxPlayers];

    PlayerMenus Menus => MenuPanels.For(_owner);
    PlayerNav Nav => UiNav.For(_owner);

    VisualElement _root, _popup, _cards;
    Label _heading, _queue, _message;
    VisualElement _currentColumn, _currentCard;
    Label _currentTitle, _currentSubtitle;
    VisualElement _currentBody;
    VisualElement _newCard;
    Label _newLabel, _newTitle, _newSubtitle;
    VisualElement _newBody;
    Button _keepButton, _takeButton, _okButton;

    AccountManager _subscribedAccount;

    void OnEnable()
    {
        _panelRenderer = GetComponent<PanelRenderer>();
        _panelRenderer.RegisterUIReloadCallback(OnUIReload);
        // In every player's tabs; another of the owner's panels opening closes it
        for (int i = 0; i < GameInput.MaxPlayers; i++)
        {
            int player = i;
            _openChanged[i] = openPanel => OnMenuOpenChanged(player, openPanel);
            MenuPanels.For(i).OpenChanged += _openChanged[i];
            MenuPanels.For(i).AddTab(this, MenuTab.ItemReview, open => SetOpenBy(player, open));
        }
    }

    void OnDisable()
    {
        for (int i = 0; i < GameInput.MaxPlayers; i++)
        {
            MenuPanels.For(i).RemoveTab(this);
            MenuPanels.For(i).OpenChanged -= _openChanged[i];
        }
        if (_open) SetOpen(false);
        _panelRenderer.UnregisterUIReloadCallback(OnUIReload);
        Unsubscribe();
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

        _bound = Bind(root);
        _boundVersion = version;
        if (_bound) SetOpen(_open);
    }

    bool Bind(VisualElement root)
    {
        _root = root.Q<VisualElement>("dropPopupRoot");
        _popup = root.Q<VisualElement>("popup");
        _cards = root.Q<VisualElement>("cards");
        _message = root.Q<Label>("message");
        _heading = root.Q<Label>("heading");
        _queue = root.Q<Label>("queue");
        _currentColumn = root.Q<VisualElement>("currentColumn");
        _currentCard = root.Q<VisualElement>("currentCard");
        _currentTitle = root.Q<Label>("currentTitle");
        _currentSubtitle = root.Q<Label>("currentSubtitle");
        _currentBody = root.Q<VisualElement>("currentBody");
        _newCard = root.Q<VisualElement>("newCard");
        _newLabel = root.Q<Label>("newLabel");
        _newTitle = root.Q<Label>("newTitle");
        _newSubtitle = root.Q<Label>("newSubtitle");
        _newBody = root.Q<VisualElement>("newBody");
        _keepButton = root.Q<Button>("keepButton");
        _takeButton = root.Q<Button>("takeButton");
        _okButton = root.Q<Button>("okButton");
        if (_root == null || _popup == null || _cards == null || _message == null || _currentColumn == null || _currentCard == null || _newCard == null || _keepButton == null || _takeButton == null || _okButton == null)
        {
            Debug.LogError("DropPopupHUD: DropPopup.uxml is missing elements. Is it the Panel Renderer's Source Asset?", this);
            return false;
        }

        _keepButton.clicked += () => Resolve(takeNew: false);
        _takeButton.clicked += () => Resolve(takeNew: true);
        _okButton.clicked += () => Resolve(takeNew: false);
        if (_popup.Q(className: "nav-hint") == null) UiNav.Hint(_popup, "A Choose · LB/RB Panels · B Close");
        return true;
    }

    void Update()
    {
        Subscribe();
        if (!_bound) return;
        // Any player's toggle
        for (int i = 0; i < GameInput.PlayerCount; i++)
            if (MenuPanels.For(i).TogglePressed(GameInput.For(i).Actions.asset.FindAction("Player/ItemReview")))
                SetOpenBy(i, !_open);

        if (!_open) return;
        if (Menus.BackPressed)
        {
            SetOpen(false);
            return;
        }
        if (_canReview != GameArea.CanChangeLoadout) _dirty = true;
        if (_dirty) Refresh();
    }

    bool _canReview;

    // Another of its owner's menu panels opened: close this one
    void OnMenuOpenChanged(int player, object openPanel)
    {
        if (_open && player == _owner && openPanel != null && !ReferenceEquals(openPanel, this)) SetOpen(false);
    }

    // A player opens or closes it; while one has it open, the other can't
    void SetOpenBy(int player, bool open)
    {
        if (_open && player != _owner) return;
        if (open) _owner = player;
        SetOpen(open);
    }

    void SetOpen(bool open)
    {
        _open = open;
        Menus.SetOpen(this, open);
        if (!_bound) return;
        _root.EnableInClassList(HiddenClass, !open);
        if (open)
        {
            Nav.Register(_root);
            Refresh();
        }
        // A controller starts on Take new (or OK)
        Nav.SetScope(this, _popup, open, _takeButton.style.display == DisplayStyle.Flex ? _takeButton : _okButton);
    }

    // AccountManager may not exist yet when this is enabled
    void Subscribe()
    {
        var account = AccountManager.Instance;
        if (account == _subscribedAccount) return;
        Unsubscribe();
        if (account == null) return;
        account.PendingDropsChanged += OnPendingDropsChanged;
        _subscribedAccount = account;
        _dirty = true;
    }

    void Unsubscribe()
    {
        if (_subscribedAccount != null) _subscribedAccount.PendingDropsChanged -= OnPendingDropsChanged;
        _subscribedAccount = null;
    }

    void OnPendingDropsChanged() => _dirty = true;

    void Resolve(bool takeNew)
    {
        if (_subscribedAccount == null || !GameArea.CanChangeLoadout) return;
        _subscribedAccount.ResolveDrop(takeNew);
        if (_subscribedAccount.PendingDrops.Count == 0) SetOpen(false);
        else
        {
            Refresh();
            Nav.Focus(_takeButton.style.display == DisplayStyle.Flex ? _takeButton : _okButton);
        }
    }

    void Refresh()
    {
        _dirty = false;
        _canReview = GameArea.CanChangeLoadout;
        var drops = _subscribedAccount != null ? _subscribedAccount.PendingDrops : null;
        bool review = drops != null && drops.Count > 0 && _canReview;
        _cards.style.display = review ? DisplayStyle.Flex : DisplayStyle.None;
        _message.style.display = review ? DisplayStyle.None : DisplayStyle.Flex;
        if (!review)
        {
            _heading.text = "Item review";
            _queue.text = "";
            _message.text = drops == null || drops.Count == 0 ? "No items to review."
                          : $"{drops.Count} {(drops.Count == 1 ? "item waits" : "items wait")} to be reviewed in town or at difficulty 1.";
            return;
        }

        var drop = drops[0];
        _queue.text = drops.Count > 1 ? $"1 of {drops.Count}" : "";

        // Compare against what's in the database now (an earlier drop of the same item may have replaced it)
        var current = drop.Kind == AccountManager.DropKind.Compare ? _subscribedAccount.Items.Get(drop.Key) : null;
        bool compare = current != null;

        _heading.text = compare ? "Item dropped" : "New item found!";
        // New item: the current card says so and its button is hidden but keeps its space, so OK sits where Take new does
        if (compare) Fill(current, _currentTitle, _currentSubtitle, _currentBody, _currentCard, compareTo: null);
        else FillEmpty();
        _keepButton.style.visibility = compare ? Visibility.Visible : Visibility.Hidden;
        _keepButton.SetEnabled(compare);
        _newLabel.text = compare ? "New" : "Added to your item database";
        Fill(drop.Item, _newTitle, _newSubtitle, _newBody, _newCard, compareTo: current);

        _takeButton.style.display = compare ? DisplayStyle.Flex : DisplayStyle.None;
        _okButton.style.display = compare ? DisplayStyle.None : DisplayStyle.Flex;
    }

    // No current item yet: a muted note, and the card's usual border
    void FillEmpty()
    {
        _currentTitle.text = "Nothing yet";
        _currentTitle.style.color = StyleKeyword.Null;
        _currentTitle.EnableInClassList(EmptyTitleClass, true);
        _currentSubtitle.text = "";
        _currentBody.Clear();
        _currentCard.style.borderTopColor = StyleKeyword.Null;
        _currentCard.style.borderBottomColor = StyleKeyword.Null;
        _currentCard.style.borderLeftColor = StyleKeyword.Null;
        _currentCard.style.borderRightColor = StyleKeyword.Null;
    }

    static void Fill(Item item, Label title, Label subtitle, VisualElement body, VisualElement card, Item compareTo)
    {
        title.text = item.BaseItem.Name;
        title.RemoveFromClassList(EmptyTitleClass);
        title.style.color = ItemText.TitleColor(item);
        subtitle.text = ItemText.Subtitle(item);
        ItemStatRows.Fill(body, item, compareTo);

        var border = ItemText.TitleColor(item);
        card.style.borderTopColor = border;
        card.style.borderBottomColor = border;
        card.style.borderLeftColor = border;
        card.style.borderRightColor = border;
    }
}
