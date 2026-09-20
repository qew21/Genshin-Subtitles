# Overlay is click-through except during region adjust

The overlay sits on the game. Always-draggable subtitles steal clicks meant for in-game controls. Region adjust is armed from settings for one target at a time: a region pair (both of that pair's outlined rectangles take the mouse—grabbing a box drags that box's own rectangle, capture or display; arming requires either rectangle to be valid), or the optional dark-screen display, or the optional dialogue-option display. Extra-path adjust only arms when that display already exists; its detected band is not a user-editable capture rectangle. Only valid rectangles are drawn as adjust targets.

While armed, pair sampling does not pause: dragging the capture means the next sampling tick uses the new rectangle. Escape, explicit cancel, target deletion, window deactivation, and any failed arm operation must restore click-through and clear the armed target. Otherwise the overlay—subtitles, hints, and the region-pair preview—remains click-through.

This ADR defines the baseline interaction contract for the region-pair overlay. Later polish may refine hit-testing or diagnostics, but it must preserve the one-target-at-a-time and click-through guarantees and be recorded in the ADR revision that introduces it.
