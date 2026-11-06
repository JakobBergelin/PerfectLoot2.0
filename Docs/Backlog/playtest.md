# Playtest backlog

First real run of the recent features, on `flair/evening-polish`, driven through the Unity CLI in Play mode. Skills, passives and powers were equipped through their managers or `Apply`, enemies came from `EnemyManager.Spawn`, and mouse and keys were real Input System events. Values below were read from ECS.

## Result per feature

| Feature | Result | How it was checked |
| --- | --- | --- |
| Damage Meter HUD | Works | Bottom right, M and the button both toggle it, all 4 tabs and By unit/By skill switch, rows and rates update, the pointer over it blocks skills, rows reset with a new level. |
| Mirror Image | Works, after the shader fix below | Cast cost and cooldown apply, bars hidden, fires toward the cursor, gone 10 s after the cast, recast replaces the old copy, unequip and owner death remove it. All 12 copied skills ran on it without exceptions. |
| Bulwark | Works | Same hit: 9.16 with no passive, 5.50 from the front (-40%), 13.74 from behind (+50%). Also correct with no attacker, going by hit direction. |
| Quarry | Works | 100 to the target, 70 to others. Picks a new target when the old one leaves the radius. Killing the target refunded 20% of max resource. See bug 5 for the marker. |
| Undertow | Works | A Spider was pushed at +17 on x without it, a heavier Skeleton pulled at -10.5 with it (same hit, twice the force). A real Fireball hit pulled a Skeleton toward the player. |
| Slingshot | Works | Fireballs split at +20 and -20 degrees through a second player, both with visuals. No split with no ally in the way. |
| Soul Thief | Works | Soul drops, lasts 5.9 s, is stolen within reach, the stolen passive lasts 8 s and then goes. |
| Crossfire | Works | ChannelCostChange is +0.3 with Flamethrower, -0.2 without, 0 after removal. Flames go at about +90 and -90 degrees (33 flames at peak against 11), and sweep around during Whirlwind. Net drain measured: Whirlwind 6.7/s to 4.5/s without Flamethrower; 6.7/s to 10.1/s with it. |
| Borrowed Time | Works | 22.9 after resistances: 20% at once, 18.33 over 5 s. Keeps ticking after the item comes off. Shows in the meter. Runs before Last Stand and CheckDeathSystem. |
| Blood Pact | Works | Wolf hits gave +0 Life on Hit without it, +4.0 in 3 s with it. |
| Life on Hit | Works | +2.0 per main hit at 2 Life on Hit. |
| Resource on Hit | Works only in a test, see bug 1 | Moved 3 from target to player once the target was given a resource. |
| Scenes | Clean | MainMenu, SampleScene and HauntedCathedral, played directly and through `GameSceneLoader` (MainMenu to HauntedCathedral to SampleScene). No exceptions and no Burst compile errors. |

Not tested: the five legendary items equipped through the inventory (`Power.Apply` was called directly; each power is linked to an item asset). Bulwark and Borrowed Time took synthetic `DamageInstance` hits, not real enemy attacks.

The only exceptions in these sessions were 4 from `UpdateResourcesSystem`, caused by the test adding `Resource` to an enemy without `ResourceChanger`. Not a game bug.

## Fixed (cosmetic)

- **Mirror Image's copy wasn't dark.** `PL_Comic.shader` didn't declare `_BaseColor` for DOTS instancing, so every `URPMaterialPropertyBaseColor` was ignored. The copy's tint was set correctly in ECS but drew as a normal player. Now a dark silhouette, body and gear. Compared 8 enemy types before and after in HauntedCathedral: no color changes in the ones in view (walls hid part of the row).
- **Damage Meter text.** Tab and toggle text sat in the top left corner. Now centered in `DamageMeter.uss`.

## Bugs

### 1. Resource on Hit does nothing in play (High)
- Where: `Assets/Scripts/Systems/OnHitSystems.cs:48`, `Assets/Scripts/Monobehaviors/Managers/Enemies/EnemyEntitySetup.cs:81`
- Reproduce: equip gear with Resource on Hit and hit any enemy. The player's resource doesn't rise.
- Cause: it moves resource from the target, and only targets with a `Resource` qualify. No EnemyDefinition has `Stats.Resource` above 0, so none has one. The affix rolls on gear (a test character had 0.26) with no effect, and it's half of what Blood Pact offers.
- Decide: give enemies a resource, or grant the amount without taking it from the target.

### 2. Hit flash isn't visible on multi-part units (Medium, cosmetic)
- Where: `Assets/Scripts/Monobehaviors/UnitBarsAuthoring.cs:21`, `Assets/Scripts/Systems/HitFlashSystems.cs:45`
- Reproduce: hit a Dummy. Its body doesn't flash.
- Cause: `HitFlash` and the color sit on the root, which isn't drawn. On the Dummy, 10 of the 20 entities in its group are drawn and the root isn't one of them. The player's root has `HitFlash` but no color component, so the query skips it.
- Fix idea: write the flash to the drawn entities, as `MirrorImageTintSystem.Tint` walks the group. Each part needs its own base color, since the override replaces the material's color.

### 3. Tints on enemies and companions probably have the same problem (Medium, cosmetic, from reading)
- Where: `Assets/Scripts/Monobehaviors/Managers/Enemies/EnemyEntitySetup.cs:75`, `Assets/Scripts/Monobehaviors/Managers/Companions/CompanionEntitySetup.cs:62`, `Assets/Scripts/ScriptableObjects/ActiveSkills/CompanionSkill.cs:126`
- Cause: `ApplyTint` is called on the root only. None of the 8 enemies spawned here uses `OverrideColor`, so this wasn't seen in play.

### 4. Mirror Image's prefab leaks on every player death (Low)
- Where: `Assets/Scripts/Monobehaviors/Managers/ActiveSkills/ActiveSkillManager.cs:79`, `Assets/Scripts/ScriptableObjects/ActiveSkills/Skills/MirrorImageSkill.cs:68`
- Reproduce: equip Mirror Image, die and respawn, then check the old `MirrorImage.Prefab`. It still exists next to the new one.
- Cause: the prefab handle lived on the dead player, so `SetUpCopy` can't find it and `Remove` isn't called. About 34 entities per death, until the next level replaces the world.

### 5. Quarry's marker is hard to see (Low, cosmetic)
- Where: `Assets/Scripts/Vfx/VfxSystems.cs:132`
- Reproduce: equip Quarry near an enemy. The marker is a thin sliver over its head at gameplay zoom, easy to miss.

### 6. Re-equipping Mirror Image resets its 80 s cooldown (Low)
- Where: `Assets/Scripts/ScriptableObjects/ActiveSkills/Skills/MirrorImageSkill.cs:47`
- Reproduce: cast it, swap it out of its slot, swap it back. It's ready at once (measured right after a cast, with the copy gone).
- Cause: `Remove` drops the component, so the next `Apply` counts as new. Other cooldown skills weren't checked.

### 7. New lookups every frame in the Damage Meter recorder (Low, performance)
- Where: `Assets/Scripts/Systems/DamageMeterSystems.cs:49`
- Reproduce: enter Play mode. The console warns "creates a Lookup object during OnUpdate" for `DelayedDamageSystem`, `HealthRegenerationSystem`, `CaptainsShieldSystem` and `ShieldProjectileSystem`.
- Cause: the `DamageMeterRecorder` constructor creates four lookups per update for the first of these; the other three weren't traced. Lookups could be cached in `OnCreate`.

### 8. Quarry may lose its target when passives are pushed (Low, from reading)
- Where: `Assets/Scripts/ScriptableObjects/Passives/PassiveSkill.cs:33`
- Cause: `Apply` overwrites an existing component with a fresh one, so changing any passive clears `Target` and a new random one is picked.

### 9. Undertow's pull isn't limited by distance (Low, from reading)
- Where: `Assets/Scripts/Systems/Items/UndertowSystems.cs:34`, `Assets/Scripts/Systems/HealthSystems.cs:276`
- Cause: the pull is a velocity at twice the push. An enemy close to where the hit landed could overshoot it. Not seen in play.

### 10. Soul Thief reads one global passive list (Low, multiplayer, from reading)
- Where: `Assets/Scripts/Systems/Items/SoulThiefSystems.cs:154`
- Cause: `Equipped` asks `PassiveSkillManager.Instance`, so with per-player passives one player's slots would decide for another.

### 11. Damage Meter missing once (Low, not reproduced)
- In the first MainMenu to HauntedCathedral run the meter didn't show in any screenshot, though `DamageMeterHUD` existed. It was right after `DamageMeter.uss` changed. Three later runs (direct and through MainMenu) showed it, minimized and open.

## Questions

- **Should Mirror Image's hits give Life on Hit without Blood Pact?** `MirrorImageSourceSystem` says "on-hit effects and legendary powers apply", but `OnHitSystems.cs:37-40` treats the copy as a companion, so they need Blood Pact. From reading, not tested on the copy.
- **Should a copy's burns and bleeds count for it in the meter?** Their source is the owner, so from reading they go to Player 1 and the Mirror Image row shows direct hits only.
- **Stolen passives aren't in the player's `Passive` buffer** (checked in play). Anything listing passives from that buffer won't show them.
- **SkeletonArcher and Revenant can't be spawned in every scene.** `EnemyManager.Spawn` warns they aren't in the core EnemyPrefabs list unless the level uses them (SkeletonArcher failed in HauntedCathedral, Revenant in SampleScene). Fine if intended.
