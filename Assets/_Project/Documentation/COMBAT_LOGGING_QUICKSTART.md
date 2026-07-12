# Combat Logging System - Quick Setup

## 3-Minute Setup

### Step 1: Ensure CombatLogManager is in your scene
- Add a GameObject named `_CombatLogManager` to your networked scene
- Add the `CombatLogManager` component
- Add a `NetworkObject` component
- **Important**: Make it persistent (don't destroy on load) or add to a manager object

### Step 2: Create On-Screen Debug Console (Optional but Recommended)

#### Setup Canvas & TextMeshPro:
```
Canvas
??? DebugPanel (Panel component)
?   ??? LogDisplay (TextMeshProUGUI)
?       ??? [Add CombatLogDisplay component here]
```

#### Inspector Configuration:
1. Select `LogDisplay` (TextMeshProUGUI object)
2. Add Component ? `CombatLogDisplay`
3. Drag the TextMeshProUGUI object into the `Log Text` field
4. Configure:
   - **Max Lines**: 20
   - **Line Fade Time**: 10
   - **Show Timestamp**: true

#### Recommended TextMeshPro Settings:
- **Font Size**: 16-18
- **Line Spacing**: 0.8
- **Alignment**: Left + Top
- **RectTransform**: 
  - Width: 600px
  - Height: 400px
  - Anchor: Top-Left corner

### Step 3: That's it! ??

Abilities now automatically log:
- ? When they activate
- ? When they fail (silenced, cooldown, etc.)
- ? When channels start/complete
- ? When toggles turn on/off

---

## Testing It

1. Play the game
2. Cast an ability
3. Check:
   - ? Unity Console shows `[Server] Player_X activated ...`
   - ? On-screen display shows the same message
   - ? Try casting while silenced (should log failure reason)

---

## Advanced: Custom Logging

Add logs from your own code:

```csharp
using CBuilding.Core;

// Server-side authoritative action
CombatLogManager.LogAction(
    actor: "Player_1 (Hero)",
    verb: "used",
    detail: "Heal_Potion",
    worldPos: transform.position
);

// Client-local action (no network traffic)
CombatLogManager.LogLocal(
    actor: "Player_2 (Ally)",
    verb: "predicted",
    detail: "Projectile_Hit",
    worldPos: hitPoint
);

// Ability-specific
CombatLogManager.LogAbilityBlocked("Player_1", "Ultimate", "Not Enough Resources");
CombatLogManager.LogEffect("Player_3", "Stun", transform.position);
```

---

## Troubleshooting

**Q: Logs not appearing?**
- A: Check CombatLogManager is in the scene and has NetworkObject
- A: Check CombatLogDisplay is on the correct TextMeshPro object
- A: Try enabling `mirrorToUnityConsole` on CombatLogManager to see Unity console output

**Q: Logs appear on server but not clients?**
- A: This is expected if using offline/non-networked scenes
- A: In networked scenes, all peers should see identical logs

**Q: Want to disable on-screen display?**
- A: Just delete the CombatLogDisplay component (logs still go to Unity console)

---

## Performance Notes

- Log messages use fixed-size network strings (no GC overhead)
- Max 20 entries in display by default (configurable)
- Minimal CPU overhead from UI rendering
- Safe to run in production (enable/disable display as needed)

---

## File Locations

- Logic: `Assets/_Project/Scripts/Core/CombatLogManager.cs`
- Abilities: `Assets/_Project/Scripts/Core/Abilities/AbilityController.cs`
- UI Display: `Assets/_Project/Scripts/UI/CombatLogDisplay.cs`
- Full Docs: `Assets/_Project/Documentation/COMBAT_LOGGING_GUIDE.md`
