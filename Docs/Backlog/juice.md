# Juice backlog

The stream stopped early on a usage budget, so priorities 3 to 5 were not started.

## Check first

- `Assets/Scripts/Editor/Vfx/VfxBuilder.Flair.cs` holds the new effects; rerun `Tools/VFX/Build Skill Effects` after a change. See "Effects check in Play mode" below for what has been seen.
- Cold Snap plays `ColdNova` on any single-frame Cold `Attack` (`VfxSystems.cs`, the `_newAttacks` loop), so other Cold attacks get it too. It is sized to `Attack.Radius`.
- Poison Dart's look applies to any non-homing Poison projectile, again by damage type.

## Not done from priority 1

- Ball Lightning and Sonic Ball still have no visual. Their entities are not plain projectiles: see `BallLightningSystems.cs:163` and `SonicBallSystems.cs:132`.
- Fists of Thunder keeps the existing `LightningHit` burst.
- `TimezoneFieldUISystem` (`TimezoneSystems.cs`) still draws its debug shape. The Fire Hydra and Tesla Coil ones are deleted.

## Not started

- Priority 3: ground loot beams, pickup, Soul pickup and level-up bursts, portal swirl, respawn.
- Priority 4: camera shake, footstep dust, dodge and block flashes, crit pop, Mirror Image shimmer, Undertow ripples, Slingshot spark, Bulwark shimmer.
- Priority 5: HUD transitions, cooldown-ready flash, and the skill bar labels (1-4 should read LMB, RMB, Q, E).

## Notes

- `VfxManager.Spawn` allocates one `Active` object per effect (`VfxManager.cs`, Low, performance). Status puffs add up to 50 spawns per 0.4 s at the cap, so pooling `Active` would be a cheap win.
- Before this stream, a rebuild would have dropped SoulOrb and QuarryMark from the library, since the builder did not create them and replaces `Entries`. They were hand-added entries pointing at the fireball and bolt prefabs. Fixed by building real prefabs.

## VFX builder adds a second VfxManager to scenes (fixed)
`Tools/VFX/Build Skill Effects` added a scene `VfxManager` to SampleScene and HauntedCathedral, although `GameRig.prefab` already has one. The builder no longer touches scenes; GameRig's VfxManager uses the library it builds.

## Effects check in Play mode (TestArena)

Seen in the Game view:
- Flamethrower: warm yellow and orange puffs along the aim line, at unit height (y 1.05). Reads well, though the puffs look like flat round dots.
- Frozen Orb: pale blue ball with small white motes. Readable; the motes are tiny at gameplay zoom.
- Poison Dart: green trail with a thin dark sliver at the head. Too thin at gameplay zoom to tell whether the Bolt mesh is inside-out, so that question is still open.
- Tesla field: sapphire ring on the floor at radius 6.3 after the fix below.
- Timezone: frost ring at radius 8.4, seen before the fix only. The band is wide at that scale (the ring mesh width scales with radius).

Fixed: `VfxManager.Scale` multiplied the ring height too, so the rings moved up or down with their radius (Timezone measured y -0.09, under the floor). `Scale` now sizes effects sideways only, and both rings are at -1 m from their unit-height entity. Timezone measured y 0.05 and showed as a frost ring on the floor. Tesla uses the same numbers but was not looked at again.

Seen in the Game view later (TestSwarm):
- Status puffs: burn, poison, chill and bleed all show on afflicted units. The earlier "no puffs" came from the test enabling `Burning` without a `BurnInstance`, which the game switches straight back off. Made about 30% bigger, with one more particle each.
- Fire Hydra: an orange glowing body with heads where it is cast.
- Cold Snap: now a ring of frost shards at the attack's radius (seen at 6.2 m), frost inside, and a flash on the caster.

Testing notes:
- `InputsDataSystem` overwrites `SkillHeld` every frame, so disable that system in Play mode and set `SkillHeld` once, rather than from `EditorApplication.update`. Only the lowest held slot is used, so hold one skill at a time.
- Status puffs need the real state: add a `BurnInstance` or `BleedInstance`, or set `Poisoned` and `Chilled` with a `TimeRemaining`, before enabling them.
- `SkillCastRequest` did nothing for Frozen Orb and Poison Dart while they were not equipped. Equipped in slots and held, they fired.
- Held Flamethrower drains the resource in seconds, so refill `Resource.CurrentValue` during tests.
- A purple ring with a white and purple oval sits in the TestArena at the cursor side of the room. It is not a `VfxInstance`, and was there before anything was cast. The same shape sits next to the player in TestSwarm, so it is probably the level's portal.
- The builder also rewrites material files each run (keyword lists on five materials, render settings on the two ring materials). The rebuilt versions are committed.
