using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Couch co-op: Join adds player 2 (their device, GameInput, and their character, AccountManager) and Leave removes
/// them; both work from menus and game scenes. In a game scene this, on the rig, gives player 2 their own copy of
/// player 1's per-player managers (character, inventory, skills, passives, with PlayerIndex 1) while they're joined
/// with a character, and removes them (and their player entity) when they leave.
/// </summary>
public class CoopManager : MonoBehaviour
{
    [Header("Player 1's per-player managers, copied for player 2")]
    public CharacterManager CharacterPrefab;
    public InventoryManager InventoryPrefab;
    public ActiveSkillManager SkillsPrefab;
    public PassiveSkillManager PassivesPrefab;

    GameObject _player2;

    /// <summary>
    /// Adds a player on this device, playing this character (one the other player doesn't have). False when nobody more
    /// can join, the device is in use, or the character can't be picked.
    /// </summary>
    public static bool Join(InputDevice device, AccountCharacter character)
    {
        var account = AccountManager.Instance;
        if (account == null || character == null || !GameInput.IsFree(device)) return false;
        var controls = GameInput.Join(device);
        if (controls == null) return false;
        if (account.SelectCharacter(character.Guid, controls.Index)) return true;
        GameInput.Leave(controls.Index);
        return false;
    }

    /// <summary>Player 2 stops playing: their character is saved and their managers and entity go away.</summary>
    public static void Leave(int playerIndex)
    {
        if (playerIndex <= 0) return;
        if (AccountManager.Instance != null) AccountManager.Instance.ClearSelection(playerIndex);
        GameInput.Leave(playerIndex);
    }

    void Update()
    {
        var account = AccountManager.Instance;
        bool joined = GameInput.PlayerCount > 1 && account != null && account.SelectedCharacterOf(1) != null;
        if (joined && _player2 == null) CreatePlayer2();
        else if (!joined && _player2 != null)
        {
            Destroy(_player2);
            _player2 = null;
        }
    }

    // Made inactive, so every manager knows it's player 2's before its Awake runs
    void CreatePlayer2()
    {
        if (CharacterPrefab == null || InventoryPrefab == null || SkillsPrefab == null || PassivesPrefab == null)
        {
            Debug.LogError("CoopManager is missing a manager prefab, so player 2 can't play.", this);
            enabled = false;
            return;
        }

        _player2 = new GameObject("Player2");
        _player2.SetActive(false);
        _player2.transform.SetParent(transform, false);
        Instantiate(CharacterPrefab, _player2.transform).PlayerIndex = 1;
        Instantiate(InventoryPrefab, _player2.transform).PlayerIndex = 1;
        Instantiate(SkillsPrefab, _player2.transform).PlayerIndex = 1;
        Instantiate(PassivesPrefab, _player2.transform).PlayerIndex = 1;
        _player2.SetActive(true);
    }

    [ContextMenu("Join player 2 (test: a free device, the first character player 1 doesn't play)")]
    void JoinTest()
    {
        var account = AccountManager.Instance;
        if (!Application.isPlaying || account == null) return;
        var device = GameInput.FreeDevice();
        foreach (var character in account.Characters)
        {
            if (character == account.SelectedCharacter) continue;
            Debug.Log(Join(device, character) ? $"Player 2 joined on {device} as {character.Name}." : $"Player 2 couldn't join on {device}.", this);
            return;
        }
        Debug.LogWarning("No character for player 2: the account needs another one.", this);
    }

    [ContextMenu("Player 2 leaves")]
    void LeaveTest() => Leave(1);
}
