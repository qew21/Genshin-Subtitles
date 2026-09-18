# Display region is boxed, not offset from capture

A display region is an independent rectangle. Prefilling it from the capture region plus `Pad` looked convenient, but parking that default on-screen (overflow, flip above the capture) is more machinery than a second rubber-band. The operator pulls two boxes—capture, then display. `Pad` and 「重设字幕位置」 no longer move the live overlay.

Migration from the legacy global layout is one-time and loss-aware. Existing pair data, extra-path display data, scan switches, and voice-primary selection are copied into the selected game's layout. Legacy `Region`, `Region2`, and `Pad` values are retained as migration data, but they must not be silently interpreted as a new display rectangle. If no valid independent display rectangle exists, the UI shows the pair as needing display setup and the user can re-box it. Applying a different game loads that game's stored layout rather than reusing the previous game's rectangles.

The migration must be covered by tests for an existing configuration, an already-migrated configuration, and a game switch. A failed or incomplete migration must leave the original configuration recoverable rather than deleting the only copy of the legacy values.
