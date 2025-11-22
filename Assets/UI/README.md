# Zarus UI Toolkit System

This directory contains all UI-related assets for the Zarus strategy game, built using Unity's UI Toolkit.

## Architecture Overview

**Strategy Game Focus:**
- Non-diegetic UI: Menus, settings, HUD overlays
- Diegetic UI: Province stat cards, in-world information displays
- PC Platform: Windows, WebGL, macOS, Linux

## Directory Structure

```
Assets/UI/
├── Styles/              # USS theme files (artist-friendly)
├── Layouts/             # UXML layout files
│   ├── Screens/         # Full-screen UIs (menu, settings)
│   ├── Popups/          # Modal dialogs
│   └── Components/      # Reusable UI elements
├── Scripts/             # C# UI logic
│   ├── Core/            # UIManager, base classes
│   ├── Screens/         # Screen controllers
│   ├── Popups/          # Popup controllers
│   └── Components/      # Custom UI components
├── Settings/            # PanelSettings, theme assets
└── Icons/               # UI imagery (future)
```

## Theme System

The theme system uses **USS variables** (CSS-like) for easy customization by artists.

**Key Files:**
- `Styles/MainTheme.uss` - Base theme with color palette
- `Styles/Components.uss` - Reusable component styles
- `Styles/Animations.uss` - Transition effects

**Artist Workflow:**
1. Open USS files in text editor or UI Builder
2. Modify CSS variables (colors, padding, borders)
3. Save and enter Play Mode to see changes

## Core Systems

### UIManager
Central singleton managing all UI screens and popups.

**Responsibilities:**
- Screen stack management
- Pause menu toggling
- Input mode switching (gameplay ↔ UI)

### Screen System
Base class: `UIScreen.cs`

All screens inherit from UIScreen:
- `PauseMenu.cs`
- `GameHUD.cs`
- `ProvinceDetailScreen.cs` (future)

### Input Integration
Uses existing `InputSystem_Actions.inputactions`:
- "UI" action map for menu navigation
- "Player" action map disabled when UI is active

## Getting Started

### For Programmers:
1. Create new screen: Inherit from `UIScreen`
2. Design layout in UI Builder (`.uxml`)
3. Apply styles from `MainTheme.uss`
4. Register with UIManager

### For Artists:
1. Edit `MainTheme.uss` for global color changes
2. Modify component USS files for specific elements
3. No code changes needed!

## Best Practices

- **Responsive Design:** Use percentage-based layouts
- **Performance:** Minimize `display: flex` nesting
- **Consistency:** Reuse component styles
- **Accessibility:** Maintain color contrast ratios

## Technical Notes

- **Unity Version:** 6000.2.10f1
- **Render Pipeline:** URP
- **UI Toolkit Version:** Built-in with Unity 6

## References

- [Unity UI Toolkit Manual](https://docs.unity3d.com/Manual/UIElements.html)
- [Unity 6 UI Toolkit Updates](https://unity.com/blog/unity-6-ui-toolkit-updates)
- USS Syntax: Similar to CSS3
