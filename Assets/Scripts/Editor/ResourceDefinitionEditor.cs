using Characters;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ResourceDefinition), true)]
public class ResourceDefinitionEditor : IconPreviewEditor
{
    protected override Sprite Icon => ((ResourceDefinition)target).Icon;
    protected override string PreviewTitle => ((ResourceDefinition)target).Name;
}
