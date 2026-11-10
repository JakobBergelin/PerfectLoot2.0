using System.Collections.Generic;
using Characters;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// The main menu the game starts in. First the player gives a name, which stands in for an account (AccountManager);
/// a new name starts a new play-through. Then Character Select (CharacterSelectScreen: pick or create a character and
/// go on an adventure in the game scene), Options (OptionsHUD), Exit, and Change name back to the name screen. Esc goes
/// back a step. In Character Select a second player can join (Start on a free controller, Enter on a free keyboard)
/// and picks one of the account's characters on the right (CoopPicker); the game starts once both are ready.
/// Changing the name makes player 2 leave.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class MainMenuHUD : MonoBehaviour
{
    const string HiddenClass = "mm-screen--hidden";

    enum Screen { Name, Main, Characters }

    [Header("For Character Select's names and descriptions")]
    public List<CharacterClass> AllClasses = new List<CharacterClass>();
    public List<ResourceDefinition> AllResources = new List<ResourceDefinition>();

    PanelRenderer _panelRenderer;
    bool _bound;
    int _boundVersion = -1;
    Screen _screen = Screen.Name;

    VisualElement _nameScreen, _mainScreen, _characterScreen;
    TextField _nameField;
    Button _continueButton;
    Label _signedIn;
    CharacterSelectScreen _characterSelect;

    // Co-op: player 2's side of Character Select and the join hint
    VisualElement _player2Panel;
    Label _joinHint;
    CoopPicker _player2;
    bool _player2Shown, _starting;

    // Back from a game scene (the in-game menu's Main menu): still signed in, so the main screen, or Character Select
    // when player 2 came back too
    void Awake()
    {
        if (GameSceneLoader.PreviousScene != null && AccountManager.Instance != null)
            _screen = GameInput.PlayerCount > 1 ? Screen.Characters : Screen.Main;
    }

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
        _player2Shown = false;
        GameInput.AllowJoining(this, false);
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
        if (_bound) ShowScreen(_screen);
    }

    bool Bind(VisualElement root)
    {
        _nameScreen = root.Q<VisualElement>("nameScreen");
        _mainScreen = root.Q<VisualElement>("mainScreen");
        _characterScreen = root.Q<VisualElement>("characterScreen");
        _nameField = root.Q<TextField>("nameField");
        _continueButton = root.Q<Button>("continueButton");
        _signedIn = root.Q<Label>("signedIn");
        var characterSelect = root.Q<Button>("characterSelectButton");
        var options = root.Q<Button>("optionsButton");
        var exit = root.Q<Button>("exitButton");
        var changeName = root.Q<Button>("changeNameButton");
        var characterBack = root.Q<Button>("characterBackButton");
        _player2Panel = root.Q<VisualElement>("player2Panel");
        _joinHint = root.Q<Label>("joinHint");
        _characterSelect = new CharacterSelectScreen(AllClasses, AllResources, Adventure);
        if (_nameScreen == null || _mainScreen == null || _characterScreen == null || _nameField == null || _continueButton == null ||
            _signedIn == null || characterSelect == null || options == null || exit == null || changeName == null ||
            characterBack == null || _player2Panel == null || _joinHint == null || !_characterSelect.Bind(root))
        {
            Debug.LogError("MainMenuHUD: MainMenu.uxml is missing elements. Is it the Panel Renderer's Source Asset?", this);
            return false;
        }

        _nameField.maxLength = AccountManager.MaxNameLength;
        _nameField.SetValueWithoutNotify(AccountManager.LastAccountName);
        // Characters a name can't have are dropped as they're typed or pasted
        _nameField.RegisterValueChangedCallback(e =>
        {
            string clean = AccountManager.CleanName(e.newValue);
            if (clean != e.newValue) _nameField.SetValueWithoutNotify(clean);
            UpdateContinueButton();
        });
        _nameField.RegisterCallback<KeyDownEvent>(e =>
        {
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Continue();
        }, TrickleDown.TrickleDown);
        _continueButton.clicked += Continue;

        characterSelect.clicked += () => ShowScreen(Screen.Characters);
        options.clicked += () => OptionsHUD.Open();
        exit.clicked += Exit;
        changeName.clicked += () => ShowScreen(Screen.Name);
        characterBack.clicked += () => ShowScreen(Screen.Main);

        // With a controller: what A and B do on each screen
        UiNav.Register(root);
        UiNav.Hint(_nameScreen, "A Continue");
        UiNav.Hint(_mainScreen, "A Select · B Change name");
        UiNav.Hint(_characterScreen, "A Select · LT/RT Pages · B Back");

        _player2 = new CoopPicker(detailed: true, _characterSelect.Describe);
        _player2.Picked = Player2Picked;
        _player2.Back = Player2Back;
        _player2Panel.Clear();
        _player2Panel.Add(_player2.Element);

        UpdateContinueButton();
        return true;
    }

    void Update()
    {
        if (!_bound) return;
        UpdateCoop();
        if (OptionsHUD.HandlesEscape) return;
        if (_screen == Screen.Characters && MenuPanels.PagePressed != 0) _characterSelect.TurnPage(MenuPanels.PagePressed);
        if (!MenuPanels.BackPressed) return;
        if (_screen == Screen.Main) ShowScreen(Screen.Name);
        else if (_screen == Screen.Characters && !_characterSelect.Cancel()) ShowScreen(Screen.Main);
    }

    // ---------- Co-op ----------

    // Joining, player 2's picker beside Character Select, and the start once both are ready. Player 2 keeps going while
    // player 1 is in Options.
    void UpdateCoop()
    {
        bool characters = _screen == Screen.Characters;
        GameInput.AllowJoining(this, characters);
        var device = characters ? GameInput.JoinPressed() : null;
        if (device != null) CoopManager.Join(device);

        string hint = characters ? CoopManager.JoinHint() : null;
        _joinHint.text = hint ?? "";
        _joinHint.EnableInClassList("coop--hidden", hint == null);

        var player2 = GameInput.For(1);
        bool show = characters && player2 != null;
        if (show != _player2Shown)
        {
            _player2Shown = show;
            _player2Panel.EnableInClassList(HiddenClass, !show);
            if (show) _player2.Open(null, showLeave: false);
        }
        if (!characters) return;
        _characterSelect.Update();
        if (!show) return;

        var account = AccountManager.Instance;
        var ready1 = account != null ? account.SelectedCharacterOf(0) : null;
        var ready2 = account != null ? account.SelectedCharacterOf(1) : null;
        _player2.SetOwnTag("Ready");
        _player2.SetActions(ready2 != null && ready2 == _player2.Highlighted ? "Not ready" : "Ready", ready2 != null ? "Not ready" : "Leave");
        _player2.SetStatus(ready2 == null ? "" : ready1 == null ? "Waiting for Player 1" : "");
        _player2.Update();
        if (ready1 != null && ready2 != null) StartAdventure();
    }

    // Ready with this character, or not ready when it already is
    void Player2Picked(AccountCharacter character)
    {
        var account = AccountManager.Instance;
        if (account == null) return;
        if (account.SelectedCharacterOf(1) == character) account.ClearSelection(1);
        else account.SelectCharacter(character.Guid, 1);
    }

    // Not ready, else leaves
    void Player2Back()
    {
        var account = AccountManager.Instance;
        if (account != null && account.SelectedCharacterOf(1) != null) account.ClearSelection(1);
        else CoopManager.Leave(1);
    }

    void ShowScreen(Screen screen)
    {
        _screen = screen;
        // A new name can be another account: player 2 leaves. Character Select starts with nobody ready
        if (screen == Screen.Name) CoopManager.Leave(1);
        if (screen == Screen.Characters && AccountManager.Instance != null)
        {
            AccountManager.Instance.ClearSelection(0);
            AccountManager.Instance.ClearSelection(1);
        }
        _nameScreen.EnableInClassList(HiddenClass, screen != Screen.Name);
        _mainScreen.EnableInClassList(HiddenClass, screen != Screen.Main);
        _characterScreen.EnableInClassList(HiddenClass, screen != Screen.Characters);
        var shown = screen == Screen.Name ? _nameScreen : screen == Screen.Main ? _mainScreen : _characterScreen;
        UiNav.SetScope(this, shown, true);
        if (screen == Screen.Name)
        {
            // A controller can't type: a ready name to accept with A
            if (GameInput.UsingGamepad)
            {
                if (!AccountManager.IsValidName(_nameField.value)) _nameField.value = "Player";
                UiNav.Focus(_continueButton);
                return;
            }
            // Ready to type (after layout, or the hidden field can't take focus yet)
            _nameField.schedule.Execute(() =>
            {
                _nameField.Focus();
                _nameField.SelectAll();
            });
            return;
        }

        _nameField.Blur();
        if (screen == Screen.Main)
        {
            var account = AccountManager.Instance;
            _signedIn.text = $"Signed in as {(account != null ? account.AccountName : AccountManager.DefaultAccountName)}";
            UiNav.Focus(_mainScreen.Q<Button>("characterSelectButton"));
        }
        else _characterSelect.Open();
    }

    void UpdateContinueButton() => _continueButton.SetEnabled(AccountManager.IsValidName(_nameField.value));

    void Continue()
    {
        if (_screen != Screen.Name || !AccountManager.IsValidName(_nameField.value)) return;
        var account = AccountManager.Instance;
        if (account == null)
        {
            Debug.LogError("MainMenuHUD: no AccountManager in the scene, so the name can't be used.", this);
            return;
        }
        account.SignIn(_nameField.value);
        ShowScreen(Screen.Main);
    }

    // Plays the character in the town (GameBootstrap). In co-op it's Ready instead (again: not ready), and the game
    // starts once player 2 is ready too (UpdateCoop)
    void Adventure(AccountCharacter character)
    {
        var account = AccountManager.Instance;
        if (account == null) return;
        if (GameInput.PlayerCount > 1)
        {
            if (account.SelectedCharacterOf(0) == character) account.ClearSelection(0);
            else account.SelectCharacter(character.Guid);
            return;
        }
        if (account.SelectCharacter(character.Guid)) StartAdventure();
    }

    void StartAdventure()
    {
        if (_starting) return;
        string town = GameBootstrap.TownScene;
        if (!Application.CanStreamedLevelBeLoaded(town))
        {
            Debug.LogError($"MainMenuHUD: scene '{town}' isn't in Build Settings.", this);
            return;
        }
        _starting = GameSceneLoader.Load(town);
    }

    static void Exit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
