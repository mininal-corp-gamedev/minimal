# Minimal RP

Minimal RP works with a private serverside authority model. Gameplay decisions, validation, economy, persistence, spawning, damage, admin actions, and other authoritative mutations must live in `*.Server.cs` files or inside `#if SERVER` blocks. Shared code is reserved for UI, input collection, local prediction, visual/audio reactions, RPC signatures, synced state, DTOs, and definitions that clients need to render or request gameplay.

## Systems

- Player: spawn, death, respawn, money, ATM, dice, arrest, prop protection, saves, inventory sync, job clothing, achievements and stats.
- Admin: rank and ban persistence, moderator commands, player teleport, money/health/job changes, kick, ban, spawn and door admin actions.
- Jobs: job definitions, salary, job assignment RPCs, and private server job handlers for armor, inventory and spawn effects.
- Inventory: synced snapshots, item pickup, use, drop, save/load, job items, default items and hotbar/equip flow.
- Shop: shop definitions stay shared; purchases, limits, job checks and shop object spawning run through private server handlers.
- Economy: wallet/bank money, money drops, player transfers, casino payouts, dice results and world rewards.
- Weapons: local viewmodels and effects stay shared; damage, ammo mutation, special weapon authority, toolgun and physgun actions are server-controlled.
- Building/Props/Toolgun: prop spawning, ownership, favorites, undo, toolgun edits, fading doors, text screens, colors and no-collide rules.
- Doors: buy/sell, lock/unlock, roommates, job-only access, break, lockpick and paired-door state.
- Casino: roulette, slots, safe crack, risk ladder and dice requests use host validation and server-side money mutation.
- Grower: weed, fertilizer, cactus and NPC buyers validate harvest/sell rewards on the server.
- Money Printers: printer ownership, upgrades, damage, cash generation and collection are server authority.
- World Interactables: ATM, dropped money, box work, hobo collector, textscreen and fading door mutations.
- Triggers/Zones: safezone, casino and building trigger state is assigned by the server.
- Elevator: floor requests, queues, movement state, doors and passenger carry are host-authoritative.
- Vote/Demote: vote creation, vote submissions, job eligibility and demote results are server-controlled.
- Chat/UI: UI panels stay client-facing; chat commands and server-side effects are validated privately.
- Persistence: player, inventory, admin, ban, prop favorites and encrypted data storage use server-only `FileSystem.Data` access.
