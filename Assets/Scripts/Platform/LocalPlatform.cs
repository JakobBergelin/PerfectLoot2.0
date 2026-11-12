using System.Collections.Generic;

/// <summary>
/// No platform (development, a build without Steam): games join each other by IP address (the game menu's Join a game).
/// Keeps the presence it is given, so it can be read while testing, and knows no friends.
/// </summary>
public class LocalPlatform : IPlatform
{
    static readonly PlatformFriend[] NoFriends = new PlatformFriend[0];

    readonly Dictionary<string, string> _presence = new Dictionary<string, string>();

    public string Name => "Local";
    public string PlayerName => null;

    /// <summary>What SetPresence was given (for tests and debugging).</summary>
    public IReadOnlyDictionary<string, string> Presence => _presence;

    public void SetPresence(string key, string value)
    {
        if (string.IsNullOrEmpty(value)) _presence.Remove(key);
        else _presence[key] = value;
    }

    public void ClearPresence() => _presence.Clear();
    public IReadOnlyList<PlatformFriend> FriendsPlaying() => NoFriends;
    public void Update() { }
    public void Shutdown() => _presence.Clear();
}
