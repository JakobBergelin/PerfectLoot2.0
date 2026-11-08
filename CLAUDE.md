# PerfectLoot project rules

## Plan before changing anything

Before creating, editing, moving or deleting any file, show a short plan: what will change, which files are affected, and any open questions. Wait for approval before making the changes.

- Reading files and investigating is fine without asking.
- For trivial one-line fixes, state what you will do and do it.

## Items

Any item rarity can be the best choice to equip. For some builds, these should be more interesting than Legendaries or Uniques.

- Normal: the player wants or needs the highest base affix for this slot.
- Magic: one of the rolls is interesting for the build.
- Rare: the player wants this specific item but doesn't want to maximize a single affix.

Balancing:

- Standard items in a slot are sidegrades, never strict upgrades. Each has positive outliers that fit its theme.
- Legendary and Unique items never share outliers with a standard item of the same slot. Their outliers could be tied to their legendary power's theme. Their generic rollable ranges are narrower or lower, because the power carries them.
- Legendary and Unique items never have affixes that enable or strengthen their own power (for example no Area of Effect on an item whose power casts Area skills, no Critical Chance on one that triggers on crits). Standard items are what enable legendary powers: design Standard items that enable them better instead.
- An affix is never both always-on (base affix or slot stat) and rollable.
- Player and enemy stats are placeholders, so don't balance items against them.

## Programming

- Use Unity ECS/DOTS as much as possible for gameplay code.
- Keep systems simple and easy to read at a glance, and don't add too many exceptions.

## Multiplayer

The end goal is a multiplayer and split-screen game, so make systems work for multiple players.
