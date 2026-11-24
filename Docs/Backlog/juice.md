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

- Priority 3 is done (loot beams, pickup sparkle, soul pop, portal swirl, spawn shimmer), except the level-up burst: nothing gives experience yet, so nothing would trigger it. Add it to `VfxSystem` once `Level.Value` can go up.
- Priority 4 is done, see below.
- Priority 5: HUD transitions, cooldown-ready flash, and the skill bar labels (1-4 should read LMB, RMB, Q, E).

## Priority 3 (seen in TestSwarm)

- Loot beams: a column of light over each ground item in its rarity's color, faint for Normal items (`GroundLootVfx.NormalAlpha`, on the GroundLootManager prefab). Dropped from four loot tables: gold, yellow, blue and gray beams showed.
- Pickup sparkle in the item's color, through the new `GroundLootManager.ItemPickedUp` event. Seen for a Legendary.
- Soul pop when a soul goes, taken or faded. Seen on a soul made by hand that faded.
- Portal swirl: motes circling into the ring. Subtle; raise the Swirl rate in `VfxBuilder.Flair.cs` if it should stand out more.
- Spawn shimmer: warm light and dust at the feet on every `CharacterManager.PlayerSpawned`, so entering a level and respawning. Seen both.
- `VfxManager.Tint` colors a pooled effect by setting its particles' start color, alpha included. Put an effect's own transparency in its Fade gradient if it will be tinted.

## Priority 4 (seen in TestSwarm)

Counted in Play mode with a test build (always crit, 30% dodge and block, Undertow, Bulwark and Slingshot added to the player), and the ice shards, crits, ripples and dust seen in screenshots.
- Ice shards (Shatter and Frozen Orb, any non-homing Cold projectile): an inked ice crystal with a frost trail, splinters where it breaks. Shards also break at the end of their range, so a Frozen Orb leaves a ring of splinters around it (up to 80 at once).
- Crit pop: a golden star over the unit on every crit (`DamagingSystem.OnDamageTaken`), at most 8 per frame.
- Dodge whoosh and block sparks, from `CalculateDamageSystem`'s events. Refraction absorbing a hit shows as a block.
- Footstep dust: a puff every 1.2 m a player walks, none while dashing.
- Mirror Image: violet sparkles on the copy, a pop when it comes and when it goes. The pop on going was not seen (the copy lasts its full duration).
- Undertow ripple: water drawn in at the feet of an enemy pulled this frame (`LastHit.HitForce` below 0). Only hits with knockback pull: Tornado does, Frozen Orb doesn't.
- Slingshot spark where a shot splits. Seen once, a shard through the Mirror Image copy.
- Bulwark: a gold half ring in front of the wearer when a hit lands from the front.
- Camera shake (`HitShakeSystem`): each skill asset has Crit Shake and Hit Shake (0 to 1); a hit shakes by Crit Shake when it crits, else by Hit Shake. The strongest hit of the frame counts, so a crowd doesn't add up. `CameraSettings` has Shake Strength (1 m at 1, trauma squared, so 0.3 is about 9 cm), Shake Decay and Shake Frequency; Shake Strength 0 turns it off. Crit Shake: Meteor 0.5, Cold Snap 0.5, Upheaval 0.45, Ball Lightning 0.35, Tornado 0.3, Hookshot Slash 0.25, Frozen Orb 0.15 (was 0.3, which felt too strong because its many shard crits keep refreshing it). Hit Shake, kept below that and 0 for skills that hit very often: Cold Snap 0.15, Hookshot Slash, Phantom Edge, Cursed Hammer and Riposte 0.12, Swipe and Frost Lance 0.1.
- Hits on players: one that takes at least `PlayerHitShare` (10%) of a player's max health shakes by `PlayerHitShake` (0.3).
- Impact shake: big one-shots shake even when they hit nothing, for any caster (players, enemies, bosses), when they play on screen. Set per effect as Shake in `VfxLibrary`'s inspector (kept on rebuild; first values in `VfxBuilder.ImpactShakes`): Meteor impact 0.45 (its own `MeteorImpact` entry, the fireball's look), Upheaval slam 0.35, quake and Trample 0.3, Earth Wall 0.25, Cold Nova 0.25, round strikes of 2 m or more 0.2, fireball impact 0.15.
- Hit pulse: a crit whose Crit Shake is at least `BigHitShake` (0.4) flashes chromatic aberration and a bloom boost (`HitPulse_Volume`, a global volume on the rig's camera whose weight is `CameraFollow.Pulse`) at `BigHitPulse` (0.6), fading over `PulseTime` (0.3 s). Boss intro (`FocusIntroSystem`): the first time a Camera Focus comes within Focus Range of a player, a full pulse and `BossIntroShake` (0.5), once per boss.
- Tested in Brightwater: meteor impact (trauma 0.45), a Meteor crit (shake and pulse), a hit on the player over and under 10%, the Hero Slime's intro (once). Not judged by feel in real play yet, and Hit Shake wasn't seen on a real hit (no skill with it was equipped).
- One shared camera (couch co-op has no split-screen): any player's hits shake it, the strongest of the frame. Hit shake and pulse are worked out where damage is (the host); impacts shake on every machine that plays them.

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
