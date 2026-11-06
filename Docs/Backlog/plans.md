# Backlog: plans stream

Bugs noticed while reading code for `Docs/ControllerSupportPlan.md` and `Docs/MultiplayerPlan.md`. All found by reading only: none were reproduced in the Editor, and none were fixed.

## Mirror Image copy can press the keys again on its last frame
- Where: `Assets/Scripts/Systems/Skills/MirrorImageSystems.cs:115-119,124-129`, `Assets/Scripts/Systems/InputsDataSystem.cs:41`
- What: when a copy runs out, it sets `Ending` and clears its inputs so Whirlwind and Tesla Coil clean up. On the next frame `InputsDataSystem` writes live input to every `InputsData`, copies included, and the `Ending` branch only queues `DestroyWithChildren` and does not clear them. So a held key is seen again on the copy's last frame, and a channel can restart just before the copy is destroyed.
- Reproduce: equip Mirror Image and Whirlwind or Tesla Coil, hold the skill while the copy expires, look for an orphaned field or effect.
- Severity: low to medium, unconfirmed. Clearing inputs in the `Ending` branch would fix it, and the planned `LocalPlayer` filter would too.

## Right stick feeds a screen position into aiming
- Where: `Assets/InputSystem_Actions.inputactions` (`Look`), `Assets/Scripts/Systems/InputsDataSystem.cs:60-61`
- What: `Look` binds `<Mouse>/position`, `<Pointer>/delta` and `<Gamepad>/rightStick`, and the result is used as a screen point. Conflict resolution probably keeps the mouse position because its value is larger, but a stick or delta value could win near the screen's bottom left corner, or with no mouse, aiming at that corner.
- Reproduce: connect a gamepad, move the right stick with the mouse near the bottom left, or without moving the mouse.
- Severity: low today, since gamepads can't cast anyway. Fixed by controller phase C0.

## Aim point picks an arbitrary enemy or floor
- Where: `Assets/Scripts/Systems/InputsDataSystem.cs:74-86`
- What: hits come in no particular order (the comment at line 102 says so). The loop takes the first enemy in the list, not the closest, and the last ground hit, not the closest. With enemies overlapping on screen the aim can snap to the one behind. On stacked floors (a bridge over ground) the aim point can land on the lower floor. `ClosestUnit` already does it properly for `Hovered`, so `Hovered` and the aim point can disagree.
- Reproduce: hover two overlapping enemies and compare the Unit Panel with where a skill goes.
- Severity: low.

## Hovered stays stale without a camera
- Where: `Assets/Scripts/Systems/InputsDataSystem.cs:54-59`
- What: the `continue` when the camera is null comes before `Hovered = Entity.Null`.
- Severity: very low. The Unit Panel checks `Exists`, and the camera is normally present.

## Skill bar shows 1-4 but the keys are LMB, RMB, Q, E
- Where: `Assets/UI/SkillBar/SkillBar.uxml:12,21,30,39`, `Assets/UI/SkillBar/SkillBarHUD.cs`
- What: the `key` labels keep their UXML text, and nothing in `SkillBarHUD` sets them. `SkillsPanelHUD.KeyLabel` (`SkillsPanelHUD.cs:264`) shows the real binding. No USS rule hides them.
- Reproduce: look at the skill bar in play.
- Severity: low, cosmetic. Not fixed here because this stream writes documents only. Controller phase C6 covers it.

## Legendary powers can stick on the second player
- Where: `Assets/Scripts/Monobehaviors/Managers/Inventory/InventoryManager.cs:268-273,293-300`
- What: `TryPushEquipmentStats` calls `SyncLegendaryPowers` for each player, but `_appliedPowers` is replaced after the first one. For later players the removal step sees the new list, so a power that was unequipped is never removed from them.
- Reproduce: spawn a test player through `CharacterManager`'s context menu (it gets `EquipmentStats` from `AddCombatComponents`), equip and unequip a legendary, inspect the second entity.
- Severity: low today, since only test players are affected. Goes away with per-player managers.

## Latent multiplayer bugs
These do nothing with one player, and are listed in `Docs/MultiplayerPlan.md`:
- Two real players would both be "Player 1" in the Damage Meter: `PlayerEntitySetup.cs:13`, `DamageMeterSystems.cs:87`.
- Soul Thief checks the one `PassiveSkillManager` for any owner: `SoulThiefSystems.cs:156`.
- Level chunks stream around the last player in the query: `LevelGenerationSystems.cs:106`.
