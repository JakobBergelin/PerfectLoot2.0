using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// While the player stands within a Portal's range, shows the portal's name and an Enter button pinned above it on
/// screen (the nearest portal when several are in range). Enter, or EnterKey, loads the portal's scene with the current
/// character (GameSceneLoader). Hidden while a menu panel is open.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class PortalHUD : MonoBehaviour
{
    const string HiddenClass = "portal-card--hidden";

    [Tooltip("Also enters the portal in range")]
    public Key EnterKey = Key.F;
    [Tooltip("Height above the portal's origin where the card's bottom edge sits, in meters")]
    public float CardHeight = 3f;

    PanelRenderer _panelRenderer;
    int _boundVersion = -1;
    VisualElement _card;
    Label _name;
    Button _enter;

    bool _hovered;
    bool _entering;
    FixedString64Bytes _scene;      // Of the portal the card shows; empty while hidden

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

        _enter.text = $"Enter ({EnterKey})";
        _enter.clicked += Enter;
        // Clicking the button mustn't also use a skill
        _card.RegisterCallback<PointerEnterEvent>(_ => SetHovered(true));
        _card.RegisterCallback<PointerLeaveEvent>(_ => SetHovered(false));
        Hide();
    }

    void LateUpdate()
    {
        if (_card == null || _card.panel == null) return;
        var camera = Camera.main;
        if (_entering || camera == null || MenuPanels.IsPanelOpen || !TryFindPortal(out var portal, out var position))
        {
            Hide();
            return;
        }

        Vector3 anchor = (Vector3)position + Vector3.up * CardHeight;
        if (camera.WorldToViewportPoint(anchor).z < 0f)
        {
            Hide();
            return;
        }

        _scene = portal.Scene;
        _name.text = portal.DisplayName.ToString();
        Vector2 panelPosition = RuntimePanelUtils.CameraTransformWorldToPanel(_card.panel, anchor, camera);
        _card.style.left = panelPosition.x;
        _card.style.top = panelPosition.y;
        _card.RemoveFromClassList(HiddenClass);

        var keyboard = Keyboard.current;
        if (keyboard != null && !MenuPanels.IsTyping && keyboard[EnterKey].wasPressedThisFrame) Enter();
    }

    // The nearest portal whose range the player stands in, measured along the ground
    static bool TryFindPortal(out Portal nearest, out float3 nearestPosition)
    {
        nearest = default;
        nearestPosition = default;
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return false;
        var entityManager = world.EntityManager;

        using var playerQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>(), ComponentType.ReadOnly<LocalToWorld>());
        if (playerQuery.IsEmpty) return false;
        using var players = playerQuery.ToComponentDataArray<LocalToWorld>(Allocator.Temp);
        float2 player = players[0].Position.xz;

        using var portalQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Portal>(), ComponentType.ReadOnly<LocalToWorld>());
        using var portals = portalQuery.ToComponentDataArray<Portal>(Allocator.Temp);
        using var transforms = portalQuery.ToComponentDataArray<LocalToWorld>(Allocator.Temp);
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < portals.Length; i++)
        {
            float distance = math.distance(player, transforms[i].Position.xz);
            if (distance > portals[i].Range || distance >= nearestDistance) continue;
            nearestDistance = distance;
            nearest = portals[i];
            nearestPosition = transforms[i].Position;
        }
        return nearestDistance < float.MaxValue;
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

    void Enter()
    {
        if (_entering || _scene.IsEmpty) return;
        _entering = GameSceneLoader.Load(_scene.ToString());
        if (_entering) Hide();
    }
}
