using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// While a player stands within a Portal's range, shows the portal's name and an Enter button pinned above it on screen
/// (the nearest portal in range, player 1's first). Enter, or a player's Interact, loads the portal's scene with the
/// current characters (GameSceneLoader). With several players, couch or online, every living player has to choose it:
/// each one's Interact (or the mouse's player's click) makes them ready for the portal they stand at, until they walk
/// away (NetPortal keeps the choice on their player, so every machine sees it), and the card says who is ready and who
/// it waits for. The host enters once everyone is ready, and takes the other machines along. Going into a dungeon it
/// also warns about equipped skills whose requirements aren't met (they can't be used there). Hidden while player 1 has
/// a menu panel open. Interact goes to the portal before any loot in reach (AtPortal).
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class PortalHUD : MonoBehaviour
{
    const string HiddenClass = "portal-card--hidden";

    [Tooltip("Height above the portal's origin where the card's bottom edge sits, in meters")]
    public float CardHeight = 3f;

    // Per local player: the portal they stand at (its scene; empty for none)
    static readonly FixedString64Bytes[] _at = new FixedString64Bytes[GameInput.MaxPlayers];
    readonly float3[] _portalPositions = new float3[GameInput.MaxPlayers];
    readonly FixedString64Bytes[] _portalNames = new FixedString64Bytes[GameInput.MaxPlayers];

    /// <summary>True while this player stands at a portal: their Interact is for it (GroundLootHUD leaves the loot alone).</summary>
    public static bool AtPortal(int playerIndex) =>
        playerIndex >= 0 && playerIndex < _at.Length && !_at[playerIndex].IsEmpty;

    PanelRenderer _panelRenderer;
    int _boundVersion = -1;
    VisualElement _card;
    Label _name, _status;
    Button _enter;

    bool _hovered;
    bool _entering;
    FixedString64Bytes _scene;      // Of the portal the card shows; empty while hidden
    int _labelVersion = -1;         // GameInput.Version the Enter button's key label is from

    void OnEnable()
    {
        _panelRenderer = GetComponent<PanelRenderer>();
        _panelRenderer.RegisterUIReloadCallback(OnUIReload);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        _panelRenderer.UnregisterUIReloadCallback(OnUIReload);
        _boundVersion = -1;
        _card = null;
        for (int i = 0; i < _at.Length; i++) _at[i] = default;
        SetHovered(false);
    }

    // The HUD is kept between scenes (GameBootstrap's rig): the portal entered is behind us
    void OnSceneLoaded(Scene scene, LoadSceneMode mode) => _entering = false;

    void OnUIReload(PanelRenderer panelRenderer, VisualElement root, int version)
    {
        if (_card != null && version == _boundVersion) return;

        // The Panel Renderer's own container isn't part of the UXML; make it fill the screen without taking clicks
        root.style.position = Position.Absolute;
        root.style.left = 0;
        root.style.top = 0;
        root.style.right = 0;
        root.style.bottom = 0;
        root.pickingMode = PickingMode.Ignore;

        _card = root.Q<VisualElement>("portalCard");
        _name = root.Q<Label>("portalName");
        _enter = root.Q<Button>("enterButton");
        _boundVersion = version;
        if (_card == null || _name == null || _enter == null)
        {
            Debug.LogError("PortalHUD: PortalHUD.uxml is missing elements. Is it the Panel Renderer's Source Asset?", this);
            _card = null;
            return;
        }

        // Who is ready in co-op, under the name
        _status = new Label { pickingMode = PickingMode.Ignore };
        _status.AddToClassList("portal-status");
        _card.Insert(_card.IndexOf(_name) + 1, _status);

        _enter.clicked += () => Choose(GameInput.MouseOwner);
        _labelVersion = -1;
        // Clicking the button mustn't also use a skill
        _card.RegisterCallback<PointerEnterEvent>(_ => SetHovered(true));
        _card.RegisterCallback<PointerLeaveEvent>(_ => SetHovered(false));
        Hide();
    }

    void LateUpdate()
    {
        if (_card == null || _card.panel == null) return;
        var camera = Camera.main;
        FindPortals();
        if (NetSession.IsHost && !_entering) EnterWhenEveryoneIsReady();

        // Each player's Interact chooses the portal they stand at
        for (int i = 0; i < GameInput.PlayerCount; i++)
        {
            var menus = MenuPanels.For(i);
            if (!_entering && AtPortal(i) && !menus.IsPanelOpen && !menus.IsTyping && !UiNav.For(i).HasGamepadInput &&
                GameInput.For(i).Actions.Player.Interact.WasPressedThisFrame())
                Choose(i);
        }

        // The card: the first player's portal (player 1's, else player 2's)
        int shown = AtPortal(0) ? 0 : AtPortal(1) ? 1 : -1;
        if (_entering || camera == null || MenuPanels.IsPanelOpen || shown < 0)
        {
            Hide();
            return;
        }

        Vector3 anchor = (Vector3)_portalPositions[shown] + Vector3.up * CardHeight;
        if (camera.WorldToViewportPoint(anchor).z < 0f)
        {
            Hide();
            return;
        }

        _scene = _at[shown];
        _name.text = _portalNames[shown].ToString();
        Vector2 panelPosition = RuntimePanelUtils.CameraTransformWorldToPanel(_card.panel, anchor, camera);
        _card.style.left = panelPosition.x;
        _card.style.top = panelPosition.y;
        _card.RemoveFromClassList(HiddenClass);
        ShowStatus();

        if (_labelVersion != GameInput.Version)
        {
            _labelVersion = GameInput.Version;
            string key = GameInput.Label(GameInput.Player.Interact);
            _enter.text = string.IsNullOrEmpty(key) ? "Enter" : $"Enter ({key})";
        }
    }

    // Where each local player stands (the nearest portal in range, along the ground)
    void FindPortals()
    {
        for (int i = 0; i < _at.Length; i++)
        {
            _at[i] = default;
            var world = World.DefaultGameObjectInjectionWorld;
            var characters = CharacterManager.For(i);
            if (GameInput.For(i) == null || characters == null || world == null || !world.IsCreated) continue;
            var entityManager = world.EntityManager;
            var player = characters.Player;
            if (player == Entity.Null || !entityManager.Exists(player) || !entityManager.HasComponent<LocalToWorld>(player)) continue;
            if (!NetPortal.TryFindPortal(entityManager, entityManager.GetComponentData<LocalToWorld>(player).Position, out var portal, out var position)) continue;
            _at[i] = portal.Scene;
            _portalNames[i] = portal.DisplayName;
            _portalPositions[i] = position;
        }
    }

    // Every player in the game, this machine's and the others', in the same order everywhere (by machine, then couch
    // player), named Player 1, 2, ... in that order: their portal choice, and whether they're alive. Mirror Image copies,
    // made from the player prefab, have no owner and don't count.
    static NativeList<(int Order, Entity Player, FixedString64Bytes Portal, bool Alive)> Players(EntityManager entityManager)
    {
        var players = new NativeList<(int, Entity, FixedString64Bytes, bool)>(Allocator.Temp);
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<NetPlayer>(), ComponentType.ReadOnly<GhostOwner>(),
            ComponentType.ReadOnly<NetPlayerActions>(), ComponentType.ReadOnly<Health>(), ComponentType.ReadOnly<LocalToWorld>());
        using var entities = query.ToEntityArray(Allocator.Temp);
        foreach (var entity in entities)
        {
            int owner = entityManager.GetComponentData<GhostOwner>(entity).NetworkId;
            if (owner == 0) continue;
            int order = owner * GameInput.MaxPlayers + entityManager.GetComponentData<NetPlayer>(entity).LocalIndex;
            players.Add((order, entity, entityManager.GetComponentData<NetPlayerActions>(entity).Portal, entityManager.GetComponentData<Health>(entity).CurrentValue > 0f));
        }
        players.Sort(new ByOrder());
        return players;
    }

    struct ByOrder : System.Collections.Generic.IComparer<(int Order, Entity Player, FixedString64Bytes Portal, bool Alive)>
    {
        public int Compare((int Order, Entity Player, FixedString64Bytes Portal, bool Alive) a, (int Order, Entity Player, FixedString64Bytes Portal, bool Alive) b) => a.Order.CompareTo(b.Order);
    }

    // The host: a player who walked away from the portal they chose isn't ready anymore; once every living player has
    // chosen the same portal, it opens for everyone
    void EnterWhenEveryoneIsReady()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;
        var entityManager = world.EntityManager;
        using var players = Players(entityManager);
        FixedString64Bytes scene = default;
        bool everyone = true;
        foreach (var (_, player, chosen, alive) in players)
        {
            var portal = chosen;
            var position = entityManager.GetComponentData<LocalToWorld>(player).Position;
            if (!portal.IsEmpty && (!NetPortal.TryFindPortal(entityManager, position, out var at, out _) || at.Scene != portal))
            {
                NetPortal.SetChoice(entityManager, player, default);
                portal = default;
            }
            if (!alive) continue;
            if (portal.IsEmpty || (!scene.IsEmpty && portal != scene)) everyone = false;
            else scene = portal;
        }
        if (!everyone || scene.IsEmpty) return;
        _entering = GameSceneLoader.Load(scene.ToString());
        if (_entering) Hide();
    }

    // The players in the level: the ones who have to choose a portal
    static bool InLevel(int playerIndex)
    {
        var characters = CharacterManager.For(playerIndex);
        return GameInput.For(playerIndex) != null && characters != null && characters.Player != Entity.Null;
    }

    // "Player 1 ready · waiting for Player 2" with several players (Players: every machine's, alive), then the
    // requirement warnings
    void ShowStatus()
    {
        string text = "";
        var world = World.DefaultGameObjectInjectionWorld;
        if (world != null && world.IsCreated)
        {
            using var players = Players(world.EntityManager);
            if (players.Length > 1)
                for (int i = 0; i < players.Length; i++)
                {
                    if (!players[i].Alive) continue;
                    bool ready = players[i].Portal == _scene;
                    if (text.Length > 0) text += " · ";
                    text += ready ? $"Player {i + 1} ready" : $"waiting for Player {i + 1}";
                }
        }
        string warnings = RequirementWarnings();
        if (warnings.Length > 0) text += (text.Length > 0 ? "\n" : "") + warnings;
        if (_status.text != text) _status.text = text;
        _status.EnableInClassList(HiddenClass, text.Length == 0);
    }

    // Leaving the Hub: "Fireball needs 2 Int" for each equipped skill whose requirements aren't met, since it can't be
    // used there (ActiveSkillSlot.MeetsRequirements; entering still works). In co-op each player's own, named
    string RequirementWarnings()
    {
        if (_scene.IsEmpty || _scene.ToString() == GameBootstrap.TownScene) return "";
        var lines = new System.Collections.Generic.List<string>();
        for (int i = 0; i < GameInput.PlayerCount; i++)
        {
            var manager = ActiveSkillManager.For(i);
            if (!InLevel(i) || manager == null) continue;
            var stats = SkillTooltip.CurrentStats(i);
            foreach (var slot in new[] { manager.Slot1, manager.Slot2, manager.Slot3, manager.Slot4 })
            {
                if (slot == null || slot.IsEmpty || slot.MeetsRequirements) continue;
                var skill = slot.EquippedSkill;
                var requirements = skill.Requirements;
                var missing = new System.Collections.Generic.List<string>();
                for (int k = 0; k < 3; k++)
                {
                    float have = stats is StatBlock s ? ActiveSkills.SkillRequirements.Attributes(s)[k] : 0f;
                    if (requirements[k] > have) missing.Add($"{requirements[k]} {ActiveSkills.SkillRequirements.Short[k]}");
                }
                string name = string.IsNullOrEmpty(skill.Name) ? skill.name : skill.Name;
                string who = GameInput.PlayerCount > 1 ? $"Player {i + 1}: " : "";
                lines.Add($"<color=#E05050>{who}{name} needs {string.Join(" and ", missing)}</color>");
            }
        }
        return string.Join("\n", lines);
    }

    void Hide()
    {
        _scene = default;
        if (_card != null) _card.AddToClassList(HiddenClass);
        SetHovered(false);
    }

    void SetHovered(bool hovered)
    {
        if (_hovered == hovered) return;
        _hovered = hovered;
        MenuPanels.SetBlocking(this, hovered);
    }

    // The player chooses the portal they stand at; once everyone in the game has chosen it, the host opens it
    // (EnterWhenEveryoneIsReady)
    void Choose(int playerIndex)
    {
        if (_entering || !AtPortal(playerIndex)) return;
        NetPortal.Choose(playerIndex, _at[playerIndex]);
    }
}
