using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// While a player stands within a Portal's range, shows the portal's name and an Enter button pinned above it on screen
/// (the nearest portal in range, player 1's first). Enter, or a player's Interact, loads the portal's scene with the
/// current characters (GameSceneLoader). In co-op every living player has to choose it: each one's Interact (or the
/// mouse's player's click) makes them ready for the portal they stand at, until they walk away, and the card says who
/// is ready and who it waits for. Going into a dungeon it also warns about equipped skills whose requirements aren't
/// met (they can't be used there). Hidden while player 1 has a menu panel open. Interact goes to the portal before
/// any loot in reach (AtPortal).
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class PortalHUD : MonoBehaviour
{
    const string HiddenClass = "portal-card--hidden";

    [Tooltip("Height above the portal's origin where the card's bottom edge sits, in meters")]
    public float CardHeight = 3f;

    // Per player: the portal they stand at (its scene; empty for none), and the one they chose
    static readonly FixedString64Bytes[] _at = new FixedString64Bytes[GameInput.MaxPlayers];
    readonly FixedString64Bytes[] _ready = new FixedString64Bytes[GameInput.MaxPlayers];
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
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _entering = false;
        for (int i = 0; i < _ready.Length; i++) _ready[i] = default;
    }

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

    // Where each player stands (the nearest portal in range, along the ground); a player who walked away isn't ready
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
            if (!TryFindPortal(entityManager, entityManager.GetComponentData<LocalToWorld>(player).Position, out var portal, out var position)) continue;
            _at[i] = portal.Scene;
            _portalNames[i] = portal.DisplayName;
            _portalPositions[i] = position;
        }
        for (int i = 0; i < _ready.Length; i++)
            if (_ready[i] != _at[i]) _ready[i] = default;
    }

    static bool TryFindPortal(EntityManager entityManager, float3 player, out Portal nearest, out float3 nearestPosition)
    {
        nearest = default;
        nearestPosition = default;
        using var portalQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Portal>(), ComponentType.ReadOnly<LocalToWorld>());
        using var portals = portalQuery.ToComponentDataArray<Portal>(Allocator.Temp);
        using var transforms = portalQuery.ToComponentDataArray<LocalToWorld>(Allocator.Temp);
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < portals.Length; i++)
        {
            float distance = math.distance(player.xz, transforms[i].Position.xz);
            if (distance > portals[i].Range || distance >= nearestDistance) continue;
            nearestDistance = distance;
            nearest = portals[i];
            nearestPosition = transforms[i].Position;
        }
        return nearestDistance < float.MaxValue;
    }

    // The players in the level: the ones who have to choose a portal
    static bool InLevel(int playerIndex)
    {
        var characters = CharacterManager.For(playerIndex);
        return GameInput.For(playerIndex) != null && characters != null && characters.Player != Entity.Null;
    }

    // "Player 1 ready · waiting for Player 2" in co-op, then the requirement warnings
    void ShowStatus()
    {
        string text = "";
        if (GameInput.PlayerCount > 1)
        {
            for (int i = 0; i < GameInput.PlayerCount; i++)
            {
                if (!InLevel(i)) continue;
                bool ready = !_ready[i].IsEmpty && _ready[i] == _scene;
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
                    float have = stats is StatBlock s ? (k == 0 ? s.Strength : k == 1 ? s.Agility : s.Intelligence) : 0f;
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

    // The player chooses the portal they stand at; once everyone in the level has chosen it, it opens
    void Choose(int playerIndex)
    {
        if (_entering || !AtPortal(playerIndex)) return;
        _ready[playerIndex] = _at[playerIndex];
        var scene = _ready[playerIndex];
        for (int i = 0; i < GameInput.PlayerCount; i++)
            if (InLevel(i) && _ready[i] != scene) return;
        _entering = GameSceneLoader.Load(scene.ToString());
        if (_entering) Hide();
    }
}
