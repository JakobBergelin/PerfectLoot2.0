using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

/// <summary>
/// Pages instead of scrolling: shows the items of the current page and hides the rest, with "‹ 2 / 3 ›" under the
/// list (hidden when everything fits on one page). Pages fill up in order to Capacity, each item counting its
/// weight (1 by default; e.g. a section counts its rows). The arrows turn pages with the mouse; menus turn them with
/// LT/RT (MenuPanels.PagePressed) through Turn. Styles in Resources/UiNav.uss.
/// </summary>
public sealed class Pager
{
    public readonly VisualElement Element;
    public int Page { get; private set; }
    public int PageCount { get; private set; } = 1;

    readonly Label _previous, _next, _label;
    readonly List<VisualElement> _items = new List<VisualElement>();
    readonly List<int> _pageOf = new List<int>();
    int _capacity = 1;
    Func<VisualElement, int> _weight;

    /// <summary>Makes the page controls; add Element under the list (InsertAfter).</summary>
    public Pager()
    {
        Element = new VisualElement();
        Element.AddToClassList("pager");
        _previous = new Label("‹");
        _previous.AddToClassList("pager__arrow");
        _previous.RegisterCallback<ClickEvent>(_ => Turn(-1));
        _label = new Label { pickingMode = PickingMode.Ignore };
        _label.AddToClassList("pager__label");
        _next = new Label("›");
        _next.AddToClassList("pager__arrow");
        _next.RegisterCallback<ClickEvent>(_ => Turn(1));
        Element.Add(_previous);
        Element.Add(_label);
        Element.Add(_next);
    }

    /// <summary>Adds the page controls right after the list, in its parent.</summary>
    public static Pager After(VisualElement list)
    {
        var pager = new Pager();
        UiNav.AddStyles(list.parent);
        list.parent.Insert(list.parent.IndexOf(list) + 1, pager.Element);
        return pager;
    }

    /// <summary>Back to the first page (the next Show uses it).</summary>
    public void Reset() => Page = 0;

    /// <summary>Splits the items into pages and shows the current one (kept, unless there are fewer pages now).</summary>
    public void Show(IEnumerable<VisualElement> items, int capacity, Func<VisualElement, int> weight = null)
    {
        _items.Clear();
        _items.AddRange(items);
        _capacity = Math.Max(1, capacity);
        _weight = weight;
        Apply();
    }

    /// <summary>Shows the page the item is on.</summary>
    public void ShowPageOf(VisualElement item)
    {
        int index = _items.IndexOf(item);
        if (index < 0 || _pageOf[index] == Page) return;
        Page = _pageOf[index];
        Apply();
    }

    /// <summary>The next (1) or previous (-1) page; with a controller, focus moves to its first item. False at an end.</summary>
    public bool Turn(int step)
    {
        int page = Math.Clamp(Page + step, 0, PageCount - 1);
        if (page == Page) return false;
        Page = page;
        Apply();
        if (GameInput.UsingGamepad) UiNav.FocusFirstOf(ItemsOnPage());
        return true;
    }

    IEnumerable<VisualElement> ItemsOnPage()
    {
        for (int i = 0; i < _items.Count; i++)
            if (_pageOf[i] == Page) yield return _items[i];
    }

    void Apply()
    {
        // Greedy: an item goes on the current page while it still fits (an item bigger than a page gets one of its own)
        _pageOf.Clear();
        int page = 0, used = 0;
        foreach (var item in _items)
        {
            int weight = Math.Max(1, _weight != null ? _weight(item) : 1);
            if (used > 0 && used + weight > _capacity)
            {
                page++;
                used = 0;
            }
            _pageOf.Add(page);
            used += weight;
        }
        PageCount = page + 1;
        Page = Math.Clamp(Page, 0, PageCount - 1);

        for (int i = 0; i < _items.Count; i++)
            _items[i].style.display = _pageOf[i] == Page ? DisplayStyle.Flex : DisplayStyle.None;
        _label.text = $"{Page + 1} / {PageCount}";
        _previous.EnableInClassList("pager__arrow--disabled", Page == 0);
        _next.EnableInClassList("pager__arrow--disabled", Page >= PageCount - 1);
        Element.style.display = PageCount > 1 ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
