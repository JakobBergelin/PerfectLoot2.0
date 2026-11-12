# Multiplayer Plan

How local split-screen and later online play can work. Written 2026-11-06 on `flair/evening-polish`. No code has been changed for this plan.

**Update 2026-11-13:** phases O2 to O5 are done on `net/foundation`. How the online game works now is in
[MultiplayerArchitecture.md](MultiplayerArchitecture.md).

**Update 2026-11-12:** couch co-op has shipped since (shared camera with a tether, `CoopManager`, `CoopHUD`, per-player managers and HUDs through `IPerPlayer`), so the local phases M1-M5 below are mostly history. The online plan agreed on 2026-11-12, with Steam, is in [Online with Steam](#online-with-steam-2026-11-12) at the end. It builds on the facts in "Networking" below, which still apply.

Marks: **[V]** verified by reading the code at this commit. **[A]** assumed or from memory, check before relying on it. Nothing here was run with two real players.

Netcode for Entities is not in this project (`Packages/manifest.json`, `Library/PackageCache`). Everything said about it is from memory unless marked otherwise. Docs exist for a [`com.unity.netcode` 6.5](https://docs.unity3d.com/Packages/com.unity.netcode@6.5/manual/index.html), but I could not confirm which version pairs with Entities 6.6. Check Package Manager first.

## Summary

- **The simulation is mostly ready. The layer around it is not.** Skills, damage, enemy AI and flow fields work per entity. Input, managers, camera and HUDs all assume one player.
- **Split-screen needs no networking.** All players live in one world, so it can ship on its own, and its work also pays for online.
- **Order:** per-player input and managers, then cameras and HUDs, then a networking spike.
- **Online recommendation:** Netcode for Entities with a player-hosted server. The server runs the whole simulation; clients predict only their own movement. Decide after a spike, because of the blockers in "Networking".

## What is already ready [V]

| System | Why it works |
|---|---|
| Skill systems | Per-entity queries over `InputsData`, `Skill`, `DesiredDirection`. No player singleton. |
| Enemy targeting | `EnemyTargets` lists every `EnemyTargetable` unit each frame (`EnemyDetectionSystems.cs:75`). Detection uses nearest and threat. |
| Flow fields | One per `FieldSlot`, up to 4 (`FlowFieldSystems.cs:156,243`). Enemies use the field of the unit they chase. Works once slots are assigned. |
| Companions, thralls | Owner-based, and take the owner's `FieldSlot` (`CompanionEntitySetup.cs:35`). |
| Mirror Image | Copies its owner's `InputsData` (`MirrorImageSystems.cs:131`), and its prefab is stored per owner (`MirrorImageSkill.cs:70`). |
| Effects, bars, visuals | `EffectDisplay`, `HoveredEffect` and `EquippedVisual` are buffers on each player. |
| Life Link | Walks all players (`LifeLinkSystems.cs:36`). |
| Menus | Nothing uses `Time.timeScale`, so one player's menu never pauses the others. |
| Test tools | `CharacterManager` can spawn a `TestPlayer` on field slot 1, so several players can be tried today. |

## Inventory of one-player assumptions [V]

**Input**

| Where | Assumption | Fix |
|---|---|---|
| `InputsDataSystem.cs:16,24` | One `InputSystem_Actions`, reading all devices | One per local player (M1) |
| `InputsDataSystem.cs:41` | Writes every `InputsData` from that device, including Mirror copies | Query `LocalPlayer` too; keep the `DesiredDirection` loop as is |
| `InputsDataSystem.cs:39` | `Camera.main` for the aim ray | The player's camera |
| `MirrorImageSystems.cs:94` | Runs after `InputsDataSystem` to overwrite it | Still fine; a copy simply stops being written first |
| `MenuPanels.cs:13-16,43` | Static open panel, blockers, typing | Per-player instance |
| `CharacterPanelHUD.cs:113`, `SkillsPanelHUD.cs:53`, `ItemDatabasePanelHUD.cs:97` | Each makes its own actions | Use the player's `LocalPlayerInput` |
| `Keyboard.current`, `Mouse.current` in 7 HUDs | Global devices | See `ControllerSupportPlan.md` C0 |

**Managers**

| Where | Assumption | Fix |
|---|---|---|
| `CharacterManager.cs:17,212,64` | `Instance`, one `_player`, one `Class`, `Resource`, respawn timer | One per player |
| `CharacterManager.cs:20,23` | Static `PlayerSpawned` and `PlayerDied` | Instance events |
| `ActiveSkillManager.cs:13,121-122` | `TryGetPlayer` takes `players[0]`. Query order is not a stable meaning of "first". | Holds its own player |
| `PassiveSkillManager.cs:10,147` | Pushes one list to **every** player | Own player only |
| `InventoryManager.cs:19,259-268` | Pushes one gear set to **every** player; `_appliedPowers` is shared across the loop (see backlog) | Own player only |
| `AccountManager.cs:29` | One account, one item database, one drop queue | Depends on question 1 |
| `SoulThiefSystems.cs:156` | ECS system asks `PassiveSkillManager.Instance` whether a passive is equipped | Read the owner's `Passive` buffer |

**Camera and HUD**

| Where | Assumption | Fix |
|---|---|---|
| `CameraFollowSystems.cs:21` | Loop overwrites, so the last non-test player wins | `CameraFollow` names its target |
| `CameraMonoBehavior.cs:37,45` | `GetSingleton` on the camera entity | Look up by player index |
| `Camera.main` in `HealthSystems.cs:20`, `ResourceSystems.cs:15`, `CombatTextHUD.cs:167`, `GroundLootHUD.cs:85`, `PortalHUD.cs:88`, `DungeonManager.cs:455` | One camera | HUDs use their player's. Bars only use `forward`, which is equal for all cameras at the same pitch, so they can stay. |
| `CharacterManager.Player` in `EffectBarHUD.cs:106`, `UnitPanelHUD.cs:99`, `SkillTooltip.cs:25`, `CharacterPanelHUD.cs:814` | The HUD's player | Context from M2 |
| `PortalHUD.cs:122-125` | `players[0]` | Its own player |
| `SkillBarHUD`, `DropPopupHUD`, `DungeonHUD` | Manager `.Instance` | Context from M2 |
| `Assets/UI/PanelSettings.asset` | One 1920x1080 panel for all HUDs | See M4 |
| `GameRig.prefab` | One camera with one `AudioListener` | Only one listener in the scene |

**World**

| Where | Assumption | Fix |
|---|---|---|
| `PlayerEntitySetup.cs:13` | Real players always get `FieldSlot` 0, only test players get a slot | Take a player index |
| `DamageMeterSystems.cs:87`, `DamageMeterHUD.cs:232` | Names come from `FieldSlot + 1`, so two real players are both "Player 1" | Fixed by the line above |
| `MeterName` (`DamageMeterComponents.cs:78`) | Rows are keyed by skill label or unit type, with no owner, so two players' Fireballs and companions merge | Add owner index, plus a per-player view |
| `LevelGenerationSystems.cs:106` | Chunks stream around the last player only | Stream around each player |
| `LevelGenerationSystems.cs:515-517`, `EnemySpawner.cs:40` | Spawners trigger on `CharacterManager.Player` only | Any player in range |
| `DungeonManager.cs:444` and its `TryGetPlayer` callers (273, 311, 345, 392) | Waves, boss, exit check and return portal use one player | Pick by rule: nearest, or random living player |
| `DungeonManager.cs:385` | One death fails a Survive step | All players dead, or a rule from you |
| `GroundLootManager.cs:124,132,144` | Reach and pickup use one player, and items go to the one account | Pass the picking player |
| `GameSceneLoader.cs` | Replaces the whole world on a portal | Fine for a party that moves together; needs a rule (question 5) |
| `GameArea.IsTown` | One area at a time | Fine under the same rule |

Harmless for now: `EnemyManager.cs:168` (dev spawn position), `VfxManager.Instance` and `AssetStorage.Instance` (shared by design), and `LootGenerationManager.Instance` (stateless service as far as I read, not fully checked).

## Phases: local split-screen

Sizes are rough: S is a day or less, M a few days, L a week or more.

**M1. Per-player input (M).** Add `LocalPlayer { int Index }`, and `LocalPlayerInput` as in the controller plan. `InputsDataSystem` loops over local players and gives each its own actions, camera and menu state. Pair devices through the generated `devices` property. Ships with one player and no visible change. Shared with controller phase C0.

**M2. Per-player context (L).** Split `GameRig` into a shared `WorldRig` (dungeon, ground loot, enemies, VFX) and a `PlayerRig` (camera, Character, ActiveSkill, Passive and Inventory managers, HUDs), one instance per player. A `PlayerContext` on the `PlayerRig` root replaces the `.Instance` calls for those managers (`GetComponentInParent`). This is many edits, but simple ones. Keep state in the managers rather than moving it to ECS: they hold ScriptableObject loadouts and are not hot code. Managers push only to their own player. Assign `FieldSlot` from the index. Fixes the Soul Thief lookup too. Ships with one player and no visible change.

**M3. World rules for several players (M).** Spawner triggers, chunk streaming, dungeon targeting, fail rule, loot pickup, meter owner. Test with a second real `PlayerRig` on one screen with a shared camera, before any split rendering.

**M4. Split-screen cameras and HUD (L).** `Camera.rect` per player: left and right for 2 players, quarters for 3-4. `CameraFollow` targets by index. HUD roots stretch to the player's viewport rather than the screen, and world-anchored labels subtract the root's origin. The hard part is size: panels are designed for 1920x1080 and a half-screen viewport can't hold the character panel at full scale. Try scaling the root first. If that fails, try a `PanelSettings` per player rendering to a texture. [A] Spike this before committing.

**M5. Join flow (M).** Press Start on an unpaired pad to join, in town only. Character pick for the new player, leave, and pad disconnect handling. Save handling depends on question 1.

## Networking

Work on M1-M3 first: they also create the seams online needs. Input per player becomes commands, and manager pushes become requests.

### Facts about this code that matter [V]

1. **No fixed tick.** Every gameplay group is in `SimulationSystemGroup` at frame rate; "FixedStep" appears nowhere in `Assets/Scripts`, and 55 files use `DeltaTime`. Prediction needs a fixed tick.
2. **Components are added at runtime.** `PlayerEntitySetup.AddGameplayComponents` builds a player on a visual-only prefab. `Apply` adds and removes components as gear changes, across about 25 skill, 24 passive and 35 legendary script files. Replicated ghost components must be on the ghost prefab. [A] This is the main blocker for replicating the full player.
3. **Prefabs are built at runtime** (`MirrorImageSkill.cs:85-93`, thralls from enemy bodies). Ghost prefabs must match on both sides. [A]
4. **Simulation calls managed code:** `VfxSystems`, `RaiseThrallSystems.cs:142`, `SoulThiefSystems.cs:156`, and loot goes `DungeonManager` to `GroundLootManager` to `AccountManager`.
5. **Randomness:** `AttackSystems.cs:20,216,343` and `HealthSystems.cs:113` seed from the tick count, and `SoulThiefSystems.cs:56`, `DungeonManager.cs:319,331` use `UnityEngine.Random`. Fine on a server only; prediction of these could never match.
6. **The world is disposed on each portal** (`GameSceneLoader.cs`). Netcode worlds and connections must survive a level change, so this needs a server-driven level change.
7. **Levels are generated at runtime** from a seed (`LevelGenerationSystems.cs:134`). Good: send the seed, not the level. Whether the solve gives the same result on every machine is unverified. [A]
8. **Items are small:** saves store base item id, seed, rarity and roll version (`AccountSaves.cs:68-75`). Cheap to send.
9. **Cosmetics follow entities:** `VfxSystems` binds effects to new `Projectile`, `Attack` and `Soul` entities. If those exist on clients, much VFX just works. Combat text needs its events replicated.
10. **No world filters:** 214 `UpdateInGroup` uses, 0 `WorldSystemFilter`. But most gameplay sits under a few groups in `ExecutionGroups.cs`, a natural place to mark server-only. Whether a group's filter covers its children needs checking. [A]

### Options

| Option | Fit | Verdict |
|---|---|---|
| Netcode for Entities, full prediction | Native to DOTS. Must predict skills, spawns and structural changes, and all the facts above bite. | Too costly for co-op |
| **Netcode for Entities, thin prediction** | Server runs all of the simulation unchanged. Clients get a small replicated view and predict only their own movement. | **Recommended, pending spike** |
| Client-authoritative movement over Netcode for Entities | Each client owns its position; server checks it loosely. No rollback, and simplest to build. Cheating is possible, which matters little in friend co-op. | Good fallback if prediction is painful |
| Own replication on Unity Transport | Full control, but you write ghost sync, relevancy and snapshots yourself. | Only if Netcode for Entities fails the spike |
| Netcode for GameObjects | Built for GameObjects, not entities. | No |
| Deterministic lockstep | Needs a fixed tick, exact determinism, no managed code in the simulation, and hurts late joining. | No |

### Recommended design

- **Topology:** one player hosts (listen server), friends join through a relay. No dedicated servers at first. The Multiplayer Services packages are the obvious first try, Steam sockets if you ship on Steam. [A]
- **Client world:** does not run skill or damage systems. It shows ghosts, runs VFX and HUD.
- **Replicated player view:** transform, health, resource, skill slots with cooldown state (the `Skill` buffer is what the HUD reads), `EffectDisplay`, visuals. A fixed component set on the prefab, so per-skill components stay on the server. This sidesteps blocker 2.
- **Commands:** `InputsData` becomes the command: `Move`, held and pressed skills, aim point, and hovered target. `SkillPressedThisFrame` needs latching across ticks; only `RaiseThrallSystems.cs:35` reads it.

What each side owns:

| Thing | Authority | Client behaviour |
|---|---|---|
| Own movement | Server | **Predicted** with a small movement-only copy of `CharacterMovementSystem` and `UnitMoverSystem` |
| Dash, Teleport, Ball Lightning, Sonic Ball, Hookshot | Server | Not predicted at first: the player would feel latency here. Dash is the first candidate for prediction. |
| Skill casts, cooldowns, resources | Server | Instant cosmetic response (animation, sound), real result arrives after |
| Damage, crits, effects, death | Server | Interpolated, numbers from events |
| Enemies, companions, projectiles | Server | Interpolated ghosts. Projectiles may need events instead of ghosts if bandwidth suffers. |
| Level | Server sends seed | Generated locally, with colliders for movement prediction |
| Loot rolls | Server | Receives the item |
| Inventory, passives, skill choice | Client asks, server applies | Server checks the request against the items the client says it owns |
| Saves | Each client, local | Trusted. Fine for friends, not for trading or ladders. |

### Loot rules

Recommended: **personal loot.** Each kill rolls separately for each player, and only they see and can pick up their drops. It suits a game about hunting the perfect roll, avoids fights over items, and needs no trading. It maps well to the code: `GroundLootManager` gets an owner per item, and `DropFromTable` (called from `DungeonManager.cs:210`) runs once per player. In split-screen each HUD filters labels by owner. The drop rate per player stays the same.

### Online phases

**N0. Spike (L, a throwaway branch).** Add the package. Host plus one client. One player ghost with the thin view, one enemy type, move and cast Fireball, in the town scene. Must answer: the right package version; two-world or single-world host and its cost; world filters on groups; a runtime-composed ghost prefab; how the fixed tick feels; bandwidth with 40+ enemies (`DungeonDefinition.MaxAlive` default is 40, level spawners add more).

**N1. Fixed tick offline (L).** Move gameplay to a fixed rate, with interpolation for rendering. Gameplay feel will shift, so it needs its own testing. Worth doing only after N0 says yes.

**N2. Server and client split (L).** Filters, thin player view, enemy ghosts, commands, prediction for movement.

**N3. Events and sessions (L).** Combat text and VFX events, level seed, server-driven level change replacing `GameSceneLoader` for online, join and leave, relay.

**N4. Loot and loadouts online (M).** Requests, personal loot, reconnect rules.

## Risks

- **Size of M2.** Largest refactor before anything visible happens. Mitigation: it ships with no change for one player, one manager at a time.
- **Split-screen UI.** Panels may need a second layout. Biggest unknown in the local work.
- **Render cost.** Each viewport renders the scene again, and `PC_Renderer` has two renderer features I did not check against `Camera.rect`. Measure in M4 on target hardware. [A]
- **Flow field cost** scales with players: each field refreshes separately and walks physics. Should be fine at 4, not measured.
- **Fixed tick.** Touches every skill's feel, and frame-based assumptions may hide in the skills.
- **Netcode version risk.** Entities 6.6 is newer than what I know. The package's API may differ from my memory, so facts marked [A] may need rework.
- **Late joins and level streaming** online, since each client streams its own chunks around its player, and the server around all of them.

## Questions for you

1. Split-screen accounts: is the second player a second character on the same account (and can two characters wear the same item?), or a second account on the same machine?
2. Maximum players: 2 or 4? `FlowFields.MaxFields` is 4 today; 2 makes the UI far easier.
3. Is true split-screen a must, or is a shared camera acceptable? Shared would skip M4 but needs a leash, and the flow field comment suggests you want players far apart.
4. Death in a party: wait for respawn as now, or revive by a teammate? Does a Survive step fail only when all are down?
5. Portals: does one player pull everyone along, or must all stand in range or vote?
6. Personal loot as recommended, or shared loot that can be fought over or traded?
7. Is cheating a concern at all (trading, ladders)? If yes, saves must move to a server, which is a much bigger project.
8. Should online and split-screen combine (two local players joining an online game)? I'd leave this out at first.
9. Enemy scaling by player count: wanted later? Stats are placeholders, so this only needs a hook for now.
10. Which platforms? Steam or console changes relay and session choices.

## Online with Steam (2026-11-12)

Agreed with you on 2026-11-12. Supersedes "Online phases" above where they differ; the facts in "Networking" still hold.

### Decisions

| Topic | Decision |
|---|---|
| Netcode | Netcode for Entities. From Unity 6.5 it is a core package bundled with the editor, so it pairs with Entities 6.6. |
| One code path | Every game runs through netcode; single player is a host with nobody connected. |
| Topology | Listen server: one player's game hosts, up to 3 machines join. No dedicated servers, no host migration. |
| Authority | Server runs the whole simulation. Clients predict only their own players' movement ("thin prediction" above). |
| Players | At most 4 per game, couch players included. Couch co-op stays at 2 per machine (`GameInput.MaxPlayers`). The host turns down a join that would go over 4. |
| Movement rules | The same as couch co-op: `CoopTetherSystem` and `CoopRules` apply to every player, local or remote. |
| Joining | Only while both the joiner and the host are in a town (the Hub or a hub-like level). The join button only shows then; otherwise the game may still show that a friend is in town and can be joined. |
| Trust | The host accepts the items, skills and passives a friend's client sends. Saves stay local. No anti-cheat. |
| Loot | Personal: each player rolls and sees only their own drops. |
| Host leaves | Everyone returns to their own Hub. |
| Steam library | Steamworks.NET. |
| Platform layer | One interface with two versions: Local (direct IP, for development) and Steam. Gameplay never calls Steam directly. |

### Rich Presence

Published by every game, read by friends' games:

| Key | Value | Use |
|---|---|---|
| `pl_area` | `town` or `dungeon` | The join button shows only for `town` |
| `pl_players` | Players in the game, couch included | Hidden at 4 |
| `pl_lobby` | The host's Steam lobby id | What the join button joins |
| `connect` | `+connect_lobby <id>` | Steam's own Join Game in the friends list, and overlay invites |
| `status`, `steam_display` | "In the Hub (2/4)" | Needs our own App ID: the localization file can't be uploaded for App 480 |

### Join flow

1. Hosting: in town, the host's game creates a Steam lobby (up to 4 players) and publishes the keys above. It updates them on every area change and player count change.
2. The joiner's Hub lists friends playing PerfectLoot (`SteamFriends`) whose `pl_area` is `town` and `pl_players` is under 4, and shows a "Join <name>" button where the couch co-op prompt is (`CoopHUD`). Outside town it shows only a notice.
3. Clicking joins the lobby, reads the host's SteamID and connects over Steam Networking Sockets (Steam relay, addressed by SteamID).
4. Steam's Join Game and overlay invites arrive as `GameLobbyJoinRequested_t` or `GameRichPresenceJoinRequested_t`, or as a `+connect_lobby` launch argument, and take the same path from step 3.
5. The host checks the player count and that it is still in town, then sends the level seed and current scene. The joiner loads it and its players spawn next to the host's.

### Testing without being on Steam

- **No Steam at all (phases O2-O5):** Unity's Multiplayer Play Mode runs up to 4 players inside the editor, and builds join each other by IP on a local network through the Local platform layer.
- **Steam with no store page:** App ID 480 ("Spacewar") is owned by every Steam account. With `steam_appid.txt` set to 480, the friends list, Rich Presence keys, lobbies and Steam networking work, which is all the in-game join button needs. Steam's own Join Game button with 480 is reported to be unreliable, so it is not part of the first test. It needs two Steam accounts on two PCs.
- **Later:** a private App ID through Steam Direct ($100). The store page stays unpublished; builds go to testers through keys or a private branch, and the `status` text works.

### Phases

**O1. Steam platform layer (M).** Steamworks.NET as a service next to `AccountManager` (`GameBootstrap`), safe with Steam not running. Platform interface with Local and Steam versions. Publish and read Rich Presence; list friends playing. Testable with App 480 before any netcode.

**O2. Netcode spike (L, throwaway branch).** As N0 above: host plus one client, one player ghost, one enemy, move and cast Fireball in the Hub. Answers the open facts (fixed tick feel, world filters on the groups in `ExecutionGroups.cs`, ghosts for players built at runtime, bandwidth with 40+ enemies).

**O3. Netcode foundation (L).** Fixed simulation tick (N1). Server and client worlds replace the single world; `GameSceneLoader` becomes a host-driven scene change that keeps connections. Player ghost with a fixed replicated set (transform, health, resource, `Skill` buffer, `EffectDisplay`, visuals). `InputsData` becomes the command, for up to 2 local players per connection. Predicted movement.

**O4. Gameplay replication (L).** Enemies, companions and projectiles as interpolated ghosts. Combat text, hit flashes and telegraph markers from events or replicated cast state. VFX keep binding to ghosts on the client.

**O5. Loadouts, loot and sessions (L).** Client managers (inventory, skills, passives, character) send changes to the host as requests; the host applies them with the existing `Apply` code. Personal loot. Host and join by IP, the 4-player limit, joining only in town, tether for every player, disconnects, host leaving.

**O6. Steam join (M).** Steam Networking Sockets as a Unity Transport network interface (Unity Transport 2.3 added a custom network family for addresses that aren't IPs; there's a [community Netcode for Entities Steamworks integration](https://discussions.unity.com/t/here-is-my-steamworks-integration-for-netcode-for-entities-and-transport-use-as-you-like/942447) to start from). Lobbies, the Hub join button and notice, Steam Join Game and invites.

### Open risks

- Everything in "Risks" above under networking still applies, mainly the fixed tick and the components added at runtime.
- Two local players on one client connection: each player entity needs its own command stream. Check in O2.
- Netcode package numbering: the docs use 6.x labels while older packages were 1.x. Confirm the bundled version in Package Manager in O2.

## Spike results (O2, 2026-11-12)

Branch `spike/netcode`, not merged. Tested in the editor in the Hub: a single-world host, plus a second, real client world created in the same process and connected over loopback (`ClientServerBootstrap.CreateClientWorld`, subscenes loaded into it by hand). Not tested with Multiplayer Play Mode, builds or real latency.

### What worked

| Question | Answer |
|---|---|
| Package version | Netcode for Entities **6.6.0**, Unity Transport **6.6.0** and Multiplayer Play Mode **3.0.0** ship with editor 6000.6.5f1; adding them to `manifest.json` is enough. |
| Host model | **Single-world host** works: one world runs the server and the host's own client, so the host plays exactly as before (managers, rendering, couch co-op untouched). It is experimental: needs the `NETCODE_EXPERIMENTAL_SINGLE_WORLD_HOST` define, and throws in builds made with only `UNITY_CLIENT` or `UNITY_SERVER`. Fallback: the binary host (separate server and client worlds), which would need the managers to read the client world. |
| Fixed tick | Free: Netcode runs the server's `SimulationSystemGroup` at its own fixed rate (60 Hz), and the existing `DeltaTime` code works unchanged. Phase N1 shrinks to a feel check. |
| World filters | `[WorldSystemFilter]` on the groups in `ExecutionGroups.cs` covers almost everything: gameplay groups server-only (`GameWorlds.Server`), `FeedbackSystemGroup` client-only, `InputSystemGroup` everywhere. Only 4 loose systems needed their own filter (`SkillActivationSystem`, `MirrorImageInputSystem`, `DamageMeterSystem`, `InitializeFromSourceSystem`). |
| Players built at runtime | Works. The player prefab (`PL_Base_Visuals`) is an owner-predicted ghost; the server adds all gameplay components at runtime exactly as today (`PlayerEntitySetup`), and they stay server-only. Only components a client must see need to be baked onto the prefab (`NetReplicatedAuthoring`: Health, Resource, the input command). The "components added at runtime" blocker is gone. |
| Enemies built at runtime | Works unchanged: `EnemyManager`'s template spawning gives ghost instances. 40 Skeletons reached the client with position and health. |
| Projectiles made in code | Works with one generic ghost prefab (`NetPrefabsAuthoring.Projectile`): `FireballSystem` instantiates it instead of `CreateEntity` and adds its components as before. Each remote Fireball appeared and vanished on the client in step. |
| Input | The remote client runs the normal `InputsDataSystem` on the player ghost it owns; `NetInputSendSystem` packs `InputsData` into an `IInputComponentData` command; on the server `NetInputReceiveSystem` unpacks it before skills run, finding presses itself (held now, not last tick), so a press is never lost or doubled. The remote player moved and cast Fireball. |
| Two local players per connection | Works: two ghosts owned by one connection get separate command streams. |

### What broke and how it was fixed

- **`World.DefaultGameObjectInjectionWorld` in `OnCreate`:** 19 `SystemBase` systems fetched their command buffer system through it. Netcode creates a world's systems before that world becomes the default, so they threw. Fixed by using the system's own `World`. Worth bringing to `main` as is.
- **Physics never ran, so nobody could move.** Netcode moves Unity Physics into its prediction loop, which only runs once a connection is in game **and** predicted ghosts exist; until then the physics world is empty and every floor check fails. Needs all three: every connection goes in game (`GoInGameSystem`), `NetCodePhysicsConfig` set to `AlwaysRun` (in `CoreEntities`), and `ClientTickRate.PredictionLoopUpdateMode = AlwaysRun` (`NetClientConfigSystem`).
- **A remote client spawned its own local player.** `CharacterManager` now skips spawning on a remote client; the client instead adopts the player ghost it owns (`NetSpikeDriver`).

### Open, for O3 onward

Updated 2026-11-12, after O4 and O5's first step, on `net/foundation`.

- **Own player's prediction:** the client's own owner-predicted ghost showed a stale position, because no prediction systems run for it yet. Either move player movement (`CharacterMovementSystem`, `UnitMoverSystem`) into the prediction loop, or start with the own player interpolated (feels about one round trip late).
- **What a client sees:** done in O4 for enemies and companions (ghosts with bars, names and tints), projectiles and fields (`NetVfx` stand-ins), hit flashes, attack and death clips, statuses, combat text, one-shot effects and telegraphs. O5 step 5b added the own player's skill states, effects (both to the owner only), level and stats, and every player's gear (`EquippedVisual`, rebuilt on clients by `NetEquippedVisualsSystem`). A client keeps and saves the level and experience it earns in the host's game.
- **Loadout:** done (O5 step 5a). The client sends its class, resource, level, skills, passives and equipped items (`NetLoadout`), again on every change and for every new player ghost; the host applies them with the managers' own code.
- **Scene changes:** work. The host keeps its connections through `GameSceneLoader` (`NetSceneSync`) and clients follow.
- **Level generation and spawners on clients:** work. Clients build the level from the seed; enemy spawners only run on the host and trigger on any player.
- **Bandwidth:** measured with Netcode's `GhostMetricsMonitor` on a client: about 0.5 KB/s idle in the Hub, 1.2 KB/s idle in a dungeon, and 9 to 17 KB/s per client in a fight with 46 enemies (largest snapshot 1.3 KB, at the spawn burst).
- **Performance:** about 40 fps in the editor with both worlds and 40 Skeletons, Burst partly disabled in the editor; not representative. With two editors in the background the host logs "server tick batching": check in a build.
- **Run In Background** must be on in builds, or a host that loses focus stalls and drops its clients (Netcode warns about this).
- **Experimental API:** the single-world host is experimental in 6.6. Keep the binary host as the fallback in mind.

### How to try it

On `spike/netcode`, `Assets/Resources/NetSpike.txt` turns the networked bootstrap on (delete it for the normal local world). Play in the Hub: the editor is the host. A client: a Multiplayer Play Mode virtual player (`-name Player2`), a build with `-netclient`, or `ClientServerBootstrap.CreateClientWorld("Client")` from the console, with the subscenes loaded into it.
