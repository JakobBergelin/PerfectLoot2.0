using Passives;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PassiveSkill), true)]
public class PassiveSkillEditor : IconPreviewEditor
{
    protected override Sprite Icon => ((PassiveSkill)target).Icon;
    protected override string PreviewTitle => ((PassiveSkill)target).Name;
}
