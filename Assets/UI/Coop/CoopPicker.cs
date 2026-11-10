using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Player 2's character picker, run by their own buttons (PlayerMenuInput) and by the mouse when it's theirs: the
/// account's characters, with the ones the players have tagged ("Player 1" can't be picked). Character Select shows it
/// detailed (the highlighted character's class, resource and skills); the Hub shows only the list, with Leave co-op
/// under it while player 2 plays. The host decides what picking (Picked), Leave co-op (Left) and back (Back) do, and
/// calls Update every frame while it's shown. Styles in Coop.uss (and MainMenu.uss for the skill icons).
/// </summary>
public sealed class CoopPicker
{
    const int PlayerIndex = 1;
    const int CharactersPerPage = 6;
    const string HiddenClass = "coop--hidden";
    const string HighlightedClass = "coop-row--highlighted";
    const string TakenClass = "coop-row--taken";

    public readonly VisualElement Element;
    public Action<AccountCharacter> Picked;
    public Action Left;
    public Action Back;

    struct Entry
    {
        public VisualElement Row;
        public Label Tag;
        public AccountCharacter Character;      // Null for Leave co-op
    }

    readonly PlayerMenuInput _input = new PlayerMenuInput(PlayerIndex);
    readonly Func<AccountCharacter, string> _describe;
    readonly VisualElement _list;
    readonly Pager _pager;
    readonly Label _status, _hint;
    readonly VisualElement _details, _skills, _passives;
    readonly Label _detailName, _detailInfo;
    readonly List<Entry> _entries = new List<Entry>();

    int _highlighted;
    int _openedFrame = -1;
    bool _showLeave;
    string _ownTag = "", _confirm = "", _back = "";
    AccountCharacter _tagged0, _tagged1;
    bool _tagsValid;

    static AccountManager Account => AccountManager.Instance;

    /// <param name="detailed">Show the highlighted character's class, resource and skills under the list.</param>
    /// <param name="describe">The line under a character's name, e.g. "Level 12 Mage · Mana".</param>
    public CoopPicker(bool detailed, Func<AccountCharacter, string> describe)
    {
        _describe = describe ?? (character => $"Level {character.Level} {ItemText.SplitWords(character.Class.ToString())}");
        Element = new VisualElement();
        Element.AddToClassList("coop-panel");
        Element.Add(NewLabel("Player 2", "coop-title"));

        _list = new VisualElement();
        _list.AddToClassList("coop-list");
        Element.Add(_list);
        _pager = Pager.After(_list);

        if (detailed)
        {
            _details = new VisualElement();
            _details.AddToClassList("coop-details");
            _detailName = NewLabel("", "coop-detail-name");
            _detailInfo = NewLabel("", "coop-detail-info");
            _skills = new VisualElement();
            _skills.AddToClassList("cs-skills");
            _passives = new VisualElement();
            _passives.AddToClassList("cs-skills");
            _details.Add(_detailName);
            _details.Add(_detailInfo);
            _details.Add(_skills);
            _details.Add(_passives);
            Element.Add(_details);
        }

        _status = NewLabel("", "coop-status");
        _hint = NewLabel("", "coop-hint");
        Element.Add(_status);
        Element.Add(_hint);
    }

    /// <summary>The character the highlight is on, or null (none, or Leave co-op).</summary>
    public AccountCharacter Highlighted => _highlighted >= 0 && _highlighted < _entries.Count ? _entries[_highlighted].Character : null;

    /// <summary>
    /// Shows the account's characters with this one highlighted (else the first one player 1 doesn't play). Its buttons
    /// work from the next frame, so the press that opened it doesn't pick too.
    /// </summary>
    public void Open(AccountCharacter highlight, bool showLeave)
    {
        _showLeave = showLeave;
        _openedFrame = Time.frameCount;
        Rebuild();
        int index = _entries.FindIndex(entry => entry.Character != null && entry.Character == highlight);
        if (index < 0) index = _entries.FindIndex(entry => entry.Character != null && entry.Character != Taken);
        Highlight(Mathf.Max(0, index));
    }

    /// <summary>The tag on player 2's own character, e.g. "Ready" or "Playing".</summary>
    public void SetOwnTag(string tag)
    {
        if (_ownTag == tag) return;
        _ownTag = tag;
        _tagsValid = false;
    }

    /// <summary>A line above the hint, e.g. "Waiting for Player 1"; empty hides it.</summary>
    public void SetStatus(string text)
    {
        if (_status.text == text) return;
        _status.text = text;
        _status.EnableInClassList(HiddenClass, string.IsNullOrEmpty(text));
    }

    /// <summary>What confirm and back do, for the hint ("A Ready · B Leave").</summary>
    public void SetActions(string confirm, string back)
    {
        _confirm = confirm;
        _back = back;
    }

    /// <summary>Every frame while shown: follows the account's characters and the players' picks, and reads player 2's buttons.</summary>
    public void Update()
    {
        if (Account == null) return;
        if (!MatchesAccount()) Rebuild();
        UpdateTags();
        string hint = $"{_input.ConfirmKey} {_confirm} · {_input.BackKey} {_back}";
        if (_hint.text != hint) _hint.text = hint;

        if (Time.frameCount == _openedFrame) return;
        int step = _input.ReadStep();
        if (step != 0) Highlight(Mathf.Clamp(_highlighted + step, 0, _entries.Count - 1));
        if (_input.ConfirmPressed) Confirm(_highlighted);
        else if (_input.BackPressed) Back?.Invoke();
    }

    // The character player 1 plays (or is ready with), which player 2 can't pick
    static AccountCharacter Taken => Account != null ? Account.SelectedCharacterOf(0) : null;

    // ---------- List ----------

    bool MatchesAccount()
    {
        var characters = Account.Characters;
        int count = characters.Count + (_showLeave ? 1 : 0);
        if (_entries.Count != count) return false;
        for (int i = 0; i < characters.Count; i++)
            if (_entries[i].Character != characters[i]) return false;
        return true;
    }

    void Rebuild()
    {
        var highlighted = Highlighted;
        _list.Clear();
        _entries.Clear();
        if (Account != null)
            foreach (var character in Account.Characters)
                AddRow(character.Name, _describe(character), character);
        if (_showLeave) AddRow("Leave co-op", "Player 2 stops playing", null);
        _pager.Show(_entries.ConvertAll(entry => entry.Row), CharactersPerPage);
        _tagsValid = false;

        int index = highlighted != null ? _entries.FindIndex(entry => entry.Character == highlighted) : -1;
        Highlight(index >= 0 ? index : Mathf.Clamp(_highlighted, 0, _entries.Count - 1));
    }

    void AddRow(string name, string info, AccountCharacter character)
    {
        var row = new VisualElement();
        row.AddToClassList("coop-row");
        if (character == null) row.AddToClassList("coop-row--leave");
        var text = new VisualElement { pickingMode = PickingMode.Ignore };
        text.AddToClassList("coop-row__text");
        text.Add(NewLabel(name, "coop-row__name"));
        text.Add(NewLabel(info, "coop-row__info"));
        var tag = NewLabel("", "coop-row__tag");
        tag.AddToClassList(HiddenClass);
        row.Add(text);
        row.Add(tag);

        int index = _entries.Count;
        row.RegisterCallback<ClickEvent>(_ =>
        {
            if (!_input.OwnsMouse) return;
            Highlight(index);
            Confirm(index);
        });
        _list.Add(row);
        _entries.Add(new Entry { Row = row, Tag = tag, Character = character });
    }

    void Highlight(int index)
    {
        _highlighted = index;
        for (int i = 0; i < _entries.Count; i++) _entries[i].Row.EnableInClassList(HighlightedClass, i == index);
        if (index >= 0 && index < _entries.Count) _pager.ShowPageOf(_entries[index].Row);
        ShowDetails(Highlighted);
    }

    void Confirm(int index)
    {
        if (index < 0 || index >= _entries.Count) return;
        var character = _entries[index].Character;
        if (character == null) Left?.Invoke();
        else if (character != Taken) Picked?.Invoke(character);
    }

    // "Player 1" on player 1's character, the own tag on player 2's
    void UpdateTags()
    {
        var player1 = Taken;
        var player2 = Account.SelectedCharacterOf(PlayerIndex);
        if (_tagsValid && player1 == _tagged0 && player2 == _tagged1) return;
        _tagsValid = true;
        _tagged0 = player1;
        _tagged1 = player2;
        foreach (var entry in _entries)
        {
            string tag = entry.Character == null ? "" : entry.Character == player1 ? "Player 1" : entry.Character == player2 ? _ownTag : "";
            entry.Tag.text = tag;
            entry.Tag.EnableInClassList(HiddenClass, string.IsNullOrEmpty(tag));
            entry.Row.EnableInClassList(TakenClass, entry.Character != null && entry.Character == player1);
        }
    }

    // ---------- Details ----------

    void ShowDetails(AccountCharacter character)
    {
        if (_details == null) return;
        _details.EnableInClassList(HiddenClass, character == null);
        if (character == null) return;
        _detailName.text = character.Name;
        _detailInfo.text = _describe(character);

        _skills.Clear();
        for (int i = 0; i < 4; i++)
        {
            var skill = CharacterSelectScreen.FindSkill(i < character.ActiveSkills.Count ? character.ActiveSkills[i] : null);
            AddIcon(_skills, skill != null ? skill.Icon : null, skill != null, passive: false);
        }
        _passives.Clear();
        for (int i = 0; i < 3; i++)
        {
            var passive = CharacterSelectScreen.FindPassive(i < character.Passives.Count ? character.Passives[i] : null);
            AddIcon(_passives, passive != null ? passive.Icon : null, passive != null, passive: true);
        }
    }

    static void AddIcon(VisualElement row, Sprite icon, bool known, bool passive)
    {
        var element = new VisualElement { pickingMode = PickingMode.Ignore };
        element.AddToClassList("cs-skill");
        if (passive) element.AddToClassList("cs-skill--passive");
        if (!known) element.AddToClassList("cs-skill--empty");
        if (icon != null) element.style.backgroundImage = new StyleBackground(icon);
        row.Add(element);
    }

    static Label NewLabel(string text, string className)
    {
        var label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.AddToClassList(className);
        return label;
    }
}
