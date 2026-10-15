using ActiveSkills;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ActiveSkill), true)]
public class ActiveSkillEditor : IconPreviewEditor
{
    protected override Sprite Icon => ((ActiveSkill)target).Icon;
    protected override string PreviewTitle => ((ActiveSkill)target).Name;
}
