using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The in-game menu in GameMenu.uxml: Resume, Options (OptionsHUD), Main menu and Exit game. Esc or Start opens it when
/// nothing else is open, and closes it again; while something is open, Esc closes that first. A menu panel like the
/// others (MenuPanels), so gameplay input is ignored while it's open. The game keeps running.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class GameMenuHUD : MonoBehaviour
{
    const string HiddenClass = "gm--hidden";

    PanelRenderer _panelRenderer;
    bool _bound;
    int _boundVersion = -1;
    bool _open;
    VisualElement _root;

    void OnEnable()
    {
        _panelRenderer = GetComponent<PanelRenderer>();
        _panelRenderer.RegisterUIReloadCallback(OnUIReload);
        MenuPanels.OpenChanged += OnMenuOpenChanged;
    }

    void OnDisable()
    {
        MenuPanels.OpenChanged -= OnMenuOpenChanged;
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
        root.pickingMode = PickingMode.Ignore;

        _bound = Bind(root);
        _boundVersion = version;
        if (_bound) SetOpen(_open);
    }

    bool Bind(VisualElement root)
    {
        _root = root.Q<VisualElement>("gameMenuRoot");
        var resume = root.Q<Button>("resumeButton");
        var options = root.Q<Button>("optionsButton");
        var mainMenu = root.Q<Button>("mainMenuButton");
        var exit = root.Q<Button>("exitButton");
        if (_root == null || resume == null || options == null || mainMenu == null || exit == null)
        {
            Debug.LogError("GameMenuHUD: GameMenu.uxml is missing elements. Is it the Panel Renderer's Source Asset?", this);
            return false;
        }

        resume.clicked += () => SetOpen(false);
        options.clicked += OptionsHUD.Open;
        mainMenu.clicked += MainMenu;
        exit.clicked += Exit;
        return true;
    }

    void Update()
    {
        if (!_bound || !MenuPanels.EscapePressed || OptionsHUD.HandlesEscape) return;

        if (_open) SetOpen(false);
        // This Esc is for what is open, or already closed something this frame
        else if (!MenuPanels.IsAnyOpen && !MenuPanels.ChangedThisFrame) SetOpen(true);
    }

    void OnMenuOpenChanged(object openPanel)
    {
        if (_open && openPanel != null && !ReferenceEquals(openPanel, this)) SetOpen(false);
    }

    void SetOpen(bool open)
    {
        _open = open;
        MenuPanels.SetOpen(this, open);
        if (!_bound) return;
        _root.EnableInClassList(HiddenClass, !open);
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
