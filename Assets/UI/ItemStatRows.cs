using Equipment;
using UnityEngine.UIElements;

/// <summary>
/// An item's stats (ItemText.Lines) as rows in a tooltip body: the affix icon, then the line. Lines without an affix
/// keep the icon's space so the text lines up; wide lines (the power, notes) use the full width. The text takes its
/// font, size and color from the container's style.
/// </summary>
public static class ItemStatRows
{
    const float IconSize = 18f;
    const float IconGap = 4f;
    const float BlankLineHeight = 8f;

    public static void Fill(VisualElement container, Item item, Item compareTo = null)
    {
        container.Clear();
        foreach (var line in ItemText.Lines(item, compareTo))
        {
            if (line.Wide)
            {
                var wide = Text(line.Text);
                if (line.Text.Length == 0) wide.style.minHeight = BlankLineHeight;
                container.Add(wide);
                continue;
            }

            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.style.width = IconSize;
            icon.style.height = IconSize;
            icon.style.flexShrink = 0;
            icon.style.marginRight = IconGap;
            var sprite = line.Affix != null ? AffixIcons.Get(line.Affix.Value) : null;
            if (sprite != null) icon.style.backgroundImage = new StyleBackground(sprite);
            row.Add(icon);

            var label = Text(line.Text);
            label.style.flexShrink = 1;
            label.style.flexGrow = 1;
            row.Add(label);
            container.Add(row);
        }
    }

    static Label Text(string text)
    {
        var label = new Label(text) { pickingMode = PickingMode.Ignore, enableRichText = true };
        label.style.paddingLeft = label.style.paddingRight = label.style.paddingTop = label.style.paddingBottom = 0;
        label.style.marginLeft = label.style.marginRight = label.style.marginTop = label.style.marginBottom = 0;
        label.style.whiteSpace = WhiteSpace.Normal;
        return label;
    }
}
