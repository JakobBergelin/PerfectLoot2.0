using System.Collections.Generic;
using System.IO;
using Equipment;
using UnityEditor;
using UnityEngine;

/// <summary>Account save helpers: clearing the Dev account, opening the saves folder, and giving every item its save Id.</summary>
public static class AccountTools
{
    [MenuItem("Tools/Clear Dev Account")]
    static void ClearDevAccount()
    {
        string id = AccountManager.ToAccountId(AccountManager.DefaultAccountName);
        if (!EditorUtility.DisplayDialog("Clear Dev Account", $"Delete everything saved for the {AccountManager.DefaultAccountName} account?", "Delete", "Cancel"))
            return;
        Debug.Log(AccountSaves.DeleteAccount(id)
            ? $"Deleted the {AccountManager.DefaultAccountName} account ({AccountSaves.FolderOf(id)})."
            : $"Nothing saved for the {AccountManager.DefaultAccountName} account.");
    }

    // A running game would save it again right away
    [MenuItem("Tools/Clear Dev Account", true)]
    static bool CanClearDevAccount() => !EditorApplication.isPlaying;

    [MenuItem("Tools/Open Saves Folder")]
    static void OpenSavesFolder()
    {
        Directory.CreateDirectory(AccountSaves.RootFolder);
        EditorUtility.RevealInFinder(AccountSaves.RootFolder);
    }

    [MenuItem("Tools/Assign Item IDs")]
    static void AssignItemIds()
    {
        int assigned = 0;
        var seen = new Dictionary<string, RollableItem>();
        foreach (var guid in AssetDatabase.FindAssets($"t:{nameof(RollableItem)}"))
        {
            var item = AssetDatabase.LoadAssetAtPath<RollableItem>(AssetDatabase.GUIDToAssetPath(guid));
            if (item == null) continue;
            if (item.AssignId()) assigned++;
            if (!seen.TryAdd(item.Id, item))
                Debug.LogWarning($"{item.name} and {seen[item.Id].name} have the same Id.", item);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"Assign Item IDs: {seen.Count} items, {assigned} given a new Id.");
    }
}
