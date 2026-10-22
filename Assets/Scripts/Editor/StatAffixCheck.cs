using System;
using System.Collections.Generic;
using System.Reflection;
using Equipment;
using UnityEditor;
using UnityEngine;

/// <summary>
/// After every recompile, checks that Affix and StatBlock are hooked up in both directions through StatAffix.Add:
/// every Affix changes exactly one StatBlock field, and every StatBlock field (except Excluded) has an Affix.
/// Errors go to the Console.
/// </summary>
[InitializeOnLoad]
static class StatAffixCheck
{
    // StatBlock fields that deliberately have no Affix
    static readonly string[] Excluded = { nameof(StatBlock.WeaponCooldown) };

    static StatAffixCheck()
    {
        var fields = typeof(StatBlock).GetFields(BindingFlags.Public | BindingFlags.Instance);
        var covered = new HashSet<string>();

        foreach (Affix affix in Enum.GetValues(typeof(Affix)))
        {
            var block = new StatBlock();
            StatAffix.Add(ref block, affix, 1f);

            var changed = new List<string>();
            foreach (var field in fields)
                if (Convert.ToSingle(field.GetValue(block)) != 0f) changed.Add(field.Name);

            if (changed.Count == 0)
                Debug.LogError($"StatAffixCheck: Affix.{affix} isn't mapped onto any StatBlock field in StatAffix.Add.");
            else if (changed.Count > 1)
                Debug.LogError($"StatAffixCheck: Affix.{affix} changes several StatBlock fields: {string.Join(", ", changed)}.");
            covered.UnionWith(changed);
        }

        foreach (var field in fields)
            if (!covered.Contains(field.Name) && Array.IndexOf(Excluded, field.Name) < 0)
                Debug.LogError($"StatAffixCheck: StatBlock.{field.Name} has no Affix (add one to Affix and StatAffix.Add, or to StatAffixCheck.Excluded).");
    }
}
