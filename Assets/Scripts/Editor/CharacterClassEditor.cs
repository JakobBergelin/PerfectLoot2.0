using Characters;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CharacterClass))]
public class CharacterClassEditor : IconPreviewEditor
{
    protected override Sprite Icon => ((CharacterClass)target).Icon;
    protected override string PreviewTitle => ((CharacterClass)target).Name;
}
