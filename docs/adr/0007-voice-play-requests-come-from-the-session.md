# Voice-play requests come from the overlay session

Who may speak is a session rule (the voice-primary pair plus the extra-path exceptions in ADR 0002). The window used to hard-code `pairIndex != 0` next to NAudio, which hid the rule from session tests and invited a second copy of designation. The live overlay session emits a request containing the source, text/key, and enough identity to coalesce duplicates; `MainWindow` only applies the user playback toggle, resolves the audio file, and invokes NAudio.

Playback is an acknowledgement boundary, not a fire-and-forget event. The window must report whether a request was started, skipped, or failed, and must report completion for a started request. The session may use that state to prevent overlapping or duplicate playback, but a skipped or failed request must always clear any pending/active state. Session tests cover authorization and request coalescing; window/integration tests cover the playback acknowledgement and no-op paths.
