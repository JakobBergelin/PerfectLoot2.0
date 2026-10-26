using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// The main menu the game starts in. First the player gives a name, which stands in for an account (AccountManager);
/// a new name starts a new play-through. Then Character Select (loads the game scene for now), Options (not yet),
/// Exit, and Change name (also Esc) back to the name screen.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class MainMenuHUD : MonoBehaviour
{
    const string HiddenClass = "mm-screen--hidden";

    [Tooltip("Scene Character Select loads, until there is a real character select. Must be in Build Settings.")]
    public string GameScene = "SampleScene";

    PanelRenderer _panelRenderer;
    bool _bound;
    int _boundVersion = -1;
    bool _onMainScreen;

    VisualElement _nameScreen, _mainScreen;
    TextField _nameField;
    Button _continueButton;
    Label _signedIn;

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
        if (_bound) ShowScreen(_onMainScreen);
    }

    bool Bind(VisualElement root)
    {
        _nameScreen = root.Q<VisualElement>("nameScreen");
        _mainScreen = root.Q<VisualElement>("mainScreen");
        _nameField = root.Q<TextField>("nameField");
        _continueButton = root.Q<Button>("continueButton");
        _signedIn = root.Q<Label>("signedIn");
        var characterSelect = root.Q<Button>("characterSelectButton");
        var options = root.Q<Button>("optionsButton");
        var exit = root.Q<Button>("exitButton");
        var changeName = root.Q<Button>("changeNameButton");
        if (_nameScreen == null || _mainScreen == null || _nameField == null || _continueButton == null || _signedIn == null ||
            characterSelect == null || options == null || exit == null || changeName == null)
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

        characterSelect.clicked += CharacterSelect;
        options.SetEnabled(false);      // Hooked up later
        exit.clicked += Exit;
        changeName.clicked += () => ShowScreen(false);

        UpdateContinueButton();
        return true;
    }

    void Update()
    {
        if (_bound && _onMainScreen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            ShowScreen(false);
    }

    void ShowScreen(bool main)
    {
        _onMainScreen = main;
        _nameScreen.EnableInClassList(HiddenClass, main);
        _mainScreen.EnableInClassList(HiddenClass, !main);
        if (main)
        {
            _nameField.Blur();
            var account = AccountManager.Instance;
            _signedIn.text = $"Signed in as {(account != null ? account.AccountName : AccountManager.DefaultAccountName)}";
        }
        else
        {
            // Ready to type (after layout, or the hidden field can't take focus yet)
            _nameField.schedule.Execute(() =>
            {
                _nameField.Focus();
                _nameField.SelectAll();
            });
        }
    }

    void UpdateContinueButton() => _continueButton.SetEnabled(AccountManager.IsValidName(_nameField.value));

    void Continue()
    {
        if (_onMainScreen || !AccountManager.IsValidName(_nameField.value)) return;
        var account = AccountManager.Instance;
        if (account == null)
        {
            Debug.LogError("MainMenuHUD: no AccountManager in the scene, so the name can't be used.", this);
            return;
        }
        account.SignIn(_nameField.value);
        ShowScreen(true);
    }

    void CharacterSelect()
    {
        if (!Application.CanStreamedLevelBeLoaded(GameScene))
        {
            Debug.LogError($"MainMenuHUD: scene '{GameScene}' isn't in Build Settings.", this);
            return;
        }
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
