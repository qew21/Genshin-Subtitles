# Follow-tail is a sticky pin disarmed only by the operator leaving the bottom

The activity log viewport stays pinned to the newest visible content only while it is already at the bottom, including after 有新记录. WPF `ScrollChanged` reports layout, pixel virtualization, and wrapped result rows as offset and extent changes that look like the viewport left the tail. That made 有新记录 appear while following, and made the button return after a jump until the operator scrolled up and back down. Decision: follow is a sticky flag on `ActivityLogFollowTail`. Visible rows after log de-noise are the only “new content”; 有新记录 jumps and re-arms; only the operator moving the viewport (wheel, scrollbar, keys) updates the flag. Extent growth is not visible content and does not show the button.

**Considered Options**: treat `ExtentHeightChange` as new records (the previous window code; layout and remeasure false-positive the button); sample at-bottom on every `ScrollChanged` (virtualization undershoot disarms follow after `ScrollToEnd`).

**Consequences**: hidden repeat rows never announce visible content, so they neither scroll the list nor show 有新记录; clicking 有新记录 keeps follow armed across later layout events; tests cover the policy without hosting the window.
