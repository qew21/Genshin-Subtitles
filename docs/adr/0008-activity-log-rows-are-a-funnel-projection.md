# Activity-log rows are a second projection of the session funnel

Hints and activity-log rows come from the same operator-action write on the live overlay session. They are not a public log module, and they are not derived from `HintChanged` (that event also fires on expiry and clear). The session stays one module; the funnel lives in `LiveOverlaySession.OperatorActions.cs` so an implementer need not load OCR cadence, extra paths, or voice. A row is a snapshot: UTC time, job, global-or-pair ordinal, result resource key and format arguments. The window is an adapter.

**Considered Options**: split issue #18 into session-rows vs window tickets; extract a public `ActivityLog` module; append by subscribing to `HintChanged`; store already-localized display strings on the session.

**Consequences**: the hint write carries job and scope so a shared result key (未设置识别区) still distinguishes 作业. Settings-only boxing stays out of this funnel until a later ticket. Empty-state copy is window-only; the session list may be empty.
