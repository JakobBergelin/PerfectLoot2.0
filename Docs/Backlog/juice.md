# Juice backlog

The stream stopped early on a usage budget, so priorities 3 to 5 were not started and nothing below was looked at in Play mode.

## Check first

- None of the new effects has been seen in Play mode or screenshotted. Sizes, heights and colors are first guesses. `Assets/Scripts/Editor/Vfx/VfxBuilder.Flair.cs` holds them all; rerun `Tools/VFX/Build Skill Effects` after a change.
- Field ring heights are guesses: Tesla at -0.8 and Timezone at +0.1 from the entity position (`VfxBuilder.Flair.cs`, the `Field` calls). If a ring floats or sinks, change those numbers.
- Cold Snap plays `ColdNova` on any single-frame Cold `Attack` (`VfxSystems.cs`, the `_newAttacks` loop), so other Cold attacks get it too, at a fixed size that ignores `Attack.Radius`.
- Poison Dart's look applies to any non-homing Poison projectile, again by damage type.

## Not done from priority 1

- Ball Lightning and Sonic Ball still have no visual. Their entities are not plain projectiles: see `BallLightningSystems.cs:163` and `SonicBallSystems.cs:132`.
- Fists of Thunder keeps the existing `LightningHit` burst.
- `FireHydraUISystem` and `TeslaCoilFieldUISystem` still draw their debug spheres. They use `Debug.DrawLine`, so they show in the Scene view or with Gizmos on, not in a build. Delete them once the new looks are approved.

## Not started

- Priority 3: ground loot beams, pickup, Soul pickup and level-up bursts, portal swirl, respawn.
- Priority 4: camera shake, footstep dust, dodge and block flashes, crit pop, Mirror Image shimmer, Undertow ripples, Slingshot spark, Bulwark shimmer.
- Priority 5: HUD transitions, cooldown-ready flash, and the skill bar labels (1-4 should read LMB, RMB, Q, E).

## Notes

- `VfxManager.Spawn` allocates one `Active` object per effect (`VfxManager.cs`, Low, performance). Status puffs add up to 50 spawns per 0.4 s at the cap, so pooling `Active` would be a cheap win.
- Before this stream, a rebuild would have dropped SoulOrb and QuarryMark from the library, since the builder did not create them and replaces `Entries`. They were hand-added entries pointing at the fireball and bolt prefabs. Fixed by building real prefabs.

## VFX builder adds a second VfxManager to scenes
`Tools/VFX/Build Skill Effects` adds a scene `VfxManager` to SampleScene and HauntedCathedral, although `GameRig.prefab` already has one. The coordinator discarded those scene changes on this branch. Running the tool again brings them back, so the builder should stop adding it.
