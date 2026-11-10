using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Couch co-op: Join adds player 2 (their device, GameInput; they pick their character next, AccountManager) and Leave
/// removes them; both work from menus and game scenes. Character Select (MainMenuHUD) and the Hub (CoopHUD) let them
/// join and pick. In a game scene this, on the rig, gives player 2 their own copy of player 1's per-player managers
/// and HUDs (Player2Copies: character, inventory, skills, their bars, panels and Start menu; IPerPlayer, with
/// PlayerIndex 1) while they're joined with a character, new ones when they pick another, and removes them (and their
/// player entity) when they leave. Its Settings (CoopSettings) limit how far apart the players get (CoopTetherSystem).
/// </summary>
public class CoopManager : MonoBehaviour
{
    [Tooltip("The rig's per-player managers and HUDs (player 1's); player 2 gets fresh copies of them from the rig prefab")]
    public List<GameObject> Player2Copies = new List<GameObject>();
    [Tooltip("How far apart the players can get (pushed to CoopRules every frame, so it can be tuned in Play mode)")]
    public CoopSettings Settings;

    GameObject _player2;
    AccountCharacter _player2Character;     // The one _player2's managers play

    /// <summary>
    /// Adds a player on this device; they pick their character next (AccountManager.SelectCharacter with their index:
    /// Character Select's or the Hub's picker). False when nobody more can join or the device is in use.
    /// </summary>
    public static bool Join(InputDevice device) => GameInput.IsFree(device) && GameInput.Join(device) != null;

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

    /// <summary>
    /// "Player 2: press Start to join" with the buttons of the free devices, or null when nobody can join now: no
    /// screen allows it (GameInput.CanJoin), no device is free, or the account has no character for player 2.
    /// </summary>
    public static string JoinHint()
    {
        var account = AccountManager.Instance;
        if (!GameInput.CanJoin || account == null || account.Characters.Count < 2) return null;
        bool gamepad = false, keyboard = false;
        foreach (var device in InputSystem.devices)
        {
            if (!GameInput.IsFree(device)) continue;
            gamepad |= device is Gamepad;
            keyboard |= device is Keyboard;
        }
        if (!gamepad && !keyboard) return null;
        string keys = gamepad && keyboard ? "Start or Enter" : gamepad ? "Start" : "Enter";
        return $"Player 2: press {keys} to join";
    }

    /// <summary>Player 2 stops playing: their character is saved and their managers and entity go away.</summary>
    public static void Leave(int playerIndex)
    {
        if (playerIndex <= 0) return;
        if (AccountManager.Instance != null) AccountManager.Instance.ClearSelection(playerIndex);
        GameInput.Leave(playerIndex);
    }

    // Player 2 picking another character (the Hub's picker) gets new managers. The old ones are only gone at the end of
    // the frame, so the new ones come the frame after
    void Update()
    {
        var account = AccountManager.Instance;
        var character = GameInput.PlayerCount > 1 && account != null ? account.SelectedCharacterOf(1) : null;
        if (_player2 != null && character != _player2Character)
        {
            Destroy(_player2);
            _player2 = null;
        }
        if (character != null && _player2 == null && CharacterManager.For(1) == null) CreatePlayer2(character);
    }

    // The rules singleton in the current world (each scene has a new one)
    void LateUpdate()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (Settings == null || world == null || !world.IsCreated) return;
        var entityManager = world.EntityManager;
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadWrite<CoopRules>());
        var entity = query.IsEmpty ? entityManager.CreateEntity(typeof(CoopRules)) : query.GetSingletonEntity();
        var rules = entityManager.GetComponentData<CoopRules>(entity);
        Settings.ApplyTo(ref rules);
        entityManager.SetComponentData(entity, rules);
    }

    // Made inactive, so every copy knows it's player 2's before its Awake runs
    void CreatePlayer2(AccountCharacter character)
    {
        _player2Character = character;
        _player2 = new GameObject("Player2");
        _player2.SetActive(false);
        _player2.transform.SetParent(transform, false);
        var rigAsset = GameBootstrap.RigPrefab;
        foreach (var original in Player2Copies)
        {
            if (original == null) continue;
            // From the rig prefab itself, not player 1's: theirs has their skills and items in its slots by now
            var source = rigAsset != null ? rigAsset.transform.Find(original.name) : null;
            var copy = Instantiate(source != null ? source.gameObject : original, _player2.transform);
            copy.name = original.name;
            foreach (var perPlayer in copy.GetComponentsInChildren<IPerPlayer>(true)) perPlayer.PlayerIndex = 1;
        }
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
