using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The in-game menu in GameMenu.uxml: Resume, Leave dungeon (in a dungeon; asks first when something would be lost,
/// DungeonHUD), Options (OptionsHUD), Main menu and Exit game; for player 2 in the Hub also Change character and Leave
/// co-op; for player 1 Join a game (in their own game's Hub: the host's IP address, NetSession.Join) and Leave game (in
/// someone else's, NetSession.Leave). Each player has their own (IPerPlayer), opened by their Esc or Start when nothing else of theirs is open;
/// Esc, Start or B closes it again (or its question); while something else is open, that closes first. A menu panel
/// like the others (PlayerMenus), so their gameplay input is ignored while it's open. The game keeps running. Main
/// menu takes both players back (player 2 stays joined).
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class GameMenuHUD : MonoBehaviour, IPerPlayer
{
    /// <summary>Whose HUD this is: 0 is player 1 (the rig's own); player 2's copy is made by CoopManager.</summary>
    public int PlayerIndex { get; set; }

    PlayerMenus Menus => MenuPanels.For(PlayerIndex);
    PlayerNav Nav => UiNav.For(PlayerIndex);
    GameInput.PlayerControls Controls => GameInput.For(PlayerIndex);

    const string HiddenClass = "gm--hidden";
    const string JoinAddressKey = "PerfectLoot.JoinAddress";

    PanelRenderer _panelRenderer;
    bool _bound;
    int _boundVersion = -1;
    bool _open;
    VisualElement _root, _panel, _buttons, _leaveConfirm, _joinPanel;
    Button _resume, _leaveDungeon, _confirmLeave, _changeCharacter, _leaveCoop, _joinGame, _leaveGame;
    Label _leaveQuestion, _joinError;
    TextField _joinAddress;

    bool Confirming => _leaveConfirm != null && !_leaveConfirm.ClassListContains(HiddenClass);
    bool Joining => _joinPanel != null && !_joinPanel.ClassListContains(HiddenClass);

    void OnEnable()
    {
        _panelRenderer = GetComponent<PanelRenderer>();
        _panelRenderer.RegisterUIReloadCallback(OnUIReload);
        Menus.OpenChanged += OnMenuOpenChanged;
    }

    void OnDisable()
    {
        Menus.OpenChanged -= OnMenuOpenChanged;
        if (_open) SetOpen(false);
        _panelRenderer.UnregisterUIReloadCallback(OnUIReload);
        _bound = false;
        _boundVersion = -1;
    }

    void OnUIReload(PanelRenderer panelRenderer, VisualElement root, int version)
    {
        if (_bound && version == _boundVersion) return;

        // The Panel Renderer's own container isn't part of the UXML; make it fill the screen without taking clicks
        root.style.position = Position.Absolute;
        root.style.left = 0;
        root.style.top = 0;
        root.style.right = 0;
        root.style.bottom = 0;
        // The player's half of the screen in co-op
        PlayerHud.Register(root, PlayerIndex);
        root.pickingMode = PickingMode.Ignore;

        _bound = Bind(root);
        _boundVersion = version;
        if (_bound) SetOpen(_open);
    }

    bool Bind(VisualElement root)
    {
        _root = root.Q<VisualElement>("gameMenuRoot");
        _panel = root.Q<VisualElement>("panel");
        _buttons = root.Q<VisualElement>("buttons");
        _leaveConfirm = root.Q<VisualElement>("leaveConfirm");
        _leaveQuestion = root.Q<Label>("leaveQuestion");
        _resume = root.Q<Button>("resumeButton");
        _leaveDungeon = root.Q<Button>("leaveDungeonButton");
        _changeCharacter = root.Q<Button>("changeCharacterButton");
        _leaveCoop = root.Q<Button>("leaveCoopButton");
        _confirmLeave = root.Q<Button>("confirmLeaveButton");
        var stay = root.Q<Button>("stayButton");
        var options = root.Q<Button>("optionsButton");
        var mainMenu = root.Q<Button>("mainMenuButton");
        var exit = root.Q<Button>("exitButton");
        _joinGame = root.Q<Button>("joinGameButton");
        _leaveGame = root.Q<Button>("leaveGameButton");
        _joinPanel = root.Q<VisualElement>("joinPanel");
        _joinAddress = root.Q<TextField>("joinAddress");
        _joinError = root.Q<Label>("joinError");
        var confirmJoin = root.Q<Button>("confirmJoinButton");
        var cancelJoin = root.Q<Button>("cancelJoinButton");
        if (_root == null || _panel == null || _buttons == null || _leaveConfirm == null || _leaveQuestion == null || _resume == null ||
            _leaveDungeon == null || _confirmLeave == null || stay == null || options == null || mainMenu == null || exit == null ||
            _changeCharacter == null || _leaveCoop == null || _joinGame == null || _leaveGame == null || _joinPanel == null ||
            _joinAddress == null || _joinError == null || confirmJoin == null || cancelJoin == null)
        {
            Debug.LogError("GameMenuHUD: GameMenu.uxml is missing elements. Is it the Panel Renderer's Source Asset?", this);
            return false;
        }

        Nav.Register(_root);
        _resume.clicked += () => SetOpen(false);
        _leaveDungeon.clicked += OnLeaveClicked;
        _confirmLeave.clicked += Leave;
        stay.clicked += () => SetConfirming(false);
        options.clicked += () => OptionsHUD.Open(PlayerIndex);
        _changeCharacter.clicked += ChangeCharacter;
        _leaveCoop.clicked += LeaveCoop;
        mainMenu.clicked += MainMenu;
        exit.clicked += Exit;
        _joinGame.clicked += () => SetJoining(true);
        _leaveGame.clicked += LeaveGame;
        confirmJoin.clicked += Join;
        cancelJoin.clicked += () => SetJoining(false);
        MenuPanels.TrackTyping(_joinAddress);
        return true;
    }

    void Update()
    {
        if (!_bound || OptionsHUD.HandlesEscapeFor(PlayerIndex)) return;
        if (_open)
        {
            if (!Menus.BackPressed) return;
            if (Confirming) SetConfirming(false);
            else if (Joining) SetJoining(false);
            else SetOpen(false);
        }
        // This Esc is for what is open, or already closed something this frame
        else if (Menus.EscapePressed && !Menus.IsAnyOpen && !Menus.ChangedThisFrame) SetOpen(true);
    }

    void OnMenuOpenChanged(object openPanel)
    {
        if (_open && openPanel != null && !ReferenceEquals(openPanel, this)) SetOpen(false);
    }

    void SetOpen(bool open)
    {
        _open = open;
        Menus.SetOpen(this, open);
        if (!_bound) return;
        _root.EnableInClassList(HiddenClass, !open);
        _leaveDungeon.style.display = DungeonHUD.InDungeon ? DisplayStyle.Flex : DisplayStyle.None;
        bool player2InHub = PlayerIndex > 0 && CoopHUD.InHub;
        _changeCharacter.style.display = player2InHub ? DisplayStyle.Flex : DisplayStyle.None;
        _leaveCoop.style.display = player2InHub ? DisplayStyle.Flex : DisplayStyle.None;
        _joinGame.style.display = PlayerIndex == 0 && NetSession.IsHost && CoopHUD.InHub ? DisplayStyle.Flex : DisplayStyle.None;
        _leaveGame.style.display = PlayerIndex == 0 && !NetSession.IsHost ? DisplayStyle.Flex : DisplayStyle.None;
        SetConfirming(false);
        SetJoining(false);
        Nav.SetScope(this, _panel, open, _resume);
    }

    // The address field in place of the buttons, with the last address joined
    void SetJoining(bool joining)
    {
        bool was = Joining;
        _buttons.EnableInClassList(HiddenClass, joining);
        _joinPanel.EnableInClassList(HiddenClass, !joining);
        _joinError.EnableInClassList(HiddenClass, true);
        if (joining)
        {
            _joinAddress.value = PlayerPrefs.GetString(JoinAddressKey, "127.0.0.1");
            _joinAddress.Focus();
        }
        else if (was) Nav.Focus(_joinGame);
    }

    void Join()
    {
        string address = _joinAddress.value;
        if (!NetSession.Join(address))
        {
            _joinError.EnableInClassList(HiddenClass, false);
            return;
        }
        PlayerPrefs.SetString(JoinAddressKey, address.Trim());
        SetOpen(false);
    }

    // Back to this machine's own game, in its Hub
    void LeaveGame()
    {
        SetOpen(false);
        NetSession.Leave(null);
    }

    // Leaves at once when nothing would be lost, else asks
    void OnLeaveClicked()
    {
        string question = DungeonHUD.LeaveQuestion();
        if (question == null) Leave();
        else
        {
            _leaveQuestion.text = question;
            SetConfirming(true);
        }
    }

    void SetConfirming(bool confirming)
    {
        bool was = Confirming;
        _buttons.EnableInClassList(HiddenClass, confirming);
        _leaveConfirm.EnableInClassList(HiddenClass, !confirming);
        if (confirming) Nav.Focus(_confirmLeave);
        else if (was) Nav.Focus(_leaveDungeon);
    }

    void Leave()
    {
        SetOpen(false);
        DungeonHUD.LeaveDungeon();
    }

    // Player 2 in the Hub: their character picker (CoopHUD)
    void ChangeCharacter()
    {
        SetOpen(false);
        CoopHUD.OpenPicker();
    }

    // Player 2 stops playing (their managers and this menu go with them)
    void LeaveCoop()
    {
        SetOpen(false);
        CoopManager.Leave(PlayerIndex);
    }

    // Saves and leaves the game scene (GameSceneLoader); the main menu opens on its main screen
    void MainMenu()
    {
        SetOpen(false);
        GameSceneLoader.Load(GameBootstrap.MainMenuScene);
    }

    // Progress is saved on quit (CharacterManager, StatisticsRecorder)
    static void Exit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
