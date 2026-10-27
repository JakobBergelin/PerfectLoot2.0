using System;
using System.Collections.Generic;
using Characters;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The main menu's Character Select screen, run by MainMenuHUD: the account's characters on the left with Create new
/// character, and on the right the selected character (Adventure, Delete) or the form that creates one. Opens on the
/// character played last; an account without characters opens the form.
/// </summary>
public class CharacterSelectScreen
{
    const string HiddenClass = "mm-screen--hidden";
    const string SelectedRowClass = "cs-character--selected";
    const string SelectedChoiceClass = "cs-choice--selected";

    readonly IReadOnlyList<CharacterClass> _classes;
    readonly IReadOnlyList<ResourceDefinition> _resources;
    readonly Action<AccountCharacter> _adventure;

    ScrollView _list;
    Label _noCharacters, _noSelection;
    VisualElement _detailsView, _createView, _deleteConfirm;
    Label _detailName, _detailClass, _detailResource, _detailDescription, _deleteQuestion;
    Button _deleteButton;
    TextField _nameField;
    Label _nameError, _classDescription, _resourceDescription;
    Button _createButton;

    readonly Dictionary<string, VisualElement> _rows = new Dictionary<string, VisualElement>();
    readonly Dictionary<ClassType, Button> _classChoices = new Dictionary<ClassType, Button>();
    readonly Dictionary<ResourceType, Button> _resourceChoices = new Dictionary<ResourceType, Button>();

    string _selectedGuid;
    ClassType _class;
    ResourceType _resource;

    static AccountManager Account => AccountManager.Instance;

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
        _list = root.Q<ScrollView>("characterList");
        _noCharacters = root.Q<Label>("noCharacters");
        _noSelection = root.Q<Label>("noSelection");
        _detailsView = root.Q<VisualElement>("detailsView");
        _createView = root.Q<VisualElement>("createView");
        _deleteConfirm = root.Q<VisualElement>("deleteConfirm");
        _detailName = root.Q<Label>("detailName");
        _detailClass = root.Q<Label>("detailClass");
        _detailResource = root.Q<Label>("detailResource");
        _detailDescription = root.Q<Label>("detailDescription");
        _deleteQuestion = root.Q<Label>("deleteQuestion");
        _deleteButton = root.Q<Button>("deleteButton");
        _nameField = root.Q<TextField>("characterNameField");
        _nameError = root.Q<Label>("nameError");
        _classDescription = root.Q<Label>("classDescription");
        _resourceDescription = root.Q<Label>("resourceDescription");
        _createButton = root.Q<Button>("createButton");
        var classGrid = root.Q<VisualElement>("classGrid");
        var resourceGrid = root.Q<VisualElement>("resourceGrid");
        var createCharacter = root.Q<Button>("createCharacterButton");
        var adventure = root.Q<Button>("adventureButton");
        var deleteYes = root.Q<Button>("deleteYesButton");
        var deleteNo = root.Q<Button>("deleteNoButton");
        var cancelCreate = root.Q<Button>("cancelCreateButton");
        if (_list == null || _noCharacters == null || _noSelection == null || _detailsView == null || _createView == null ||
            _deleteConfirm == null || _detailName == null || _detailClass == null || _detailResource == null ||
            _detailDescription == null || _deleteQuestion == null || _deleteButton == null || _nameField == null ||
            _nameError == null || _classDescription == null || _resourceDescription == null || _createButton == null ||
            classGrid == null || resourceGrid == null || createCharacter == null || adventure == null || deleteYes == null ||
            deleteNo == null || cancelCreate == null)
            return false;

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
            if (type != ClassType.None) _classChoices[type] = AddChoice(classGrid, ClassName(type), () => PickClass(type));

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

    /// <summary>
    /// Esc: closes the delete question or the create form (back to the selected character). False when there's nothing
    /// to close, so the menu goes back to the main screen.
    /// </summary>
    public bool Cancel()
    {
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

    // ---------- List ----------

    void RebuildList()
    {
        _list.Clear();
        _rows.Clear();
        var characters = Account != null ? Account.Characters : Array.Empty<AccountCharacter>();
        foreach (var character in characters)
        {
            var row = new VisualElement();
            row.AddToClassList("cs-character");
            var name = new Label(character.Name);
            name.AddToClassList("cs-character-name");
            var info = new Label($"Level {character.Level} {ClassName(character.Class)} · {ResourceName(character.Resource)}");
            info.AddToClassList("cs-character-info");
            row.Add(name);
            row.Add(info);
            string guid = character.Guid;
            row.RegisterCallback<ClickEvent>(_ => Select(guid));
            _list.Add(row);
            _rows[guid] = row;
        }
        _noCharacters.EnableInClassList(HiddenClass, characters.Count > 0);
    }

    AccountCharacter FirstCharacter() => Account != null && Account.Characters.Count > 0 ? Account.Characters[0] : null;

    void Select(string guid)
    {
        var character = Account != null ? Account.FindCharacter(guid) : null;
        _selectedGuid = character?.Guid;
        foreach (var pair in _rows)
            pair.Value.EnableInClassList(SelectedRowClass, pair.Key == _selectedGuid);
        if (character != null && _rows.TryGetValue(character.Guid, out var row))
            _list.schedule.Execute(() => _list.ScrollTo(row));     // After layout, or there's nothing to scroll to yet

        if (character == null)
        {
            ShowView(details: false, create: false);
            return;
        }
        _detailName.text = character.Name;
        _detailClass.text = $"Level {character.Level} {ClassName(character.Class)}";
        _detailResource.text = ResourceName(character.Resource);
        SetDescription(_detailDescription, ClassAsset(character.Class)?.Description);
        ShowView(details: true, create: false);
    }

    // The right column: the selected character, the create form, or (neither) the hint to pick one
    void ShowView(bool details, bool create)
    {
        _detailsView.EnableInClassList(HiddenClass, !details);
        _createView.EnableInClassList(HiddenClass, !create);
        _noSelection.EnableInClassList(HiddenClass, details || create);
        ShowDeleteConfirm(false);
    }

    // ---------- Selected character ----------

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
        _deleteConfirm.EnableInClassList(HiddenClass, !show);
        _deleteButton.EnableInClassList(HiddenClass, show);
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
        _nameField.SetValueWithoutNotify("");
        PickClass(ClassType.None);
        PickResource(ResourceType.None);
        ShowView(details: false, create: true);
        UpdateCreateButton();
        // Ready to type (after layout, or the hidden field can't take focus yet)
        _nameField.schedule.Execute(() => _nameField.Focus());
    }

    void PickClass(ClassType type)
    {
        _class = type;
        foreach (var pair in _classChoices) pair.Value.EnableInClassList(SelectedChoiceClass, pair.Key == type);
        SetDescription(_classDescription, ClassAsset(type)?.Description);
        UpdateCreateButton();
    }

    void PickResource(ResourceType type)
    {
        _resource = type;
        foreach (var pair in _resourceChoices) pair.Value.EnableInClassList(SelectedChoiceClass, pair.Key == type);
        SetDescription(_resourceDescription, ResourceAsset(type)?.Description);
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

    // Hidden when empty, so an asset without a description leaves no gap
    static void SetDescription(Label label, string description)
    {
        label.text = description ?? "";
        label.EnableInClassList(HiddenClass, string.IsNullOrWhiteSpace(description));
    }
}
