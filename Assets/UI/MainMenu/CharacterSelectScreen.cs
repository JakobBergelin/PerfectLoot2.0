using System;
using System.Collections.Generic;
using Characters;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The main menu's Character Select screen, run by MainMenuHUD: the account's characters on the left with Create new
/// character, and on the right the selected character (its skills, Adventure, Delete) or the form that creates one.
/// Opens on the character played last; an account without characters opens the form. While player 2 is joined,
/// Adventure becomes Ready (AccountManager.SelectCharacter; MainMenuHUD starts once both are), and the character player 2
/// is ready with is tagged and can't be picked.
/// </summary>
public class CharacterSelectScreen
{
    const string HiddenClass = "mm-screen--hidden";
    const string SelectedRowClass = "cs-character--selected";
    const string SelectedChoiceClass = "cs-choice--selected";

    readonly IReadOnlyList<CharacterClass> _classes;
    readonly IReadOnlyList<ResourceDefinition> _resources;
    readonly Action<AccountCharacter> _adventure;

    const int CharactersPerPage = 6;

    VisualElement _list;
    Pager _pager;
    Label _noCharacters, _noSelection;
    VisualElement _detailsView, _createView, _deleteConfirm;
    Label _detailName, _detailClass, _detailResource, _detailDescription, _detailClassInfo, _detailResourceInfo, _deleteQuestion;
    Button _deleteButton, _adventureButton, _deleteNoButton, _createCharacterButton;
    VisualElement _detailSkills, _detailPassives, _skillTooltip, _skillTooltipBody;
    Label _skillTooltipTitle;
    TextField _nameField;
    Label _nameError, _classDescription, _classInfo, _resourceDescription, _resourceInfo;
    VisualElement _startingSkills;
    Label _startingSkillsLabel;
    Button _createButton;

    readonly Dictionary<string, VisualElement> _rows = new Dictionary<string, VisualElement>();
    readonly Dictionary<string, Label> _rowTags = new Dictionary<string, Label>();
    readonly Dictionary<ClassType, Button> _classChoices = new Dictionary<ClassType, Button>();
    readonly Dictionary<ResourceType, Button> _resourceChoices = new Dictionary<ResourceType, Button>();

    string _selectedGuid;
    ClassType _class;
    ResourceType _resource;

    static AccountManager Account => AccountManager.Instance;

    static bool Coop => GameInput.PlayerCount > 1;
    // What the Adventure button and the row tags show (Update)
    AccountCharacter _shownReady, _shownPlayer2, _shownSelected;
    bool _shownCoop;

    bool Creating => !_createView.ClassListContains(HiddenClass);
    bool ConfirmingDelete => !_deleteConfirm.ClassListContains(HiddenClass);

    /// <param name="classes">For class names and descriptions; every ClassType gets a choice either way.</param>
    /// <param name="resources">For resource names and descriptions; every ResourceType gets a choice either way.</param>
    /// <param name="adventure">Called with the selected character when Adventure is clicked.</param>
    public CharacterSelectScreen(IReadOnlyList<CharacterClass> classes, IReadOnlyList<ResourceDefinition> resources,
                                 Action<AccountCharacter> adventure)
    {
        _classes = classes ?? Array.Empty<CharacterClass>();
        _resources = resources ?? Array.Empty<ResourceDefinition>();
        _adventure = adventure;
    }

    /// <summary>Finds the screen's elements under root and hooks them up. False when the UXML is missing some.</summary>
    public bool Bind(VisualElement root)
    {
        _list = root.Q<VisualElement>("characterList");
        _noCharacters = root.Q<Label>("noCharacters");
        _noSelection = root.Q<Label>("noSelection");
        _detailsView = root.Q<VisualElement>("detailsView");
        _createView = root.Q<VisualElement>("createView");
        _deleteConfirm = root.Q<VisualElement>("deleteConfirm");
        _detailName = root.Q<Label>("detailName");
        _detailClass = root.Q<Label>("detailClass");
        _detailResource = root.Q<Label>("detailResource");
        _detailDescription = root.Q<Label>("detailDescription");
        _detailClassInfo = root.Q<Label>("detailClassInfo");
        _detailResourceInfo = root.Q<Label>("detailResourceInfo");
        _deleteQuestion = root.Q<Label>("deleteQuestion");
        _deleteButton = root.Q<Button>("deleteButton");
        _nameField = root.Q<TextField>("characterNameField");
        _nameError = root.Q<Label>("nameError");
        _classDescription = root.Q<Label>("classDescription");
        _classInfo = root.Q<Label>("classInfo");
        _startingSkills = root.Q<VisualElement>("startingSkills");
        _startingSkillsLabel = root.Q<Label>("startingSkillsLabel");
        _resourceDescription = root.Q<Label>("resourceDescription");
        _resourceInfo = root.Q<Label>("resourceInfo");
        _createButton = root.Q<Button>("createButton");
        var classGrid = root.Q<VisualElement>("classGrid");
        var resourceGrid = root.Q<VisualElement>("resourceGrid");
        var createCharacter = root.Q<Button>("createCharacterButton");
        var adventure = root.Q<Button>("adventureButton");
        var deleteYes = root.Q<Button>("deleteYesButton");
        var deleteNo = root.Q<Button>("deleteNoButton");
        var cancelCreate = root.Q<Button>("cancelCreateButton");
        _detailSkills = root.Q<VisualElement>("detailSkills");
        _detailPassives = root.Q<VisualElement>("detailPassives");
        _skillTooltip = root.Q<VisualElement>("skillTooltip");
        _skillTooltipTitle = root.Q<Label>("skillTooltipTitle");
        _skillTooltipBody = root.Q<VisualElement>("skillTooltipBody");
        if (_detailSkills == null || _detailPassives == null || _skillTooltip == null || _skillTooltipTitle == null || _skillTooltipBody == null ||
            _startingSkills == null || _startingSkillsLabel == null)
            return false;
        if (_list == null || _noCharacters == null || _noSelection == null || _detailsView == null || _createView == null ||
            _deleteConfirm == null || _detailName == null || _detailClass == null || _detailResource == null ||
            _detailDescription == null || _detailClassInfo == null || _detailResourceInfo == null || _deleteQuestion == null ||
            _deleteButton == null || _nameField == null || _nameError == null || _classDescription == null || _classInfo == null ||
            _resourceDescription == null || _resourceInfo == null || _createButton == null ||
            classGrid == null || resourceGrid == null || createCharacter == null || adventure == null || deleteYes == null ||
            deleteNo == null || cancelCreate == null)
            return false;

        _pager = Pager.After(_list);
        _adventureButton = adventure;
        _deleteNoButton = deleteNo;
        _createCharacterButton = createCharacter;
        createCharacter.clicked += OpenCreate;
        adventure.clicked += Adventure;
        _deleteButton.clicked += () => ShowDeleteConfirm(true);
        deleteYes.clicked += DeleteSelected;
        deleteNo.clicked += () => ShowDeleteConfirm(false);
        _createButton.clicked += Create;
        cancelCreate.clicked += () => Cancel();

        // Same rules as account names: characters a name can't have are dropped as they're typed or pasted
        _nameField.maxLength = AccountManager.MaxNameLength;
        _nameField.RegisterValueChangedCallback(e =>
        {
            string clean = AccountManager.CleanName(e.newValue);
            if (clean != e.newValue) _nameField.SetValueWithoutNotify(clean);
            UpdateCreateButton();
        });
        _nameField.RegisterCallback<KeyDownEvent>(e =>
        {
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Create();
        }, TrickleDown.TrickleDown);

        _classChoices.Clear();
        classGrid.Clear();
        foreach (ClassType type in Enum.GetValues(typeof(ClassType)))
        {
            if (type == ClassType.None) continue;
            var choice = AddChoice(classGrid, ClassName(type), () => PickClass(type));
            // Moving over the classes previews their starting skills
            UiNav.Hover(choice, () => ShowStartingSkills(type), () => ShowStartingSkills(_class));
            _classChoices[type] = choice;
        }

        _resourceChoices.Clear();
        resourceGrid.Clear();
        foreach (ResourceType type in Enum.GetValues(typeof(ResourceType)))
            if (type != ResourceType.None) _resourceChoices[type] = AddChoice(resourceGrid, ResourceName(type), () => PickResource(type));

        return true;
    }

    /// <summary>Shows the account's characters with the last played one selected, or the create form when there are none.</summary>
    public void Open()
    {
        if (Account == null) Debug.LogError("CharacterSelectScreen: no AccountManager, so there are no characters.");
        RebuildList();
        var initial = Account != null ? Account.LastPlayedCharacter ?? FirstCharacter() : null;
        if (initial != null) Select(initial.Guid);
        else OpenCreate();
    }

    /// <summary>The next (1) or previous (-1) page of characters (LT/RT).</summary>
    public void TurnPage(int step) => _pager.Turn(step);

    /// <summary>
    /// Esc or B: closes the delete question or the create form (back to the selected character). False when there's
    /// nothing to close, so the menu goes back to the main screen.
    /// </summary>
    public bool Cancel()
    {
        if (Coop && Account != null && Account.SelectedCharacterOf(0) != null)
        {
            Account.ClearSelection(0);
            return true;
        }
        if (ConfirmingDelete)
        {
            ShowDeleteConfirm(false);
            return true;
        }
        if (Creating && FirstCharacter() != null)
        {
            var selected = Account.FindCharacter(_selectedGuid) ?? Account.LastPlayedCharacter ?? FirstCharacter();
            Select(selected.Guid);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Every frame while shown: Adventure says Ready (or Waiting for Player 2) while player 2 is joined, and the
    /// character player 2 is ready with is tagged and can't be played.
    /// </summary>
    public void Update()
    {
        var ready = Coop && Account != null ? Account.SelectedCharacterOf(0) : null;
        var player2 = Coop && Account != null ? Account.SelectedCharacterOf(1) : null;
        var selected = Account != null ? Account.FindCharacter(_selectedGuid) : null;
        if (Coop == _shownCoop && ready == _shownReady && player2 == _shownPlayer2 && selected == _shownSelected) return;
        _shownCoop = Coop;
        _shownReady = ready;
        _shownPlayer2 = player2;
        _shownSelected = selected;
        RefreshCoop();
    }

    void RefreshCoop()
    {
        var ready = _shownReady;
        var player2 = _shownPlayer2;
        _adventureButton.text = !_shownCoop ? "Adventure" : ready != null ? "Waiting for Player 2" : "Ready";
        _adventureButton.SetEnabled(_shownSelected == null || _shownSelected != player2);
        foreach (var pair in _rowTags)
        {
            string tag = player2 != null && pair.Key == player2.Guid ? "Player 2" : ready != null && pair.Key == ready.Guid ? "Ready" : "";
            pair.Value.text = tag;
            pair.Value.EnableInClassList(HiddenClass, tag.Length == 0);
        }
    }

    /// <summary>"Level 12 Mage · Mana": the line under a character's name.</summary>
    public string Describe(AccountCharacter character) =>
        $"Level {character.Level} {ClassName(character.Class)} · {ResourceName(character.Resource)}";

    // ---------- List ----------

    void RebuildList()
    {
        _list.Clear();
        _rows.Clear();
        _rowTags.Clear();
        var characters = Account != null ? Account.Characters : Array.Empty<AccountCharacter>();
        foreach (var character in characters)
        {
            var row = new VisualElement();
            row.AddToClassList("cs-character");
            var name = new Label(character.Name);
            name.AddToClassList("cs-character-name");
            var info = new Label(Describe(character));
            info.AddToClassList("cs-character-info");
            // Who else plays it in co-op (RefreshCoop)
            var tag = new Label { pickingMode = PickingMode.Ignore };
            tag.AddToClassList("cs-character-tag");
            tag.AddToClassList(HiddenClass);
            row.Add(name);
            row.Add(info);
            row.Add(tag);
            string guid = character.Guid;
            row.RegisterCallback<ClickEvent>(_ => Select(guid));
            UiNav.Clickable(row, () => Select(guid));
            _list.Add(row);
            _rows[guid] = row;
            _rowTags[guid] = tag;
        }
        RefreshCoop();
        _noCharacters.EnableInClassList(HiddenClass, characters.Count > 0);
        _pager.Show(_list.Children(), CharactersPerPage);
    }

    AccountCharacter FirstCharacter() => Account != null && Account.Characters.Count > 0 ? Account.Characters[0] : null;

    void Select(string guid)
    {
        var character = Account != null ? Account.FindCharacter(guid) : null;
        // Ready with another one: picking this one isn't ready anymore
        var ready = Account != null ? Account.SelectedCharacterOf(0) : null;
        if (Coop && ready != null && ready != character) Account.ClearSelection(0);
        _selectedGuid = character?.Guid;
        foreach (var pair in _rows)
            pair.Value.EnableInClassList(SelectedRowClass, pair.Key == _selectedGuid);
        if (character != null && _rows.TryGetValue(character.Guid, out var row)) _pager.ShowPageOf(row);

        if (character == null)
        {
            ShowView(details: false, create: false);
            UiNav.Focus(_createCharacterButton);
            return;
        }
        UiNav.Focus(_adventureButton);
        _detailName.text = character.Name;
        _detailClass.text = $"Level {character.Level} {ClassName(character.Class)}";
        _detailResource.text = ResourceName(character.Resource);
        SetDescription(_detailDescription, ClassAsset(character.Class)?.Description);
        SetDescription(_detailClassInfo, ClassInfo(ClassAsset(character.Class)));
        SetDescription(_detailResourceInfo, ResourceInfo(ResourceAsset(character.Resource), withDescription: true));
        ShowSkills(character);
        ShowView(details: true, create: false);
    }

    // The right column: the selected character, the create form, or (neither) the hint to pick one
    void ShowView(bool details, bool create)
    {
        _detailsView.EnableInClassList(HiddenClass, !details);
        _createView.EnableInClassList(HiddenClass, !create);
        _noSelection.EnableInClassList(HiddenClass, details || create);
        HideSkillTooltip();
        ShowDeleteConfirm(false);
    }

    // ---------- Selected character ----------

    // Its 4 active skills and 3 passives as icons (empty frames until it was first played), with a tooltip on hover or
    // controller focus. The skill assets come from the game rig's managers (GameBootstrap.RigPrefab).
    /// <summary>Every active skill (the game rig's skill list); empty without the rig.</summary>
    public static IReadOnlyList<ActiveSkills.ActiveSkill> AllActiveSkills()
    {
        var rig = GameBootstrap.RigPrefab;
        var manager = rig != null ? rig.GetComponentInChildren<ActiveSkillManager>(true) : null;
        return manager != null ? manager.AllSkills : (IReadOnlyList<ActiveSkills.ActiveSkill>)Array.Empty<ActiveSkills.ActiveSkill>();
    }

    /// <summary>The skill asset a character saved by type (the game rig's skill list), or null.</summary>
    public static ActiveSkills.ActiveSkill FindSkill(string type)
    {
        var rig = GameBootstrap.RigPrefab;
        var manager = rig != null && !string.IsNullOrEmpty(type) ? rig.GetComponentInChildren<ActiveSkillManager>(true) : null;
        return manager != null ? manager.AllSkills.Find(candidate => candidate != null && candidate.Type.ToString() == type) : null;
    }

    /// <summary>The passive asset a character saved by type (the game rig's passive list), or null.</summary>
    public static Passives.PassiveSkill FindPassive(string type)
    {
        var rig = GameBootstrap.RigPrefab;
        var manager = rig != null && !string.IsNullOrEmpty(type) ? rig.GetComponentInChildren<PassiveSkillManager>(true) : null;
        return manager != null ? manager.AllPassives.Find(candidate => candidate != null && candidate.Type.ToString() == type) : null;
    }

    void ShowSkills(AccountCharacter character)
    {
        HideSkillTooltip();
        _detailSkills.Clear();
        for (int i = 0; i < 4; i++)
        {
            AddActiveIcon(_detailSkills, FindSkill(i < character.ActiveSkills.Count ? character.ActiveSkills[i] : null));
        }

        _detailPassives.Clear();
        for (int i = 0; i < 3; i++)
        {
            var passive = FindPassive(i < character.Passives.Count ? character.Passives[i] : null);
            if (passive == null)
            {
                AddSkillIcon(_detailPassives, null, null, passive: true, null);
                continue;
            }
            string name = string.IsNullOrEmpty(passive.Name) ? passive.name : passive.Name;
            AddSkillIcon(_detailPassives, passive.Icon, name, passive: true, () =>
            {
                _skillTooltipTitle.text = name;
                _skillTooltipBody.Clear();
                if (!string.IsNullOrEmpty(passive.Description))
                    _skillTooltipBody.Add(new Label(passive.Description) { pickingMode = PickingMode.Ignore });
            });
        }
    }

    // An active skill's icon with its tooltip (requirements, numbers and description); an empty frame for none
    void AddActiveIcon(VisualElement row, ActiveSkills.ActiveSkill skill)
    {
        if (skill == null)
        {
            AddSkillIcon(row, null, null, passive: false, null);
            return;
        }
        string name = string.IsNullOrEmpty(skill.Name) ? skill.name : skill.Name;
        AddSkillIcon(row, skill.Icon, name, passive: false, () =>
        {
            _skillTooltipTitle.text = name;
            SkillTooltip.Fill(_skillTooltipBody, skill, null);
        });
    }

    // fill writes the tooltip's title and body; null for an empty slot (no tooltip). A skill without an icon shows its name.
    void AddSkillIcon(VisualElement row, Sprite icon, string name, bool passive, Action fill)
    {
        var element = new VisualElement();
        element.AddToClassList("cs-skill");
        if (passive) element.AddToClassList("cs-skill--passive");
        if (fill == null) element.AddToClassList("cs-skill--empty");
        if (icon != null) element.style.backgroundImage = new StyleBackground(icon);
        else if (!string.IsNullOrEmpty(name))
        {
            var label = new Label(name) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("cs-skill__name");
            element.Add(label);
        }
        if (fill != null)
            UiNav.Hover(element, () =>
            {
                fill();
                ShowSkillTooltip(element);
            }, HideSkillTooltip);
        row.Add(element);
    }

    // Right of the icon, in the tooltip's parent's coordinates
    void ShowSkillTooltip(VisualElement anchor)
    {
        var position = anchor.ChangeCoordinatesTo(_skillTooltip.parent, new Vector2(anchor.layout.width + 8f, 0f));
        _skillTooltip.style.left = position.x;
        _skillTooltip.style.top = position.y;
        _skillTooltip.AddToClassList("cs-tooltip--visible");
    }

    void HideSkillTooltip() => _skillTooltip?.RemoveFromClassList("cs-tooltip--visible");

    void Adventure()
    {
        var character = Account != null ? Account.FindCharacter(_selectedGuid) : null;
        if (character != null) _adventure?.Invoke(character);
    }

    void ShowDeleteConfirm(bool show)
    {
        var character = Account != null ? Account.FindCharacter(_selectedGuid) : null;
        show &= character != null;
        if (show) _deleteQuestion.text = $"Delete {character.Name}? This can't be undone.";
        bool was = ConfirmingDelete;
        _deleteConfirm.EnableInClassList(HiddenClass, !show);
        _deleteButton.EnableInClassList(HiddenClass, show);
        if (show) UiNav.Focus(_deleteNoButton);
        else if (was) UiNav.Focus(_deleteButton);
    }

    void DeleteSelected()
    {
        if (Account == null || !Account.DeleteCharacter(_selectedGuid)) return;
        RebuildList();
        var next = Account.LastPlayedCharacter ?? FirstCharacter();
        if (next != null) Select(next.Guid);
        else OpenCreate();
    }

    // ---------- Create ----------

    void OpenCreate()
    {
        foreach (var pair in _rows) pair.Value.RemoveFromClassList(SelectedRowClass);
        // A controller can't type: a free name to keep
        _nameField.SetValueWithoutNotify(GameInput.UsingGamepad ? FreeName() : "");
        PickClass(ClassType.None);
        PickResource(ResourceType.None);
        ShowView(details: false, create: true);
        UpdateCreateButton();
        if (GameInput.UsingGamepad)
        {
            foreach (var choice in _classChoices.Values)
            {
                UiNav.Focus(choice);
                break;
            }
            return;
        }
        // Ready to type (after layout, or the hidden field can't take focus yet)
        _nameField.schedule.Execute(() => _nameField.Focus());
    }

    // "Hero", or "Hero2" and on when it's taken (names have no spaces)
    static string FreeName()
    {
        string name = "Hero";
        for (int number = 2; Account != null && Account.IsCharacterNameTaken(name); number++) name = $"Hero{number}";
        return name;
    }

    void PickClass(ClassType type)
    {
        _class = type;
        foreach (var pair in _classChoices) pair.Value.EnableInClassList(SelectedChoiceClass, pair.Key == type);
        SetDescription(_classDescription, ClassAsset(type)?.Description);
        SetDescription(_classInfo, ClassInfo(ClassAsset(type)));
        ShowStartingSkills(type);
        UpdateCreateButton();
    }

    // The skills a new character of this class starts with (SkillRequirements.StartingSkills); hidden without a class.
    // Shown for the picked class, and for the one under the pointer or controller focus while choosing
    void ShowStartingSkills(ClassType type)
    {
        HideSkillTooltip();
        _startingSkills.Clear();
        var classAsset = ClassAsset(type);
        _startingSkills.EnableInClassList(HiddenClass, classAsset == null);
        _startingSkillsLabel.EnableInClassList(HiddenClass, classAsset == null);
        if (classAsset == null) return;
        foreach (var skill in ActiveSkills.SkillRequirements.StartingSkills(AllActiveSkills(), classAsset))
            AddActiveIcon(_startingSkills, skill);
    }

    void PickResource(ResourceType type)
    {
        _resource = type;
        foreach (var pair in _resourceChoices) pair.Value.EnableInClassList(SelectedChoiceClass, pair.Key == type);
        SetDescription(_resourceDescription, ResourceAsset(type)?.Description);
        SetDescription(_resourceInfo, ResourceInfo(ResourceAsset(type), withDescription: false));
        UpdateCreateButton();
    }

    void UpdateCreateButton()
    {
        string name = _nameField.value;
        bool taken = Account != null && AccountManager.IsValidName(name) && Account.IsCharacterNameTaken(name);
        _nameError.text = taken ? "A character with this name already exists" : "";
        _nameError.EnableInClassList(HiddenClass, !taken);
        _createButton.SetEnabled(Account != null && AccountManager.IsValidName(name) && !taken &&
                                 _class != ClassType.None && _resource != ResourceType.None);
    }

    void Create()
    {
        if (!Creating || Account == null) return;
        var classAsset = ClassAsset(_class);
        if (classAsset == null)
            Debug.LogWarning($"CharacterSelectScreen: MainMenuHUD's AllClasses has no {_class}, so the character gets no starting items.");
        var character = Account.CreateCharacter(_nameField.value, _class, _resource, classAsset != null ? classAsset.StartingItems : null);
        if (character == null)
        {
            UpdateCreateButton();
            return;
        }
        // The first skills its class reaches: equipped from the start
        if (classAsset != null)
            Account.SetSkills(character, ActiveSkills.SkillRequirements.StartingSkills(AllActiveSkills(), classAsset).ConvertAll(skill => skill.Type.ToString()), null);
        _nameField.Blur();
        RebuildList();
        Select(character.Guid);
    }

    // ---------- Names and descriptions ----------

    static Button AddChoice(VisualElement grid, string text, Action clicked)
    {
        var button = new Button(clicked) { text = text };
        button.AddToClassList("cs-choice");
        grid.Add(button);
        return button;
    }

    CharacterClass ClassAsset(ClassType type)
    {
        foreach (var asset in _classes)
            if (asset != null && asset.Type == type) return asset;
        return null;
    }

    ResourceDefinition ResourceAsset(ResourceType type)
    {
        foreach (var asset in _resources)
            if (asset != null && asset.Type == type) return asset;
        return null;
    }

    string ClassName(ClassType type)
    {
        var asset = ClassAsset(type);
        return asset != null && !string.IsNullOrWhiteSpace(asset.Name) ? asset.Name : ItemText.SplitWords(type.ToString());
    }

    string ResourceName(ResourceType type)
    {
        var asset = ResourceAsset(type);
        return asset != null && !string.IsNullOrWhiteSpace(asset.Name) ? asset.Name : ItemText.SplitWords(type.ToString());
    }

    // Attributes gained per level (and by the max level) and the class passive, as rich text; null without a class
    static string ClassInfo(CharacterClass characterClass)
    {
        if (characterClass == null) return null;
        int max = Experience.MaxLevel;
        var text = new System.Text.StringBuilder();
        text.Append("<b>Attributes per level</b>\n");
        text.Append($"Strength +{characterClass.StrengthPerLevel}   Agility +{characterClass.AgilityPerLevel}   Intelligence +{characterClass.IntelligencePerLevel}\n");
        text.Append($"<color=#A8ADB8>At level {max}: {characterClass.StrengthPerLevel * max} / {characterClass.AgilityPerLevel * max} / {characterClass.IntelligencePerLevel * max}</color>");

        var passive = characterClass.ClassPassive;
        if (passive != null)
        {
            string name = string.IsNullOrWhiteSpace(passive.Name) ? passive.name : passive.Name;
            text.Append($"\n\n<b>Class passive: <color=#E0A020>{name}</color></b>\n");
            text.Append($"<color=#A8ADB8>{passive.Description}</color>");
        }
        return text.ToString();
    }

    // The resource in its bar colour and how it works (ResourceDefinition.Explanation), as rich text; null without one.
    // The selected character's view has no description line of its own, so it can take the description too.
    static string ResourceInfo(ResourceDefinition resource, bool withDescription)
    {
        if (resource == null) return null;
        string name = string.IsNullOrWhiteSpace(resource.Name) ? resource.name : resource.Name;
        var text = new System.Text.StringBuilder();
        text.Append($"<b>Resource: <color={ResourceColors.Hex(resource.Type)}>{name}</color></b>\n");
        if (withDescription && !string.IsNullOrWhiteSpace(resource.Description)) text.Append($"<color=#A8ADB8>{resource.Description}</color>\n");
        text.Append(resource.Explanation);
        return text.ToString();
    }

    // Hidden when empty, so an asset without a description leaves no gap
    static void SetDescription(Label label, string description)
    {
        label.text = description ?? "";
        label.EnableInClassList(HiddenClass, string.IsNullOrWhiteSpace(description));
    }
}
