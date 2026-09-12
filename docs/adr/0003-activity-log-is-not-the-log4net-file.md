# Activity log is a session record, not the log4net file

The operator needs a complete view of what the live pipeline did. Tailing `%AppData%\GI-Subtitles\app.log` would mix that with cadence skips, pixel ratios, engine internals, and the updater. Those stay in log4net. The activity log is a separate in-memory record for this process only: closing its window does not stop it; leaving the app discards it; it is not written to a second file.
