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

## Effects check in Play mode (TestArena)

Seen in the Game view:
- Flamethrower: warm yellow and orange puffs along the aim line, at unit height (y 1.05). Reads well, though the puffs look like flat round dots.
- Frozen Orb: pale blue ball with small white motes. Readable; the motes are tiny at gameplay zoom.
- Poison Dart: green trail with a thin dark sliver at the head. Too thin at gameplay zoom to tell whether the Bolt mesh is inside-out, so that question is still open.
- Tesla field: sapphire ring on the floor at radius 6.3 after the fix below.
- Timezone: frost ring at radius 8.4, seen before the fix only. The band is wide at that scale (the ring mesh width scales with radius).

Fixed: `VfxManager.Scale` also multiplies the ring height, so Timezone floated at y 1.89 and Tesla would have been at about y -4. Heights in `VfxBuilder.Flair.cs` are now about -1 / radius. Tesla measured y 0.04 afterwards. Timezone should land at y 0.08 by the same sum, but was not looked at again. The height depends on radius, so a rolled radius moves the ring a little; scaling only the ring in XZ would be the proper fix (Low).

Not seen, because the budget ran out:
- Fire Hydra and Cold Snap: held in slots, but no `VFX_FireHydra` or `VFX_ColdNova` showed up in the one dump taken a moment after, so they may just have been missed.
- Status puffs: enabling `Burning`, `Poisoned`, `Chilled` and `Bleeding` on the dummies repeatedly gave no `VFX_Status*` instances in the dumps. `Burning` and `Bleeding` may need instance buffers. Not diagnosed.
- Damage Meter rows.

Testing notes:
- `InputsDataSystem` overwrites `SkillHeld` every frame, so disable that system in Play mode and set `SkillHeld` once, rather than from `EditorApplication.update`.
- `SkillCastRequest` did nothing for Frozen Orb and Poison Dart while they were not equipped. Equipped in slots and held, they fired.
- Held Flamethrower drains the resource in seconds, so refill `Resource.CurrentValue` during tests.
- A purple ring with a white and purple oval sits in the TestArena at the cursor side of the room. It is not a `VfxInstance`, and was there before anything was cast. Not identified.
- The builder also rewrites material files each run (keyword lists on five materials, render settings on the two ring materials). The rebuilt versions are committed.
