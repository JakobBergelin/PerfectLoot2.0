using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Top of the screen while in a dungeon (DungeonManager): its name, the current step and its progress, or that it's
/// complete and the return portal is open; how many items were picked up. Leave dungeon asks first when something would
/// be lost: an unfinished run, or items still on the ground.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class DungeonHUD : MonoBehaviour
{
    const string HiddenClass = "dh-card--hidden";
    const string CompleteClass = "dh-card--complete";
    const string FailedClass = "dh-card--failed";
    const string ActionsHiddenClass = "dh-actions--hidden";

    PanelRenderer _panelRenderer;
    int _boundVersion = -1;
    VisualElement _card;
    Label _name, _stepCount, _objective, _note, _found, _confirmText;
    VisualElement _progressBar, _progressFill, _leaveRow, _confirmRow;

    bool _hovered;

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

    // The HUD is kept between scenes (GameBootstrap's rig)
    void OnSceneLoaded(Scene scene, LoadSceneMode mode) => SetConfirming(false);

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

        _card = root.Q<VisualElement>("card");
        _name = root.Q<Label>("dungeonName");
        _stepCount = root.Q<Label>("stepCount");
        _objective = root.Q<Label>("objective");
        _found = root.Q<Label>("found");
        _note = root.Q<Label>("note");
        _progressBar = root.Q<VisualElement>("progressBar");
        _progressFill = root.Q<VisualElement>("progressFill");
        _leaveRow = root.Q<VisualElement>("leaveRow");
        _confirmRow = root.Q<VisualElement>("confirmRow");
        _confirmText = root.Q<Label>("confirmText");
        var leave = root.Q<Button>("leaveButton");
        var confirmLeave = root.Q<Button>("confirmLeaveButton");
        var stay = root.Q<Button>("stayButton");
        _boundVersion = version;
        if (_card == null || _name == null || _stepCount == null || _objective == null || _note == null || _found == null || _progressBar == null || _progressFill == null ||
            _leaveRow == null || _confirmRow == null || _confirmText == null || leave == null || confirmLeave == null || stay == null)
        {
            Debug.LogError("DungeonHUD: DungeonHUD.uxml is missing elements. Is it the Panel Renderer's Source Asset?", this);
            _card = null;
            return;
        }

        leave.clicked += OnLeaveClicked;
        confirmLeave.clicked += Leave;
        stay.clicked += () => SetConfirming(false);
        // Clicking a button mustn't also use a skill
        _card.RegisterCallback<PointerEnterEvent>(_ => SetHovered(true));
        _card.RegisterCallback<PointerLeaveEvent>(_ => SetHovered(false));
        SetConfirming(false);
        _card.AddToClassList(HiddenClass);
    }

    void LateUpdate()
    {
        if (_card == null) return;
        var manager = DungeonManager.Instance;
        if (manager == null || manager.State == DungeonManager.RunState.None || manager.Dungeon == null)
        {
            _card.AddToClassList(HiddenClass);
            SetHovered(false);
            SetConfirming(false);
            return;
        }
        _card.RemoveFromClassList(HiddenClass);

        bool complete = manager.State == DungeonManager.RunState.Complete;
        bool failed = manager.State == DungeonManager.RunState.Failed;
        _card.EnableInClassList(CompleteClass, complete);
        _card.EnableInClassList(FailedClass, failed);
        _name.text = manager.Dungeon.DisplayName;

        var step = manager.CurrentStep;
        _stepCount.text = !complete && !failed && manager.StepCount > 1 ? $"Step {manager.StepIndex + 1} of {manager.StepCount}" : "";
        _stepCount.style.display = _stepCount.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;

        float progress = -1f;   // No bar
        string note = "";
        if (complete) _objective.text = "Dungeon complete · Return portal open";
        else if (failed) _objective.text = "Dungeon failed · Return portal open";
        else if (step == null) _objective.text = "";
        else
        {
            switch (step.Kind)
            {
                case DungeonStepKind.KillEnemies:
                    int target = manager.KillTarget(step);
                    int kills = Mathf.Min(manager.Kills, target);
                    _objective.text = $"Defeat enemies  {kills} / {target}";
                    progress = target > 0 ? (float)kills / target : 1f;
                    break;
                case DungeonStepKind.KillBoss:
                    string boss = step.Boss == null ? "the boss" : string.IsNullOrWhiteSpace(step.Boss.Name) ? step.Boss.name : step.Boss.Name;
                    _objective.text = $"Defeat {boss}";
                    break;
                case DungeonStepKind.ReachExit:
                    _objective.text = "Find the exit";
                    break;
                case DungeonStepKind.Survive:
                    int left = Mathf.CeilToInt(manager.SurviveTimeLeft);
                    _objective.text = $"Survive  {left / 60}:{left % 60:00}";
                    progress = 1f - manager.SurviveTimeLeft / Mathf.Max(1f, step.SurviveSeconds);
                    note = "Dying fails the dungeon";
                    break;
            }
        }
        _note.text = note;
        _note.style.display = note.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        _progressBar.style.display = progress >= 0f ? DisplayStyle.Flex : DisplayStyle.None;
        if (progress >= 0f) _progressFill.style.width = Length.Percent(Mathf.Clamp01(progress) * 100f);

        var groundLoot = GroundLootManager.Instance;
        int found = groundLoot != null ? groundLoot.PickedUp : 0;
        _found.text = found > 0 ? $"{found} {(found == 1 ? "item" : "items")} found · reviewed in town" : "";
        _found.style.display = found > 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // Leaves at once when nothing would be lost
    void OnLeaveClicked()
    {
        var manager = DungeonManager.Instance;
        if (manager == null) return;
        var groundLoot = GroundLootManager.Instance;
        bool itemsOnGround = groundLoot != null && groundLoot.Items.Count > 0;
        if (manager.State == DungeonManager.RunState.Running)
            SetConfirming(true, "Leave the dungeon? This run is lost, with any items left on the ground; the next one starts fresh.");
        else if (itemsOnGround)
            SetConfirming(true, "Leave the dungeon? Items left on the ground are lost.");
        else
            Leave();
    }

    void Leave()
    {
        SetConfirming(false);
        var manager = DungeonManager.Instance;
        if (manager != null) manager.Leave();
    }

    void SetConfirming(bool confirming, string question = null)
    {
        if (_leaveRow == null) return;
        _leaveRow.EnableInClassList(ActionsHiddenClass, confirming);
        _confirmRow.EnableInClassList(ActionsHiddenClass, !confirming);
        if (confirming) _confirmText.text = question;
    }

    void SetHovered(bool hovered)
    {
        if (_hovered == hovered) return;
        _hovered = hovered;
        MenuPanels.SetBlocking(this, hovered);
    }
}
