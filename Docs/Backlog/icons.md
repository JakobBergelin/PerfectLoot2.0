# Icons stream backlog

## Needs a decision

**Power icons don't show in the swap summary.** Severity: low.
`PowerChanges` in `Assets/UI/CharacterPanel/CharacterPanelHUD.cs` puts "+Ambush" into one wrapped rich text line with the stat changes, and that Label can't hold an inline image without a sprite asset. The power's icon shows in the item tooltip when you hover the option. To show it in the summary too, split the powers out of `ChangeSummary` into a small row of their own.

**Skill and passive icons import at 128 px.** Severity: low.
`Assets/Scripts/Editor/AffixIconImporter.cs:23` forces mipmaps off in `SkillIcons`, and a 256 px icon without mipmaps gets rough at 48 to 64 px. I set `maxTextureSize` to 128 in all 46 metas in that folder instead of changing the importer. Item and power icons keep 256 px with mipmaps. If you'd rather have one rule, add `ItemIcons` and `PowerIcons` to the importer's folders and use 128 px everywhere.

## Not checked

- No icon or text has been seen in the running game, because this stream had no Editor access. Unity did import the new PNGs as 256 x 256 sprites (see the metas in commit 10896a0).
- The power icon row in tooltips compiles but hasn't been seen. Its size is `PowerIconSize` in `Assets/UI/ItemStatRows.cs`.
- The new tooltip line for `Flavor` compiles but hasn't been seen. It also appears wherever `ItemText.Lines` or `ItemText.Body` is used, such as the drop popup and ground loot, so check it isn't too long there. To limit it, move the block in `Assets/UI/ItemText.cs` behind a parameter.

## Ideas

- Bone Ward and Festering Horde use the Cold and Poison Resistance affix icons (`Assets/Resources/AffixIcons`). They could have their own.
- `Assets/Data/Images/ExampleAmulet.jpg` is no longer used by any item and can be deleted.
- Skill descriptions have no numbers because the tooltip lists them. Passive descriptions repeat their asset's numbers as plain text, as they did before, because `PassiveSkill.Description` isn't run through `DescriptionFormatter`. Running it through would let them use `{Field}` placeholders and never go stale.
