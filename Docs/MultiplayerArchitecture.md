# How multiplayer works

How the online game is put together, in big strokes, as of `net/foundation` on 2026-11-13. The plan and the decisions
behind it are in [MultiplayerPlan.md](MultiplayerPlan.md); this file is about how the code works now.

## The model in one paragraph

Every game runs through **Netcode for Entities**, even alone. The machine whose game it is is the **host**: one ECS
world (a *single-world host*) runs the whole simulation and its own players, exactly as the single-player game always
did. Friends **join** as **clients**: their world doesn't simulate anything. It shows what the host sends (ghosts and
messages), sends its players' input every tick, and predicts only its own players' walking. The host trusts what a
client says about itself (its loadout, its items); saves stay on each machine.

## Worlds and roles

| Piece | What it does |
|---|---|
| `NetBootstrap` (`Netcode/NetBootstrap.cs`) | Makes the game world on startup and on every scene change: a host world, or a client world, depending on `NetSession.Role`. |
| `NetSession` (same file) | The session as plain statics: `Role` (Host or Client), `Listen`, `ConnectTo`, `MaxPlayers` (4), `RemotePlayers`, the `Notice` shown to the players, and `Join(address)` / `Leave(notice)`. |
| `GameWorlds.Server` / `GameWorlds.Client` (`ExecutionGroups.cs`) | World filters. Gameplay groups (stats, skills, movement, damage, AI...) run only where the simulation is: `Server`. `FeedbackSystemGroup` (animation, VFX, bars) runs where things are drawn: `Client`. A host world is both. |
| `NetSession.IsHost` | The check for managed code (MonoBehaviours, `SystemBase`) that must behave differently in someone else's game. Burst code uses `state.WorldUnmanaged.IsServer()`. |

Rule of thumb: **a system that changes gameplay state belongs on the server**; on a client it would either do nothing
(the components it needs aren't there) or fight the snapshots. A Feedback system that reads a host-only component needs
another source on clients (see "What clients see").

Joining and leaving change the role and **start over in the Hub with a fresh world** (`GameSceneLoader.Restart`, run
on the next frame by `NetSceneSync`). Going to the main menu ends a client session (`NetSession.End`).

## Connecting and joining

1. `NetStartSystem`: a host listens on port 7979 (while `NetSession.Listen`); a client connects to `NetSession.ConnectTo`.
2. `NetJoinSystem`, client side: once its scene's subscenes have loaded, the client puts its connection in game and
   sends a `JoinRequest` (how many couch players it brings, which scene it is in). It does this again after every scene
   change.
3. `NetJoinSystem`, host side:
   - A client that is new to the game is turned down (`JoinRefused`) when the game would have more than 4 players, or when
     the host isn't in town (or is in a menu).
   - A client in another scene is sent the host's scene and level seed (`LoadSceneRequest`) and asks again from there.
   - Otherwise its connection goes in game and gets `JoinedPlayers`.
4. `NetPlayerSpawnSystem` (host) makes that client's player ghosts next to the host's player, with the same
   `PlayerEntitySetup` as the host's own players, owned by the client's connection (`GhostOwner`). It remakes them after
   they die, and removes them when the client's connection is gone.
5. On the client, `CharacterManager.TryAdopt` takes over the ghost it owns (`NetPlayer.LocalIndex` = which couch player)
   and gives it the local player's components (camera, input).
6. A client goes back to its own game, with a notice, when it is turned down, can't reach the host, loses the
   connection or the host leaves (`NetJoinSystem` reads Netcode's connection events and calls `NetSession.Leave`).

Couch players join only in a machine's own game (`CoopHUD`), within the 4-player limit. The co-op tether
(`CoopTetherSystem`) holds every living player, local or remote.

## Scene changes

`GameSceneLoader` replaces the ECS world on every scene change. In a networked game (`NetSceneSync`):

- The host picks the level seed, sends `LoadSceneRequest` (scene and seed) to every client, takes them out of the game,
  and loads a few frames later so the message goes out first.
- Before the old world is disposed, its connections move to a small backup world through Netcode's
  `DriverMigrationSystem`; `NetBootstrap` builds the new world around them. Connections survive, components included.
- Clients load the scene when the request arrives (they never change scenes on their own, except to the main menu),
  build the same level from the seed, and send a new `JoinRequest`. `NetJoinSystem` resets the host's record of their
  loaded ghost prefabs (`NetworkSnapshotAck.NumLoadedPrefabs`), so they get the new scene's prefab list before any ghosts.

## Players

| Concern | How |
|---|---|
| The prefab | `PL_Base_Visuals` is an owner-predicted ghost. `NetReplicatedAuthoring` (Player) bakes what clients must see; everything else is added at runtime on the host (`PlayerEntitySetup`) and never sent. |
| Input | The owning machine runs the normal `InputsDataSystem`; `NetInputSendSystem` packs `InputsData` into the `NetPlayerInput` command each tick; on the host `NetInputReceiveSystem` unpacks it before skills run, finding presses itself. |
| Walking | `PlayerMoveSystem` runs in Netcode's prediction loop: on the host for every player, and ahead of the host on the owner's machine, so walking feels instant. The host sends what it needs each snapshot (`PredictedMove`: speed, locks). Skills that move the player (Dash, Teleport...) aren't predicted. |
| Loadout | The client's own managers (character, skills, passives, inventory) don't push to its ghost; they mark the loadout dirty and `NetLoadoutSendSystem` sends it (class, resource, level, skills, passives, items as base id + seed) on every change and for every new ghost. The host's `NetLoadoutReceiveSystem` applies it with the managers' own code (`CharacterManager.ApplyCharacter`, `ActiveSkillManager.ApplySkills`, `PassiveSkillManager.ApplyPassives`, `InventoryManager.ApplyEquipment`), rolling items again from their seeds. |
| What the owner's HUD reads | Replicated from the host: `Skill` (type, slot and state: cooldowns, usable), `EffectDisplay` (to the owner only), `Level`, `CalculatedStats`, `EquippedVisual` (to everyone). The HUDs and managers read them as on the host. |
| What others see it do | `NetPlayerActions`: dashing / spinning / winding states, counters for Fireball releases, Barrage shots, Tornado sweeps and punches, and the Quarry target. Filled by the host; animation (`RigidAnimationSystem`) and VFX read it on every machine, the host included, so there is one path. |
| Progress | XP and levels are earned on the host and arrive through `Level`; the client's `CharacterManager` keeps and saves them in its own account. |

## What clients see of everything else

| Thing | How it reaches a client |
|---|---|
| Enemies, companions, thralls | Interpolated ghosts (their prefabs carry `GhostAuthoringComponent` + `NetReplicatedAuthoring`): transform, `Health`. `NetUnitSource` says which `EnemyDefinition` or skill made a unit, so clients add its name, info and tint (`NetUnitSourceSystem`). |
| Hits, attacks, deaths, statuses | `NetUnitFeedback` on every unit ghost: a hit counter and damage type, an attack counter, Dying, status bits (burning, poisoned, chilled, bleeding, frozen, charged). `NetUnitFeedbackApplySystem` turns them into the hit flash, clips, death and status components on clients. |
| Projectiles, fields, souls, Mirror Image copies | Runtime entities that only the host has. `VfxSystem` on the host makes a `NetVfx` stand-in ghost for each (look, size, tint), moves it along and destroys it with its entity; clients play the same effect on the stand-in. |
| Combat numbers, one-shot effects, chain lightning | Events, not state: `NetFeedbackSendSystem` sends `NetFeedbackRpc`s (at most 24 a frame); `NetFeedbackReceiveSystem` raises the same events the host's damage systems raise, for `CombatTextHUD` and `VfxSystem`. |
| Telegraphs | The marker is a baked ghost prefab (`NetPrefabs.Telegraph`) with a replicated `TelegraphParams` (shape, size, fill). |
| Loot | Personal: the host sends each client every dungeon drop (`NetLootDropRpc`: which table, where); each machine rolls its own items from the same table and only its players see and pick them up. |
| The level | Built locally from the seed. Enemy spawners run only on the host and trigger on any player. |

Bandwidth measured in the editor: about 0.5 KB/s per client idle in the Hub, 1.2 KB/s idle in a dungeon, 9 to 17 KB/s
in a fight with 46 enemies.

## Platform (Steam)

Gameplay never calls Steam directly. `Platform.Current` (`Scripts/Platform/IPlatform.cs`) is an `IPlatform`: who the
player is, Rich Presence, which friends are playing, and how to reach their games. It is made on first use, which can be
while the first world makes its network drivers, so a host listens on Steam from the start.

Two versions:

- **`LocalPlatform`**: no platform. Games join each other by IP address (the game menu's Join a game). It keeps the
  presence it is given (for tests) and knows no friends.
- **`SteamPlatform`**: Steamworks.NET. Persona name, Rich Presence, friends playing this game, joining them. Used when
  Steam runs; a run started with `-nosteam`, or a Multiplayer Play Mode virtual player, uses Local.

`PlatformManager` (on the GameServices prefab) publishes this game's presence every second (only changes) and keeps
the list of friends whose games can be joined:

| Key | Value |
|---|---|
| `pl_area` | `town`, `dungeon` or `menu` |
| `pl_players` | Players in the game, couch players included |
| `pl_host` | The SteamID of the game's host (this machine, or the host a client joined through Steam) |
| `connect` | `+connect_host <SteamID>`: what Steam's Join Game and invites hand to the friend's game |
| `status` | "In town (2/4)" (Steam shows it once the game has its own App ID) |

### Joining through Steam

- **Transport**: `SteamNetworkInterface` (own assembly `PerfectLoot.SteamTransport`, which allows unsafe code) carries
  Unity Transport's packets as Steam's connectionless messages (`SteamNetworkingMessages`), relayed by Steam, so nobody
  needs an open port. A Steam user is a custom endpoint holding their SteamID.
- **Drivers** (`NetDriverConstructor`, set by `NetBootstrap`): a host gets UDP (while `NetSession.Listen`, i.e. editor
  and development builds, or without Steam) plus Steam, and listens on both. A client gets Steam when it joins a SteamID,
  UDP otherwise.
- **No lobbies**: a game is joined by its host's SteamID, published as `pl_host`. Friends of a client join the same host.
- **The Hub**: a "Join <name> (n/4)" button, bottom-left, for each friend whose game is in town and not full.
- **Steam's Join Game and invites** arrive as `GameRichPresenceJoinRequested_t` with the `connect` string; the game joins
  from town in its own game, and otherwise waits with a notice ("Go back to town...", "Pick a character...").
- The host's join rules (4 players, town only) and everything after joining work as for any client.

### Setup

- Steamworks.NET `2025.165.0` from git (`Packages/manifest.json`). Its editor scripts add the `STEAMWORKS_NET`
  scripting define, which the Steam code checks; without the package the game runs on Local.
- `steam_appid.txt` in the project folder contains `480` (Spacewar, owned by every Steam account), so the editor starts
  the Steam API as that app. A build needs it next to the exe (or our own App ID later). While playing, Steam shows the
  player as playing Spacewar.

### Tested on one PC (2026-11-13)

- The game picks Steam (App 480), reads the persona name, and Steam holds the published presence, `pl_host` and
  `connect` included. Presence stays empty until the scene's area is known.
- The host has two drivers listening, UDP and Steam; Player 2 still joins over UDP.
- The Hub's join button shows for a friend (inserted by hand). Pressing it starts over as a client with one Steam driver
  connecting to that SteamID; an unreachable one gives up after about a minute with "Couldn't reach a game at that
  address." and goes back to hosting.

### Test with a friend (two PCs, two Steam accounts)

1. Both: Steam running and logged in, each other as friends, the same build or project version. In the editor
   `steam_appid.txt` is already there; for a build, put a `steam_appid.txt` containing `480` next to the exe. Builds keep
   running in the background (Player Settings); in the editor keep the game window in front, or an unfocused host can
   stall and drop the friend.
2. Host: play into the Hub with a character. Check Steam: the friend should see you "playing Spacewar".
3. Friend: play into their own Hub. Within a few seconds "Join <host> (1/4)" should show bottom-left. Click it.
4. Expect "Joining <host>...", then the friend's game starts over in the host's Hub with their own character, next to
   the host. Walk, fight, take a portal together, pick up loot (each sees their own).
5. Also try: Steam friends list > right-click the host > Join Game (or the host invites the friend); a join while the
   host is in a dungeon (should be turned down); the host going to the main menu (the friend gets "The host left").
6. If the join button never shows: in the editor, right-click PlatformManager on GameServices (Play mode) >
   "Log presence and friends playing" on both sides, and send the logs (`Logs/Editor.log`, or the build's `Player.log`).

## Adding to the game

- **A new gameplay component**: add it on the host as usual. Clients never see it unless it is baked on a ghost prefab
  and has `[GhostField]`s.
- **Clients need to show something new**: pick the cheapest of
  1. a field on an existing replicated component (`NetPlayerActions` for player actions, `NetUnitFeedback` for unit reactions),
  2. a stand-in ghost (`NetVfx`) for an effect that follows a runtime entity,
  3. an RPC for a one-off event (`NetFeedbackSendSystem.Burst`).
- **A Feedback system that reads host-only components** either reads a replicated source on every machine (preferred:
  one path, as with `NetPlayerActions`), or runs only on the host (`[WorldSystemFilter(GameWorlds.Server)]`, as
  `EffectDisplaySystem` and `TelegraphMarkerSystem` do).
- **A manager that pushes to the player entity** must not do it in someone else's game (`NetSession.IsHost`); send it
  through the loadout (`NetLoadoutSendSystem.MarkDirty`) instead.
- **Something that changes the scene** goes through `GameSceneLoader.Load`, which takes the clients along.

## Testing

Multiplayer Play Mode's virtual player "Player 2" joins the editor automatically as a client (`-name Player 2`). Known
quirks: the first Play after a prefab or code change, or the first visit to a scene, can fail on Player 2 with a
subscene import error ("entity header couldn't be resolved", "Cannot find TypeIndex"); stopping and starting Play fixes
it. Two hosts on one machine both try to listen on port 7979 (a harmless "address in use" error on the second). Another
machine on the network joins through the game menu's **Join a game** with the host's IP address.

## Known gaps

- Couch players can't join a client in the middle of someone else's game; they join before joining.
- With the host alone and three remote players, the fourth player takes flow field slot 1, which is also player 2's
  `PlayerIndex` on the host.
- The host logs "server tick batching" with two editors running in the background; check performance in a build.
- On Player 2, Netcode often logs `InvalidOperationException: Cannot find ghostTypeId in GhostPrefabCollection ...
  (GhostPrefabCollection.Length: 0)` from `GhostPredictionHistorySystem.PredictionBackupJob` right after joining or
  following the host into a scene. The game carries on (the player spawns, plays and gets its loadout), but a predicted
  ghost seems to exist in the new client world before its ghost prefab list has loaded. Not investigated yet.
- Not tested yet: a cooldown ticking down on a client after a real cast, Quarry marks and Charged puffs on clients, a
  real game over a network between two PCs. (A dungeon's completion drop works on the host: 3 items.)
- The single-world host is experimental in Netcode 6.6.
