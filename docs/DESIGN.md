# SqlPilot design language — Purple Frost

Shared implementation: src/Shared/Design.cs, linked into the installer and used by SSMS dialogs.

- English UI, Segoe UI 13; headings 25 semibold; supporting text 11–13.
- Ink #2F1F43; secondary text #745D8E; action purple #773FC9; gradient #EBDDFF → #F8F2FF.
- Purple Frosted cards: translucent white, 16 px radius, white edge, soft purple shadow; readable opaque input fields. Local gradient/alpha rendering works without enabling OS transparency.
- Window inset 24 px; card inset 20 px; sections 12–18 px apart; rounded buttons 9 px radius and at least 36 px high.
- One main SqlPilot toolbar button, grouped menu actions. Primary action at lower right; explicit completion/success/failure feedback.
- Resizable shortcut editor with a searchable list, key field and expanding SQL editor; keyboard gestures in a separate editable table.
- Installer offers checked host selection, progress, expandable logs and a final completion summary. A partial failure never claims full success. Selection survives UAC relaunch.
- Standard SSMS menu and editor controls keep native accessibility and keyboard behavior. Completion uses compact contextual rows and its purple light/dark palette, with red errors and amber warnings.
