using System;
using System.Reflection;
using System.Text.RegularExpressions;

/// <summary>
/// Fills {Field} / {Field:format} placeholders in a description from the owner's fields. Dotted paths reach into
/// referenced objects, e.g. {Effect.Duration}; a referenced text such as {Effect.Description} has its own placeholders
/// filled from that object. Unknown placeholders are left as they are. Only meant for UI.
/// </summary>
public static class DescriptionFormatter
{
    static readonly Regex Placeholder = new Regex(@"\{([\w.]+)(?::([^}]+))?\}");
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const int MaxDepth = 2;

    public static string Format(object owner, string description) => Format(owner, description, 0);

    static string Format(object owner, string description, int depth) => Placeholder.Replace(description ?? "", match =>
    {
        object holder = null;
        object value = owner;
        foreach (var name in match.Groups[1].Value.Split('.'))
        {
            var field = value?.GetType().GetField(name, Fields);
            if (field == null) return match.Value;
            holder = value;
            value = field.GetValue(value);
        }

        // Text from another object (e.g. an effect's description) gets that object's placeholders filled
        if (value is string text)
            return depth < MaxDepth && holder != owner ? Format(holder, text, depth + 1) : text;

        string format = match.Groups[2].Success ? match.Groups[2].Value : "0.##";
        return value is IFormattable formattable ? formattable.ToString(format, null) : value?.ToString() ?? "";
    });
}
