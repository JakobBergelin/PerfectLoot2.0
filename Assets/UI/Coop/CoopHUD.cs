using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Couch co-op in the Hub (on the game rig): while a device is free, "Player 2: press Start to join" shows bottom-right,
/// and pressing it there adds player 2 with a character picker on the right (CoopPicker). Picking spawns them next to
/// player 1 (CoopManager). Change character in their Start menu opens it again (OpenPicker), with Leave co-op too.
/// Nobody joins in a dungeon; a player 2 who never picked a character leaves when the Hub is left. The picker is one of
/// player 2's menu panels (PlayerMenus), so their gameplay input is ignored while it's open. Couch players join only in
/// this machine's own game, up to NetSession.MaxPlayers with the online ones. Also shows online session notices
/// (NetSession.Notice) at the top, in every scene, and in the Hub a Join button per friend whose game can be joined
/// (PlatformManager.JoinableFriends, Steam).
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class CoopHUD : MonoBehaviour
{
    const string HiddenClass = "coop--hidden";

    static CoopHUD _instance;

    PanelRenderer _panelRenderer;
    int _boundVersion = -1;
    VisualElement _pickerHolder, _friendsPanel;
    Label _joinHint, _netNotice;
    string _shownFriends;   // The joinable friends the panel shows, to rebuild it only when they change
    CoopPicker _picker;
    bool _open;

    /// <summary>True in the Hub (GameBootstrap.TownScene), where player 2 can join, change character and leave.</summary>
    public static bool InHub => SceneManager.GetActiveScene().name == GameBootstrap.TownScene;

    /// <summary>
    /// Another couch player may join: in this machine's own game, while it has room (NetSession.MaxPlayers, remote
    /// players included). In someone else's game the host counts the players a client brought when it joined, so couch
    /// players join before joining a game.
    /// </summary>
    static bool CouchJoinAllowed => NetSession.IsHost && GameInput.PlayerCount + NetSession.RemotePlayers < NetSession.MaxPlayers;

    static PlayerMenus Player2Menus => MenuPanels.For(1);
    static AccountCharacter Player2Character => AccountManager.Instance != null ? AccountManager.Instance.SelectedCharacterOf(1) : null;

    /// <summary>Player 2's picker in the Hub (their Start menu's Change character).</summary>
    public static void OpenPicker()
    {
        if (_instance != null) _instance.SetOpen(true);
    }

    void OnEnable()
    {
        _instance = this;
        _panelRenderer = GetComponent<PanelRenderer>();
        _panelRenderer.RegisterUIReloadCallback(OnUIReload);
        Player2Menus.OpenChanged += OnPlayer2MenuChanged;
    }

    void OnDisable()
    {
        if (_instance == this) _instance = null;
        Player2Menus.OpenChanged -= OnPlayer2MenuChanged;
        _panelRenderer.UnregisterUIReloadCallback(OnUIReload);
        _boundVersion = -1;
        _picker = null;
        GameInput.AllowJoining(this, false);
        SetOpen(false);
    }

    void OnUIReload(PanelRenderer panelRenderer, VisualElement root, int version)
    {
        if (_picker != null && version == _boundVersion) return;

        // The Panel Renderer's own container isn't part of the UXML; make it fill the screen without taking clicks
        root.style.position = Position.Absolute;
        root.style.left = 0;
        root.style.top = 0;
        root.style.right = 0;
        root.style.bottom = 0;
        root.pickingMode = PickingMode.Ignore;

        _pickerHolder = root.Q<VisualElement>("pickerHolder");
        _joinHint = root.Q<Label>("joinHint");
        _netNotice = root.Q<Label>("netNotice");
        _friendsPanel = root.Q<VisualElement>("friendsPanel");
        _shownFriends = null;
        _boundVersion = version;
        if (_pickerHolder == null || _joinHint == null || _netNotice == null || _friendsPanel == null)
        {
            Debug.LogError("CoopHUD: CoopHUD.uxml is missing elements. Is it the Panel Renderer's Source Asset?", this);
            _picker = null;
            return;
        }

        _picker = new CoopPicker(detailed: false, null);
        _picker.Picked = Picked;
        _picker.Left = Leave;
        _picker.Back = () =>
        {
            if (Player2Character == null) Leave();
            else SetOpen(false);
        };
        _picker.SetOwnTag("Playing");
        _pickerHolder.Clear();
        _pickerHolder.Add(_picker.Element);
        _pickerHolder.EnableInClassList(HiddenClass, !_open);
    }

    void Update()
    {
        if (_picker == null) return;
        bool hub = InHub;
        GameInput.AllowJoining(this, hub && CouchJoinAllowed);
        var device = hub ? GameInput.JoinPressed() : null;
        if (device != null && CoopManager.Join(device)) SetOpen(true);

        string hint = CoopManager.JoinHint();
        _joinHint.text = hint ?? "";
        _joinHint.EnableInClassList(HiddenClass, hint == null);
        string notice = NetSession.Notice;
        _netNotice.text = notice ?? "";
        _netNotice.EnableInClassList(HiddenClass, notice == null);
        UpdateFriends(hub && NetSession.IsHost);

        if (GameInput.For(1) == null)
        {
            SetOpen(false);
            return;
        }
        // Joined (e.g. CoopManager's test) but without a character: pick one, or leave once the Hub is behind
        if (Player2Character == null)
        {
            if (!hub)
            {
                Leave();
                return;
            }
            if (!_open) SetOpen(true);
        }
        if (!_open) return;
        _picker.SetActions("Play", Player2Character == null ? "Leave" : "Close");
        _picker.Update();
    }

    // A button per friend whose game can be joined (Steam), while in this machine's own game in the Hub
    void UpdateFriends(bool show)
    {
        var friends = PlatformManager.JoinableFriends;
        string shown = "";
        if (show)
            foreach (var friend in friends) shown += $"{friend.Id}:{friend.Players};";
        _friendsPanel.EnableInClassList(HiddenClass, shown.Length == 0);
        if (shown == _shownFriends) return;
        _shownFriends = shown;
        _friendsPanel.Clear();
        if (shown.Length == 0) return;
        foreach (var friend in friends)
        {
            var joining = friend;
            var button = new Button(() => PlatformManager.Join(joining)) { text = $"Join {friend.Name} ({friend.Players}/{NetSession.MaxPlayers})" };
            button.AddToClassList("coop-friend-button");
            _friendsPanel.Add(button);
        }
    }

    void SetOpen(bool open)
    {
        if (open && (!InHub || GameInput.For(1) == null)) open = false;
        bool opening = open && !_open;
        _open = open;
        Player2Menus.SetOpen(this, open);
        // Their stick and A are for the picker, not their character
        UiNav.For(1).SetScope(this, _pickerHolder, open && _pickerHolder != null);
        if (_pickerHolder != null) _pickerHolder.EnableInClassList(HiddenClass, !open);
        if (opening && _picker != null) _picker.Open(Player2Character, showLeave: Player2Character != null);
    }

    // Another of player 2's panels opened (e.g. their Start menu): the picker closes
    void OnPlayer2MenuChanged(object openPanel)
    {
        if (_open && openPanel != null && !ReferenceEquals(openPanel, this)) SetOpen(false);
    }

    // Plays it (CoopManager gives player 2 their managers, or new ones for another character); their own is just closing
    void Picked(AccountCharacter character)
    {
        var account = AccountManager.Instance;
        if (account != null && (character == Player2Character || account.SelectCharacter(character.Guid, 1))) SetOpen(false);
    }

    void Leave()
    {
        SetOpen(false);
        CoopManager.Leave(1);
    }
}
