using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Top of the screen while in a dungeon (DungeonManager): its name, the time left (and what a death just cost), the
/// current step and its progress with what dying does in it, or that it's complete or failed and the return portal is
/// open; how many items were picked up. Leave dungeon (the host's only: a client leaves the game from its menu) asks first
/// when something would be lost: an unfinished run, or items still on the ground.
/// </summary>
[RequireComponent(typeof(PanelRenderer))]
public class DungeonHUD : MonoBehaviour
{
    const string HiddenClass = "dh-card--hidden";
    const string CompleteClass = "dh-card--complete";
    const string FailedClass = "dh-card--failed";
    const string ActionsHiddenClass = "dh-actions--hidden";
    const string TimerLowClass = "dh-timer--low";
    const float LowTime = 30f;          // Seconds left when the timer turns red
    const float PenaltyShown = 2.5f;    // Seconds a death's cost shows next to the timer

    PanelRenderer _panelRenderer;
    int _boundVersion = -1;
    VisualElement _card;
    Label _name, _stepCount, _objective, _note, _found, _confirmText, _timer, _penalty;
    VisualElement _progressBar, _progressFill, _leaveRow, _confirmRow, _timerRow;

    bool _hovered;
    int _shownDeaths;
    float _shownCost;       // Seconds the latest deaths cost, shown next to the timer
    float _penaltyHide;     // Time.time when the shown cost goes away

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
        _timerRow = root.Q<VisualElement>("timerRow");
        _timer = root.Q<Label>("timer");
        _penalty = root.Q<Label>("penalty");
        var leave = root.Q<Button>("leaveButton");
        var confirmLeave = root.Q<Button>("confirmLeaveButton");
        var stay = root.Q<Button>("stayButton");
        _boundVersion = version;
        if (_card == null || _name == null || _stepCount == null || _objective == null || _note == null || _found == null || _progressBar == null || _progressFill == null ||
            _leaveRow == null || _confirmRow == null || _confirmText == null || _timerRow == null || _timer == null || _penalty == null ||
            leave == null || confirmLeave == null || stay == null)
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
        // Leaving someone else's game is in the game menu
        _leaveRow.style.display = NetSession.IsHost ? DisplayStyle.Flex : DisplayStyle.None;
        UpdateTimer(manager);

        var step = manager.CurrentStep;
        _stepCount.text = !complete && !failed && manager.StepCount > 1 ? $"Step {manager.StepIndex + 1} of {manager.StepCount}" : "";
        _stepCount.style.display = _stepCount.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;

        float progress = -1f;   // No bar
        string note = "";
        if (complete) _objective.text = "Dungeon complete · Return portal open";
        else if (failed) _objective.text = manager.OutOfTime ? "Out of time · Return portal open" : "Dungeon failed · Return portal open";
        else if (step == null) _objective.text = "";
        else
        {
            float penalty = manager.NextDeathPenalty;
            switch (step.Kind)
            {
                case DungeonStepKind.KillEnemies:
                    int target = manager.KillTarget(step);
                    int kills = Mathf.Min(manager.Kills, target);
                    _objective.text = $"Defeat enemies  {kills} / {target}";
                    progress = target > 0 ? (float)kills / target : 1f;
                    if (penalty > 0f) note = $"Dying costs {penalty:0} s";
                    break;
                case DungeonStepKind.KillBoss:
                    string boss = step.Boss == null ? "the boss" : string.IsNullOrWhiteSpace(step.Boss.Name) ? step.Boss.name : step.Boss.Name;
                    _objective.text = $"Defeat {boss}";
                    if (penalty > 0f) note = $"Dying costs {penalty:0} s";
                    break;
                case DungeonStepKind.ReachExit:
                    _objective.text = "Find the exit";
                    note = "Dying sends you back to the start";
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

    // The time limit's clock while it runs (not in Survive steps), red near the end; a death's cost next to it for a while
    void UpdateTimer(DungeonManager manager)
    {
        // A new run starts the count over
        if (manager.Deaths < _shownDeaths) _shownDeaths = 0;
        if (manager.Deaths > _shownDeaths)
        {
            // What the deaths since last frame cost: DeathPenalty × each one's number, in Kill steps
            float cost = 0f;
            if (manager.CurrentStep is { Kind: DungeonStepKind.KillEnemies or DungeonStepKind.KillBoss } && manager.ClockRunning)
                for (int death = _shownDeaths + 1; death <= manager.Deaths; death++) cost += manager.DeathPenalty * death;
            _shownDeaths = manager.Deaths;
            if (cost > 0f)
            {
                // Added to a cost still shown (deaths close together)
                if (Time.time < _penaltyHide) cost += _shownCost;
                _shownCost = cost;
                _penalty.text = $"-{cost:0} s";
                _penaltyHide = Time.time + PenaltyShown;
            }
        }

        bool shown = manager.ClockRunning;
        _timerRow.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
        if (!shown) return;
        int left = Mathf.CeilToInt(manager.TimeLeft);
        _timer.text = $"{left / 60}:{left % 60:00}";
        _timer.EnableInClassList(TimerLowClass, manager.TimeLeft <= LowTime);
        _penalty.style.display = Time.time < _penaltyHide ? DisplayStyle.Flex : DisplayStyle.None;
    }

    /// <summary>True while in a dungeon of this machine's own game, so it can be left (also from the in-game menu).</summary>
    public static bool InDungeon => NetSession.IsHost && DungeonManager.Instance != null && DungeonManager.Instance.State != DungeonManager.RunState.None;

    /// <summary>What leaving now would lose, as a question; null when nothing would be, so it can be left at once.</summary>
    public static string LeaveQuestion()
    {
        var manager = DungeonManager.Instance;
        var groundLoot = GroundLootManager.Instance;
        if (manager != null && manager.State == DungeonManager.RunState.Running)
            return "Leave the dungeon? This run is lost, with any items left on the ground; the next one starts fresh.";
        return groundLoot != null && groundLoot.Items.Count > 0 ? "Leave the dungeon? Items left on the ground are lost." : null;
    }

    public static void LeaveDungeon()
    {
        var manager = DungeonManager.Instance;
        if (manager != null) manager.Leave();
    }

    // Leaves at once when nothing would be lost
    void OnLeaveClicked()
    {
        if (DungeonManager.Instance == null) return;
        string question = LeaveQuestion();
        if (question != null) SetConfirming(true, question);
        else Leave();
    }

    void Leave()
    {
        SetConfirming(false);
        LeaveDungeon();
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
