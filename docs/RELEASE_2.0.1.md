# HardwareVision 2.0.1

HardwareVision 2.0.1 corrects INITIAL TRACE visibility and the first SYSTEM REWIRE transition without changing hardware collection, polling cadence, session schemas, PresentMon, navigation topology, or the published v2.0.0 release.

## Fixed

- INITIAL TRACE no longer spends its visual phases before the window is visible; its clock begins after ContentRendered and real visual readiness.
- A static Tracework first-frame cover prevents the main interface flashing before the startup presentation. Classic and Motion Off remain uncovered.
- Startup Commit waits for the existing first/newer Polling version to be Dispatcher-applied to all six visible Dashboard regions and followed by post-data layout. Pending/blank states cannot Commit; timeouts become explicit.
- Startup background, content and bottom rail now leave concurrently with Shell reveal, so Shell motion is visible.
- Index, Route, Bind and Lock remain bounded one-shot phases with a six-row stagger, one Full Bind pulse and a CanCommit-only lock flash.
- SYSTEM REWIRE prewarms its template and replays a saved cold-template Trace once. The first and subsequent Full/Standard transitions now use the same real node translations; Reduced has no translation and Off has no clocks.
- Startup suppresses theme-transition visuals until Complete; later user transitions retain full behavior.

## Performance and compatibility

- No additional polling, sensor scan, hardware scan, Window, Shell, PageHost, synchronous UI I/O, Advanced Sensors wait, PresentMon wait, or history accumulation was added.
- Version is `2.0.1`; assembly/file version is `2.0.1.0`; session schema versions are unchanged.
- Automated result: `1432 passed, 0 failed, 1432 total`; focused cold-template and startup readiness repeats are each `20 / 20`.
- Manual administrator EXE visual validation, screenshots, real DPI/high-contrast/remote-desktop testing, real-device sensor validation and real PresentMon validation were not performed.

## Distribution

The stable release contains exactly one asset, `HardwareVision.exe`: Windows x64, .NET 8 WPF, framework-dependent, single-file, untrimmed. Signing status and SHA-256 are recorded from the release workflow artifact and public download verification.
