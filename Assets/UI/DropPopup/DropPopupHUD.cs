using Equipment;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Shows AccountManager's oldest pending drop while the game keeps running. A newly found item is shown with an OK
/// button (it's already in the database). A drop whose entry already has an item is shown next to it with the
/// differences, and the player takes the new one or keeps the current one. Esc keeps the current one / closes.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class DropPopupHUD : MonoBehaviour
{
    const string HiddenClass = "dp-root--hidden";

    PanelRenderer _panelRenderer;
    bool _bound;
    int _boundVersion = -1;
    bool _dirty = true;
    bool _visible;

    VisualElement _root;
    Label _heading, _queue;
    VisualElement _currentColumn, _currentCard;
    Label _currentTitle, _currentSubtitle, _currentBody;
    VisualElement _newCard;
    Label _newLabel, _newTitle, _newSubtitle, _newBody;
    Button _keepButton, _takeButton, _okButton;

    AccountManager _subscribedAccount;

    void OnEnable()
    {
        _panelRenderer = GetComponent<PanelRenderer>();
        _panelRenderer.RegisterUIReloadCallback(OnUIReload);
    }

    void OnDisable()
    {
        SetVisible(false);
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
        _dirty = true;
    }

    bool Bind(VisualElement root)
    {
        _root = root.Q<VisualElement>("dropPopupRoot");
        _heading = root.Q<Label>("heading");
        _queue = root.Q<Label>("queue");
        _currentColumn = root.Q<VisualElement>("currentColumn");
        _currentCard = root.Q<VisualElement>("currentCard");
        _currentTitle = root.Q<Label>("currentTitle");
        _currentSubtitle = root.Q<Label>("currentSubtitle");
        _currentBody = root.Q<Label>("currentBody");
        _newCard = root.Q<VisualElement>("newCard");
        _newLabel = root.Q<Label>("newLabel");
        _newTitle = root.Q<Label>("newTitle");
        _newSubtitle = root.Q<Label>("newSubtitle");
        _newBody = root.Q<Label>("newBody");
        _keepButton = root.Q<Button>("keepButton");
        _takeButton = root.Q<Button>("takeButton");
        _okButton = root.Q<Button>("okButton");
        if (_root == null || _currentColumn == null || _currentCard == null || _newCard == null || _keepButton == null || _takeButton == null || _okButton == null)
        {
            Debug.LogError("DropPopupHUD: DropPopup.uxml is missing elements. Is it the Panel Renderer's Source Asset?", this);
            return false;
        }

        _keepButton.clicked += () => Resolve(takeNew: false);
        _takeButton.clicked += () => Resolve(takeNew: true);
        _okButton.clicked += () => Resolve(takeNew: false);
        return true;
    }

    void Update()
    {
        Subscribe();
        if (!_bound) return;
        if (_dirty) Refresh();

        // An open menu panel handles Esc first (it closes); the next Esc resolves the popup. Last frame's state too,
        // since the panel may already have closed itself this frame.
        if (_visible && !MenuPanels.IsPanelOpen && !_panelOpenLastFrame && !MenuPanels.IsTyping &&
            Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            Resolve(takeNew: false);
    }

    bool _panelOpenLastFrame;

    void LateUpdate() => _panelOpenLastFrame = MenuPanels.IsPanelOpen;

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
        if (_subscribedAccount != null) _subscribedAccount.ResolveDrop(takeNew);
    }

    void Refresh()
    {
        _dirty = false;
        var drops = _subscribedAccount != null ? _subscribedAccount.PendingDrops : null;
        if (drops == null || drops.Count == 0)
        {
            SetVisible(false);
            return;
        }

        var drop = drops[0];
        _queue.text = drops.Count > 1 ? $"1 of {drops.Count}" : "";

        // Compare against what's in the database now (an earlier drop of the same item may have replaced it)
        var current = drop.Kind == AccountManager.DropKind.Compare ? _subscribedAccount.Items.Get(drop.Key) : null;
        bool compare = current != null;

        _heading.text = compare ? "Item dropped" : "New item found!";
        // New item: only its own card, with OK under it
        _currentColumn.style.display = compare ? DisplayStyle.Flex : DisplayStyle.None;
        if (compare) Fill(current, _currentTitle, _currentSubtitle, _currentBody, _currentCard, compareTo: null);
        _newLabel.text = compare ? "New" : "Added to your item database";
        Fill(drop.Item, _newTitle, _newSubtitle, _newBody, _newCard, compareTo: current);

        _takeButton.style.display = compare ? DisplayStyle.Flex : DisplayStyle.None;
        _okButton.style.display = compare ? DisplayStyle.None : DisplayStyle.Flex;
        SetVisible(true);
    }

    static void Fill(Item item, Label title, Label subtitle, Label body, VisualElement card, Item compareTo)
    {
        title.text = item.BaseItem.Name;
        title.style.color = ItemText.TitleColor(item);
        subtitle.text = ItemText.Subtitle(item);
        body.text = ItemText.Body(item, compareTo);

        var border = ItemText.TitleColor(item);
        card.style.borderTopColor = border;
        card.style.borderBottomColor = border;
        card.style.borderLeftColor = border;
        card.style.borderRightColor = border;
    }

    // While visible the mouse is for the buttons, so gameplay ignores skill input (other panels stay open)
    void SetVisible(bool visible)
    {
        _visible = visible;
        MenuPanels.SetBlocking(this, visible);
        _root?.EnableInClassList(HiddenClass, !visible);
    }
}
