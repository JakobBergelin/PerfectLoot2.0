using UnityEngine;

/// <summary>Runs UiNav, MenuPanels' tabs and PlayerHud once a frame, for the whole session (made at start, kept between scenes).</summary>
public class UiNavigator : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        var navigator = new GameObject(nameof(UiNavigator));
        DontDestroyOnLoad(navigator);
        navigator.AddComponent<UiNavigator>();
    }

    void Update()
    {
        // Every frame: UI Toolkit's input may enable the project-wide actions again
        UiNav.DisableBuiltInNavigation();
        UiNav.Update();
        MenuPanels.UpdateTabs();
        PlayerHud.Update();
    }
}
