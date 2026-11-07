# Controller Support Plan

How gamepads should work in PerfectLoot, in phases that can each ship. Written 2026-11-06 on `flair/evening-polish`. No code has been changed for this plan.

Marks: **[V]** verified by reading the code at this commit. **[D]** read in the Input System 1.20 package docs. **[A]** assumed or from memory, check before relying on it. Nothing here was tested with a real controller.

Numbers such as angles and distances are starting guesses to tune by feel.

## Where things stand

- Only `Move` works on a gamepad. `Skill1-4`, `Inventory`, `Skills` and `ItemDatabase` have keyboard and mouse bindings only (`Assets/InputSystem_Actions.inputactions`). [V]
- `Attack` (West) and `Interact` (North) have gamepad bindings, but no code reads those actions. [V]
- `Look` is one action holding `<Mouse>/position`, `<Pointer>/delta` and `<Gamepad>/rightStick`, and `InputsDataSystem.cs:60-61` treats whatever it reads as a screen position. A stick value of (0.7, 0.2) is not a screen position. See the backlog. [V]
- `Move` is read as a vector, but `CharacterMovementSystems.cs:42-44` keeps only its direction, so any tilt past the deadzone is full speed. [V]
- Five panels poll `Keyboard.current` for Esc, `DamageMeterHUD.cs:127` polls M, `PortalHUD.cs:109` polls F, and `CharacterPanelHUD.cs:219,425` read `Mouse.current`. [V]
- UI: 22 `clicked +=` handlers, 12 `RegisterCallback<ClickEvent>`, 11 `PointerEnterEvent` hovers. A gamepad submit reaches `clicked` but not `ClickEvent`. [V count, D behaviour]
- There is no `EventSystem` in any scene or prefab, and `InputSystem_Actions` is the project-wide asset. UI Toolkit then reads its `UI` map directly, so gamepad `Navigate` may already reach focusable elements. [V setup, D behaviour, not tested]
- No rumble, and no use of `Time.timeScale` in game code, so menus never pause. [V] Camera shake exists since the juice work (crits from skills with Crit Shake, `CritShakeSystem`), so a controller rumble could follow the same trauma value.

## The main idea: skills keep reading one world point

Skills already take everything from `InputsData.MousePosition` and `DesiredDirection`. Hovering an enemy already snaps that point to the enemy and sets `InputsData.Hovered` (`InputsDataSystem.cs:72-79`). That is what a soft lock needs.

So all gamepad aiming lives in one place, which produces the same two outputs: a world point and a hovered unit. **No skill system changes.** Later, rename `MousePosition` to `AimPoint` (a mechanical change in about 8 files).

## Aiming

Each frame for a gamepad player, in `InputsDataSystem` or a helper it calls:

1. **Direction.** Right stick past its deadzone gives the stick direction. Otherwise the left stick's direction. Otherwise the last `DesiredDirection`. Use the same world axes as `Move`, so the two sticks agree.
2. **Distance.** With the right stick, `lerp(AimMin, AimMax, tilt)`, about 3 to 14 m, so a part tilt places things close. Otherwise `AimDefault`, far enough that range-clamped skills use their full range.
3. **Soft lock.** Pick the best enemy in a cone around the direction (about 35 degrees each side, within `AimMax`), scored by angle first and distance second. Keep the current target until another scores about 30% better, so it doesn't flicker in a swarm. Candidates can come from `UnitSpatialGrid`, which already holds every unit with `Steering` [V], filtered to `Enemy`. Skip targets behind walls with a ray.
4. **Output.** With a lock: `MousePosition` = the enemy's position and `Hovered` = the enemy. Without: player position plus direction times distance, with Y from a floor ray (`UnitMovement.TryFindFloor`) plus the same 0.5 as the mouse path, falling back to the player's height.

`Hovered` means the Unit Panel and `HoveredEffect` work with no change. `AimMin` above zero also avoids `TryFindLanding` failing when the cursor is on the caster.

A small **reticle** on the ground shows the aim point on gamepad, with a ring on the locked enemy. It reads ECS only. The existing telegraph material may be reusable. [A]

How cursor skills then behave: [V code, A feel]

| Skill | Reads | On gamepad |
|---|---|---|
| Teleport (`TeleportSystems.cs:64`) | point, clamped to Range | Full range along the aim, or shorter with a part tilt. Lands next to a locked enemy, as with mouse hover. |
| Fire Hydra (`FireHydraSystems.cs:50`) | point, clamped to Range | Same. With no stick input it lands at full range ahead, which may feel too far (question 3). |
| Ball Lightning (`BallLightningSystems.cs:120`) | point, no range limit | Flight length is the aim distance, so `AimDefault` sets it. Mouse reach is limited only by the screen. |
| Hookshot Slash (`HookshotSlashSystems.cs:62`) | point, distance clamped | Works. |
| Captain's Shield (`CaptainsShieldSystems.cs:72`) | point | Works, best with lock. |
| Shadowstep (`ShadowstepSystems.cs:116`) | enemy near the point | Needs lock or an aim point near an enemy. |
| Projectiles and channels (Fireball, Frozen Orb, Flamethrower...) | `DesiredDirection` | Work with any direction. |
| Sonic Ball (`SonicBallSystems.cs:70`) | `Move.x` | Works, and analog steering is a small bonus. |
| Mirror Image (`MirrorImageSystems.cs:131`) | copies the owner's `InputsData` | Works with no change. |

Cast timing stays as it is: skills fire while held. A hold-to-aim, release-to-cast mode would touch `SkillActivationSystem` and every skill, so leave it out unless playtests ask for it.

## Skill bindings

The right thumb stays on the aim stick, so skills go on triggers and bumpers:

| Control | Action | Keyboard today |
|---|---|---|
| Left stick / Right stick | Move / Aim | WASD / mouse |
| RT / LT | Skill1 / Skill2 | LMB / RMB |
| RB / LB | Skill3 / Skill4 | Q / E |
| South | Interact (loot, portal), UI Submit | F for portals, click for loot |
| East | UI Cancel | Esc |
| Start | Character panel | I |
| Select | Skills panel | K |
| D-pad Up / Right / Down | Meter toggle / next tab / by unit or by skill | M, clicks |

West and North stay free. Note `SkillActivationSystems.cs:35-39`: the lowest held slot wins, so RT beats everything held with it, as LMB does now. The Item Database stays keyboard-only unless you want it on the pad (question 5).

## Interact and hover replacement

Loot is click-only and the portal is key-only today, so gamepads need one Interact rule: nearest loot in `PickupRange` first, then a portal in range. Highlight the chosen label so the player sees what South will do.

| Mouse today | Gamepad |
|---|---|
| Enemy hover, Unit Panel | Soft lock target |
| Ground loot label click (`GroundLootHUD.cs:157`) | Interact on the highlighted label; repeated press takes the next |
| Portal card (`PortalHUD.cs:78,109`) | Interact, with a prompt on the card |
| Slot and skill tooltips | Show on selection in the panel |
| Swap preview (`CharacterPanelHUD.cs:526`, `HoverOption`) | Same method, called on selection |
| Skill bar tooltip | Skills panel only; skip in combat |
| Effect bar tooltip (`EffectBarHUD.cs:126`) | Phase 7, or list effects in the character panel (question 6) |
| Damage Meter hover blocks skills (`DamageMeterHUD.cs:91`) | Not needed: D-pad drives it, and skills stay live |

## UI navigation

There are two ways to do this:

- **UI Toolkit focus.** Make elements `focusable`, call `Focus()`, react to `NavigationMoveEvent`. Least code for one player. But a panel has one focused element, and all HUDs share `Assets/UI/PanelSettings.asset`. The Input System's multiplayer UI pages describe the Multiplayer Event System and never mention UI Toolkit. [V, D] Two players in one panel would probably not work, though nobody has tried it here.
- **Our own small navigator (recommended).** One `PanelNavigator` per local player. It reads that player's `UI` actions and holds its own selected element. It moves to the nearest element in the pressed direction, sets a `nav-selected` USS class, and calls `ScrollTo` inside scroll views. Submit calls a registered activate action. About 150 lines, and it does not care how many players share the panel.

A stick-driven virtual cursor is not an option: the package's Virtual Mouse component works with uGUI only. [D]

The navigator costs a little more now, and avoids redoing all panels for split-screen. Panels register their elements with `navigator.Add(element, onSelect, onDeselect, onActivate)`. Tooltip show and hide methods already exist, so they plug straight in.

While a gamepad player has a panel open, zero that player's `Move`, or the left stick walks and navigates at once. Today only typing blocks `Move` (`InputsDataSystem.cs:44`). Keyboard players keep moving as they do now.

The picker's click-outside close (`CharacterPanelHUD.cs:219`) gets a Cancel equivalent.

Text entry is the awkward case: `MainMenuHUD` and `CharacterSelectScreen` need typed names. A gamepad-only player needs a default name or an on-screen keyboard (question 4).

## Button prompts

- New `InputGlyphs`: action plus device family (Xbox, PlayStation, Switch, keyboard) gives a sprite or a text fallback. Family comes from the device layout. `GetBindingDisplayString` is already used at `SkillsPanelHUD.cs:266` for text, with the group fixed to keyboard.
- Places: skill bar key labels, `SkillsPanelHUD.KeyLabel`, portal card, highlighted loot label, Drop Popup, dungeon Leave, a footer in open panels.
- No glyph art exists. Draw them to fit the art style, or use a CC0 set such as Kenney's input prompts (licence to be checked).

## Rumble

Cosmetic and per device: `Gamepad.SetMotorSpeeds` on the player's own pad, never `Gamepad.current`. A managed `RumbleSystem` reads ECS and keeps a short list of pulses. The strongest wins, so a swarm never makes the pad buzz nonstop.

| Event | Feel |
|---|---|
| Took a big hit (over about 10% of max health) | Short, strong |
| Own crit | Very short, light |
| Dash, Teleport, Ball Lightning | Short light tick |
| Boss spawn, death | Long low roll |
| Legendary or Unique picked up | Double tick |

Stop with `ResetHaptics` on app focus loss, scene load, and quit. Add an intensity setting with an off position. How to detect each event still has to be checked against the damage buffers; `CombatTextHUD` already consumes those events. [A]

## Device switching

New class `LocalPlayerInput`: owns one `InputSystem_Actions`, its `devices` list and an `ActiveScheme` (`KeyboardMouse` or `Gamepad`). The generated class exposes `devices`. [V]

- One player: `devices` stays null and every device works. The scheme follows the last real input: stick tilt over 0.5 or any gamepad button switches to Gamepad; mouse moved a few pixels, a click, or a key switches back. Stick drift must not flip it.
- On Gamepad: hide the cursor, show reticle and gamepad prompts, and clear hovers. On KeyboardMouse: the reverse.
- Disconnect: keep the scheme, show a "reconnect" note. With split-screen the slot waits for its pad.

This is the same class the split-screen plan needs, so build it once. See `Docs/MultiplayerPlan.md`, phase M1. The three HUDs that make their own `new InputSystem_Actions()` move onto it too.

## Phases

Each phase leaves the game working for keyboard and mouse. Sizes are rough: S is a day or less, M a few days, L a week or more.

**C0. Actions and device tracking (S).** Add bindings. Split `Look` into `Point` (mouse position) and `Aim` (right stick, stick deadzone). Regenerating the `.cs` needs the Editor. Add `LocalPlayerInput`. Replace the hard-coded keys with actions.
Files: `Assets/InputSystem_Actions.inputactions`, `InputSystem_Actions.cs`, new `Assets/Scripts/Input/LocalPlayerInput.cs`, `InputsDataSystem.cs`, `CharacterPanelHUD.cs`, `SkillsPanelHUD.cs`, `ItemDatabasePanelHUD.cs`, `DropPopupHUD.cs`, `MainMenuHUD.cs`, `DamageMeterHUD.cs`, `PortalHUD.cs`.

**C1. Stick aim (M).** Direction and distance rules, floor ray, reticle. Ships: combat playable on a pad, menus still need the mouse.
Files: `InputsDataSystem.cs`, new `Assets/Scripts/Input/GamepadAim.cs`, new reticle script and asset, `PlayerComponents.cs` if aim state needs a home.

**C2. Soft lock (M).** Cone search, stickiness, line of sight, ring on target.
Files: `GamepadAim.cs`, reticle script.

**C3. Interact (S to M).** Interact rule, loot highlight, portal, Drop Popup and Leave on pad.
Files: `GroundLootHUD.cs`, `GroundLootManager.cs`, `PortalHUD.cs`, `DropPopupHUD.cs`, `DungeonHUD.cs`.

**C4. Panel navigation (L).** `PanelNavigator`, Character panel first, then Skills panel, then the picker. `ClickEvent` handlers wrapped so mouse and pad share them. `Move` blocked for pad players with a panel open.
Files: new `Assets/UI/PanelNavigator.cs`, `MenuPanels.cs`, `CharacterPanelHUD.cs`, `SkillsPanelHUD.cs`, panel USS files, `InputsDataSystem.cs`.

**C5. Menus and Damage Meter (M).** Main menu, character select, name entry, meter on the D-pad.
Files: `MainMenuHUD.cs`, `CharacterSelectScreen.cs`, `DamageMeterHUD.cs`.

**C6. Prompts (M).** `InputGlyphs`, art, all prompt sites. Files: new `Assets/UI/InputGlyphs.cs`, `SkillBarHUD.cs`, `SkillsPanelHUD.cs`, `PortalHUD.cs`, `GroundLootHUD.cs`, `DropPopupHUD.cs`.

**C7. Rumble and options (M).** `RumbleSystem`, intensity, aim tuning, optional rebinding and effect bar access.

Do C0 to C3 first: that gets the pad into combat and looting, and shows how the aim feels before the large UI work.

## Risks

- **Soft lock feel.** Too sticky and you can't pick targets; too weak and it's useless in a swarm. Needs playtesting with a real pad, and it is the piece most likely to need rework.
- **Editor needed.** Regenerating the input class and checking panels both need the Editor, and a real controller.
- **UI Toolkit details.** Whether the default UI input path fights our navigator is unknown. If `Navigate` moves UI Toolkit focus at the same time, disable the `UI` map's gamepad bindings for it. [A]

## Questions for you

1. Should stick tilt control speed? Today any tilt is full speed. Changing it is a gameplay change, so it stays out of this plan unless you want it.
2. Is the trigger and bumper layout right, or do you prefer the Diablo console style with skills on face buttons and auto-aim?
3. With no stick input, should placed skills (Fire Hydra) use full range, a shorter default, or the locked enemy only?
4. Gamepad name entry: on-screen keyboard, generated names, or keyboard required for first setup?
5. Does the Item Database need gamepad support, or is it a keyboard tool?
6. Effect tooltips on pad: worth a mode on the D-pad, or a list in the character panel?
7. Which platforms? Console targets add certification rules for prompts, disconnects and user pairing.
