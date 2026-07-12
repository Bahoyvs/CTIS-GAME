# Combat Logging System - Implementation Guide

## Overview

The Combat Logging System provides real-time, network-aware logging of all gameplay actions including abilities, effects, damage, and movement. It's designed as a centralized service with a structured logging pipeline that feeds both the Unity console and an on-screen debug display.

## Architecture

### Core Components

#### 1. **CombatLogManager** (`Assets/_Project/Scripts/Core/CombatLogManager.cs`)
Singleton that centralizes all combat action logging.

**Key Methods:**
- `LogAction()` - Server-authoritative actions (abilities, attacks, effects)
- `LogLocal()` - Owner-local actions (rolls, input feedback, predictions)
- `LogEffect()` - Status effect applications
- `LogAbilityActivated()` - Ability activation with mode
- `LogAbilityCooldown()` - Cooldown start with duration
- `LogAbilityChannelStart()` - Channel start with duration
- `LogAbilityChannelEnd()` - Channel completion (completed/interrupted)
- `LogAbilityToggle()` - Toggle ability ON/OFF
- `LogAbilityBlocked()` - Ability gating failure (silenced, cooldown, etc.)

**Flow:**
1. Server logs action locally
2. Server broadcasts via `BroadcastLogClientRpc()`
3. All clients receive identical log entry
4. `OnEntryLogged` event fires with formatted message

**Output Format:**
```
[Server] Player_2 (Kerem) activated Skill_1 (Instant) at X:10.0, Y:5.0
[Client] Player_1 (Bahadir) started cooldown on Ultimate (8.5s) at X:0.0, Y:0.0
[Server] Player_3 (Hero) channel completed Ability_Name at X:15.2, Y:-3.5
```

---

#### 2. **AbilityController** (`Assets/_Project/Scripts/Core/Abilities/AbilityController.cs`)
Pipeline that orchestrates ability activation and now includes comprehensive logging.

**Integration Points:**

**ServerTryActivate()** - Logs at each gating checkpoint:
- ? Successful activation (with ability mode)
- ? Silenced block
- ? Cooldown block
- ? Already channeling block
- ? CanActivate() veto
- ? Toggle ON
- ? Toggle OFF (logged separately)
- ? Channel START

**ServerEndChannel()** - Logs channel termination:
- ? Channel completed
- ? Channel interrupted

---

#### 3. **CombatLogDisplay** (`Assets/_Project/Scripts/UI/CombatLogDisplay.cs`)
On-screen debug console that renders combat logs in real-time.

**Features:**
- Real-time scrolling text display (TextMeshPro)
- Configurable max line history (default 20)
- Optional time-based fade-out (lines disappear after N seconds)
- Optional timestamp prefix (HH:MM format)
- Auto-scrolls to latest entries

**Setup:**
1. Create a Canvas in your scene
2. Add a TextMeshProUGUI element as a child
3. Add this component to the TextMeshProUGUI object
4. Configure max lines and fade time in inspector

---

## Logging Flow Diagram

```
Owner Input (e.g., Skill1)
    ?
HeroController.OnSkill1Performed()
    ?
AbilityController.TryActivate(slot, aimPoint)
    ?
RequestActivateRpc(slot, aimPoint) [Client ? Server]
    ?
ServerTryActivate(slot, aimPoint) [Server]
    ?? Gate Check 1: Silenced? ? LogAbilityBlocked("Silenced")
    ?? Gate Check 2: Cooldown? ? LogAbilityBlocked("Cooldown Active")
    ?? Gate Check 3: Channeling? ? LogAbilityBlocked("Already Channeling")
    ?? Gate Check 4: CanActivate()? ? LogAbilityBlocked("Cannot Activate")
    ?
    ?? ? All gates pass
        ?? AbilityRuntime.Execute()
        ?? LogAbilityActivated() [Instant/ChargeBased]
        ?? LogAbilityToggle(true) [Toggle ON]
        ?? LogAbilityChannelStart() [Channel]
             ? (channel runs for duration)
             ?
        ServerEndChannel(slot, completed)
             ?? LogAbilityChannelEnd(completed=true/false)
             ?? Cooldown committed

BroadcastLogClientRpc() ? Server sends log to all clients
    ?
OnEntryLogged?.Invoke(prefixed_message) [All peers]
    ?
CombatLogDisplay.AddLogEntry(message)
    ?
TextMeshProUGUI displays "[Server] Player_X ..."
```

---

## Usage Examples

### Example 1: Basic Ability Cast

```csharp
// Automatically logged by AbilityController
_abilities.ServerTryActivate(AbilitySlot.Skill1, aimPoint);

// Logs:
// "[Server] Player_2 (Kerem) activated Fire_Blast (Instant) at X:10.0, Y:5.0"
```

### Example 2: Failed Cast (Silenced)

```csharp
// Player tries to cast while silenced
_abilities.ServerTryActivate(AbilitySlot.Ultimate, aimPoint);

// Logs:
// "[Server] Player_1 (Bahadir) failed to use Ultimate (Silenced)"
```

### Example 3: Channel Ability

```csharp
// Channel starts
_abilities.ServerTryActivate(AbilitySlot.Skill2, aimPoint);
// "[Server] Player_3 (Hero) started channeling Earthquake (2.5s) at X:0.0, Y:0.0"

// ... 2.5 seconds pass ...

// Channel completes
_abilities.ServerEndChannel(AbilitySlot.Skill2, completed: true);
// "[Server] Player_3 (Hero) channel completed Earthquake at X:0.0, Y:0.0"
```

### Example 4: Toggle Ability

```csharp
// Toggle ON
_abilities.ServerTryActivate(AbilitySlot.Passive, transform.position);
// "[Server] Player_2 (Kerem) toggled ON Aura_Buff at X:5.0, Y:2.0"

// Later, toggle OFF
_abilities.ServerTryActivate(AbilitySlot.Passive, transform.position);
// "[Server] Player_2 (Kerem) toggled OFF Aura_Buff at X:5.0, Y:2.0"
```

### Example 5: Manual Logging (Non-Ability Actions)

```csharp
// In a damage script (server-side)
CombatLogManager.LogAction(
    attacker.DisplayName,
    "hit",
    $"{damage:F0} damage",
    victim.transform.position
);
// "[Server] Player_1 (Bahadir) hit 45.0 damage at X:8.5, Y:3.2"

// In a hero script (owner-local prediction)
CombatLogManager.LogLocal(
    DisplayName,
    "used",
    "Roll",
    transform.position
);
// "[Client] Player_2 (Kerem) used Roll at X:10.0, Y:5.0"
```

---

## Console Output Configuration

### Mirror to Unity Console
By default, logs are mirrored to the Unity Editor Console for debugging.

```csharp
// In CombatLogManager inspector
mirrorToUnityConsole = true; // Enable/disable Unity console output
```

### On-Screen Display

Create a debug panel:
1. In your Canvas, create a Panel child
2. Add TextMeshProUGUI to the panel
3. Add `CombatLogDisplay` component
4. Configure:
   - **Max Lines**: 20 (scroll buffer size)
   - **Line Fade Time**: 10.0 (seconds before fade)
   - **Show Timestamp**: true (prepend time)

---

## Message Format Reference

| Event | Format |
|-------|--------|
| Ability Activated | `[Server] Player_X activated AbilityName (Mode) at X:?, Y:?` |
| Cooldown Start | `[Server] Player_X started cooldown on AbilityName (Xs) at X:0.0, Y:0.0` |
| Channel Start | `[Server] Player_X started channeling AbilityName (Xs) at X:?, Y:?` |
| Channel Complete | `[Server] Player_X channel completed AbilityName at X:?, Y:?` |
| Channel Interrupt | `[Server] Player_X channel interrupted AbilityName at X:?, Y:?` |
| Toggle ON | `[Server] Player_X toggled ON AbilityName at X:?, Y:?` |
| Toggle OFF | `[Server] Player_X toggled OFF AbilityName at X:?, Y:?` |
| Blocked (Generic) | `[Server] Player_X failed to use AbilityName (Reason)` |

---

## Troubleshooting

### Logs not appearing on screen
1. ? Verify CombatLogDisplay is on a TextMeshProUGUI component
2. ? Verify Canvas is visible in hierarchy and active
3. ? Check that `CombatLogManager.OnEntryLogged` is subscribed
4. ? Verify abilitiy activation is happening server-side

### Logs not reaching clients
1. ? Verify NetworkObject is on CombatLogManager prefab
2. ? Verify scene is networked (not offline/Offline scene)
3. ? Check that server RPC is firing (add Debug.Log in BroadcastLogClientRpc)

### Ability logs missing certain events
1. ? Verify ability is reaching ServerTryActivate (check for early returns)
2. ? Check that AbilityDataSO.displayName is not empty
3. ? Verify BaseHero component exists on the entity

---

## Performance Considerations

- **FixedString128Bytes**: RPC payloads use FixedString to prevent GC allocation on every log
- **Rate Limiting**: Optional - could add throttling if log spam becomes an issue
- **UI Rendering**: Fade calculation happens every frame (minor overhead)
- **History Buffer**: Max 20 lines by default (memory efficient)

---

## Future Enhancements

1. **Log Filtering** - Show only abilities, only failures, etc.
2. **Damage Numbers** - Integrate with damage log system
3. **Stat Changes** - Log buff/debuff application
4. **Projectile Tracking** - Log projectile spawn/impact
5. **Network Analytics** - Aggregate logs for post-match analysis
6. **Log Export** - Save combat log to file for debugging

---

## Files Modified

- ? `CombatLogManager.cs` - Added ability-specific logging methods
- ? `AbilityController.cs` - Integrated logging at all key decision points
- ? `CombatLogDisplay.cs` - New UI component for on-screen rendering

## Testing Checklist

- [ ] Abilities log on activation
- [ ] Failed casts log the reason (silenced, cooldown, etc.)
- [ ] Channels log start and completion/interrupt
- [ ] Toggles log ON and OFF state
- [ ] Logs appear on all peers identically
- [ ] On-screen display scrolls properly
- [ ] Timestamps are formatted correctly
- [ ] Fade-out works smoothly
- [ ] Unity console receives logs when enabled
- [ ] No GC allocations from string building
