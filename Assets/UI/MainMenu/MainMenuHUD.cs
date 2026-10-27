using System.Collections.Generic;
using Characters;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// The main menu the game starts in. First the player gives a name, which stands in for an account (AccountManager);
/// a new name starts a new play-through. Then Character Select (CharacterSelectScreen: pick or create a character and
/// go on an adventure in the game scene), Options (not yet), Exit, and Change name back to the name screen. Esc goes
/// back a step.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class MainMenuHUD : MonoBehaviour
{
    const string HiddenClass = "mm-screen--hidden";

    enum Screen { Name, Main, Characters }

    [Tooltip("Scene Adventure loads with the selected character. Must be in Build Settings.")]
    public string GameScene = "SampleScene";

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
        _characterSelect = new CharacterSelectScreen(AllClasses, AllResources, Adventure);
        if (_nameScreen == null || _mainScreen == null || _characterScreen == null || _nameField == null || _continueButton == null ||
            _signedIn == null || characterSelect == null || options == null || exit == null || changeName == null ||
            characterBack == null || !_characterSelect.Bind(root))
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
        options.SetEnabled(false);      // Hooked up later
        exit.clicked += Exit;
        changeName.clicked += () => ShowScreen(Screen.Name);
        characterBack.clicked += () => ShowScreen(Screen.Main);

        UpdateContinueButton();
        return true;
    }

    void Update()
    {
        if (!_bound || Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;
        if (_screen == Screen.Main) ShowScreen(Screen.Name);
        else if (_screen == Screen.Characters && !_characterSelect.Cancel()) ShowScreen(Screen.Main);
    }

    void ShowScreen(Screen screen)
    {
        _screen = screen;
        _nameScreen.EnableInClassList(HiddenClass, screen != Screen.Name);
        _mainScreen.EnableInClassList(HiddenClass, screen != Screen.Main);
        _characterScreen.EnableInClassList(HiddenClass, screen != Screen.Characters);
        if (screen == Screen.Name)
        {
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

    // Plays the character in the game scene
    void Adventure(AccountCharacter character)
    {
        if (!Application.CanStreamedLevelBeLoaded(GameScene))
        {
            Debug.LogError($"MainMenuHUD: scene '{GameScene}' isn't in Build Settings.", this);
            return;
        }
        if (AccountManager.Instance == null || !AccountManager.Instance.SelectCharacter(character.Guid)) return;
        SceneManager.LoadScene(GameScene);
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
