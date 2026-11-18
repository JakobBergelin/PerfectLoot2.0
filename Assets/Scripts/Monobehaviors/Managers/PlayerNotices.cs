using UnityEngine;

/// <summary>
/// Short messages for one local player, e.g. "New skill: Teleport", shown for a few seconds above their experience bar
/// (ExperienceBarHUD). Posted by ActiveSkillManager and PassiveSkillManager while a new character is eased into its
/// skills and passive slots.
/// </summary>
public static class PlayerNotices
{
    public const float Seconds = 6f;

    static readonly string[] _text = new string[GameInput.MaxPlayers];
    static readonly float[] _until = new float[GameInput.MaxPlayers];

    /// <summary>Shows a message to this player. One still showing stays, with the new one on the line under it.</summary>
    public static void Post(int playerIndex, string text)
    {
        if (playerIndex < 0 || playerIndex >= _text.Length || string.IsNullOrEmpty(text)) return;
        bool showing = Time.unscaledTime < _until[playerIndex];
        _text[playerIndex] = showing ? _text[playerIndex] + "\n" + text : text;
        _until[playerIndex] = Time.unscaledTime + Seconds;
    }

    /// <summary>The player's message while it shows, else null.</summary>
    public static string Current(int playerIndex) =>
        playerIndex >= 0 && playerIndex < _text.Length && Time.unscaledTime < _until[playerIndex] ? _text[playerIndex] : null;

    /// <summary>The key or button that opens the Skills panel for this player, e.g. "K".</summary>
    public static string SkillsKey(int playerIndex)
    {
        var controls = GameInput.For(playerIndex);
        var action = controls?.Actions.asset.FindAction("Player/Skills");
        var label = action != null ? controls.Label(action) : "";
        return string.IsNullOrEmpty(label) ? "the Skills key" : label;
    }

    // Statics survive entering Play mode when domain reload is off
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        System.Array.Clear(_text, 0, _text.Length);
        System.Array.Clear(_until, 0, _until.Length);
    }
}
