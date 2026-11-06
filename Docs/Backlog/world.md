# World stream backlog

## How the new levels connect
- `GameBootstrap.Town` (`Assets/Resources/GameBootstrap.asset`) is now `Hub`: the main menu starts there, and a dungeon entered without a previous scene returns there. It was a data setting, so no code changed. To go back, set Town to `SampleScene`.
- The Hub has portals to Haunted Cathedral, Test Arena, Test Swarm and Old Town (SampleScene). Both test levels have a portal back. SampleScene has no portal to the Hub, because SampleScene was left alone.
- `Tools/Create Hub And Test Levels` rebuilds the three scenes' content (`Assets/Scripts/Editor/Level/HubLevelsCreator.cs`). Hand edits to those scenes are lost if it runs again.

## Not seen working
- The effects from commit 423a12e are still unseen. Skills did not fire from queued key events (`InputSystem.QueueStateEvent` on the keyboard, with the Editor brought to the front), so nothing was cast in the Test Arena and the Damage Meter was not tried. No sizes in `VfxBuilder.Flair.cs` were changed. Try it by hand: Hub, Test Arena portal, cast at the dummies.
- The portal trip ran by calling `PortalHUD.Enter` through reflection, after moving the player into range. The Enter (F) button did appear, but it wasn't pressed. Hub to Test Arena and Hub to Test Swarm both loaded. The way back to the Hub wasn't tried.
- `Tile_WWFF_Steps`, `Tile_WFFF_Broken` and `Tile_WWWF_Broken` were built but not seen in a screenshot. Arch and alcove were seen in Test Swarm. Rubble and the jagged tops are first guesses.
- The Hub wasn't looked at again after the new tiles joined the Cathedral set, so its walls may differ from the last screenshot. Portals and props don't depend on them.
- Test Swarm ran at about 84 fps with the 480 enemies spawned, before they reached the player. Frame rate with the whole swarm on the player wasn't measured.

## Found along the way
- Comic shader pools have a fixed reach (`Assets/ArtStyleTest/Shaders/PL_Comic.shader:233`, Low, cosmetic). Additional lights are stepped on `distanceAttenuation`, which ignores Range and Intensity, so a point light only reaches about 2.4 m, and a light 3 m up lights nothing on the floor. The Hub works around this with many low lights. A range-based step would allow bigger pools, but changes every scene's look, so it was left.
- `LevelLayoutAuthoring`'s summary says rotation is ignored, but levels do turn with the object (`LevelLayoutBaker.cs:33` area, `blob.Rotation` in `LevelGenerationSystems.cs:470`). HauntedCathedral's layout is turned 45 degrees, and the copies inherited that until the creator reset it. Low, comment only.
- SampleScene and HauntedCathedral use `PlaceholderTileSet`, not `CathedralTileSet`, so the new shape tiles only appear in the Hub and test levels until those scenes switch sets. Low, may be intended.
- `Tile_WFFF` lists its south-west corner as Wall, but its wall collider covers x 0 to 2.5, z -2.5 to 0, which is the south-east quarter. It looked fine in play and may be a convention of the model, so this is unconfirmed. Low.
- The shape tiles add no colliders, so walkability stays the same. The alcove pillar and the steps stand up to 0.7 m out from the wall collider, and units can clip through them. Low, cosmetic.

## Tooling
- Restoring a scene file with git while the Editor has it open raises the modal "The open scene(s) have been modified externally" dialog. It blocks every `unity command` on the main thread, which then time out, and `unity status` still says ready. It happened tonight after the VfxManager additions were discarded. Reload was pressed, which was safe because the title showed no unsaved changes.
- `capture game_view` saves under `Assets/` (`Temp/x.png` becomes `Assets/Temp/x.png`), and that folder gets imported, so it needs clearing afterwards.
