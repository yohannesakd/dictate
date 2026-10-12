# Dictate design language

Dictate is a small utility for daily English dictation. Its window should feel as quiet as the original Mac app.

The original Mac layout and the request for simplicity guide this design.
The selected direction uses neutral surfaces, compact controls, and a restrained blue accent.

## Colors

| Token | Value | Use |
| --- | --- | --- |
| Canvas | `#F8F9FB` | Window background. |
| Surface | `#FFFFFF` | Controls and selected navigation. |
| Ink | `#20232B` | Main text and the recording indicator. |
| Muted | `#656B78` | Supporting text. |
| Line | `#E3E6EC` | Dividers and control borders. |
| Voice | `#4263EB` | Recording action, selection, and the weekly chart. |

Warnings use dark amber text on a pale amber surface. Warnings appear inside the app window.

## Type and space

Inter is bundled so the interface uses the same typeface on every platform.
Body text uses 13 pixels. Page titles use 22 pixels. Supporting text uses 12 pixels.
Use regular, medium, and semibold weights. Keep text left aligned, except for chart labels and the recording control.

The normal desktop window is 480 pixels wide and 600 pixels tall. The minimum window is 400 by 480 pixels.
Content uses 16-pixel side margins at the default width. Use 12 to 16 pixels between related sections.
Use 4 to 8 pixels between related labels. Controls use 8-pixel corners. The recording surface uses 12-pixel corners.
Lists use dividers. Keep space proportional to the content.

## Layout

The compact layout keeps the original Mac app's header, weekly usage, and recent dictations.

```text
┌──────────────────────────────────┐
│ Native window controls           │
│ Wave icon  Dictate   Engine menu  │
│ Home       History     Settings  │
│                                  │
│ Ready to dictate                 │
│ Shortcut instruction   Record    │
│                                  │
│ This week              Word count│
│ Seven daily bars                 │
│                                  │
│ Recent dictations       View all │
│ Transcript                       │
│ Date, engine, copy actions        │
└──────────────────────────────────┘
```

A permanent sidebar would reduce the space for transcripts. A dashboard grid would give secondary information too much emphasis.

## Components and behavior

The waveform is the distinctive element. Keep other icons small and use consistent strokes.
The Mac menu bar uses five rounded waveform bars in an 18-point monochrome template.
Provide 18-pixel and 36-pixel assets for standard and Retina displays. macOS sets the foreground color.
Template and Retina assets follow [Electron's nativeImage conventions](https://www.electronjs.org/docs/latest/api/native-image#template-image-macos).
Windows and Linux use the same five-bar mark on the app's blue background in their system trays.
Use custom segmented navigation, switches, fields, and menus. Keep native window controls and system menus.
Use one primary action per section. Show supporting actions as quiet buttons.

The engine menu always offers Whistle and Whisper. Both run English recognition on the CPU.
Whisper is the default for new settings. Keep existing engine choices.
Show model download size and availability in Settings. Disable engine changes during dictation.

The floating indicator displays only nine waveform bars. Its visible surface is 76 by 24 pixels.
Audio levels control the bars during recording. The waveform pulses during processing and insertion.
The transparent window is 84 by 40 pixels. This size also fits Windows' native minimum height.
Keep phase labels and shortcut hints in the main app. The indicator disappears when dictation ends.
It must preserve focus in the destination field. It must never display notices or warnings.

History keeps the delivered text, original transcript, and delivery notes. Copy actions stay next to each transcript.
Settings group speech, cleanup, dictionary, permissions, and general options. Explain cloud cleanup where it is enabled.

## Platform consistency

All four apps package the same HTML, CSS, JavaScript, and fonts.
Android displays these assets in a local WebView. The native activity owns permissions, clipboard access, export, and credentials.
The Android voice keyboard uses the same recording control, engine menu, and styles.
Keep touch controls at least 44 pixels tall. Hide desktop window controls and use the available screen width.
Android uses the system keyboard picker instead of global desktop shortcuts.
Keep warnings in the main app. Keep the keyboard recording area free of warnings.
Preserve the Android input element during state updates so its native input connection remains valid.

## Accessibility and motion

Use visible focus rings, labeled controls, keyboard menus, and sufficient text contrast.
Follow reduced-motion preferences. Animate only recording levels and direct control changes.
Preserve input text, focus, and scroll position when backend state changes.

## Design review

This design removes the default Fluent controls that gave the Mac app a Windows appearance.
It retains the original app's compact hierarchy and quiet lists. The waveform provides identity without decorative cards or gradients.
