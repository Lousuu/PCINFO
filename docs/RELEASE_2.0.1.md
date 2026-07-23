# HardwareVision 2.0.1

HardwareVision 2.0.1 completes the INITIAL TRACE cold-start correction without changing hardware collection, polling cadence, session schemas, PresentMon, navigation topology, Advanced Sensors, SYSTEM REWIRE, FLOW RELAY, or the published v2.0.0 release.

## Fixed

- The first native Window surface is `#0B0E11` before Show. A one-shot opacity gate releases on the first Render commit and has a strict 500 ms fail-open; DWM dark-title failure cannot keep the Window hidden.
- COMMIT authorization becomes monotonic at Lock. A newer transient Projection can update final values but cannot make COMMIT disappear; the lock and label remain synchronized and use a 90 ms exit that never relights hidden content.
- Reveal establishes an irreversible state immediately, atomically shows `05 / 05 REVEAL`, and overlaps the existing Shell behind the startup layer. Full/Standard/Reduced holds are 100/80/40 ms and all startup layers leave together in 90 ms, removing the former dark pause.
- `SYS/BOOT.00` now reveals continuously over one stable natural text width. Full/Standard use 180/120 ms and permit one Render layout retry before safe final-state fallback.
- Startup Commit waits for the existing first/newer Polling version to be Dispatcher-applied to all six visible Dashboard regions and followed by post-data layout. Pending/blank states cannot Commit; timeouts become explicit.
- Index, Route, Bind and Lock remain bounded one-shot phases with a six-row stagger, one Full Bind pulse and a CanCommit-only lock flash.
- SYSTEM REWIRE prewarms its template and replays a saved cold-template Trace once. The first and subsequent Full/Standard transitions now use the same real node translations; Reduced has no translation and Off has no clocks.
- Startup suppresses theme-transition visuals until Complete; later user transitions retain full behavior.

## Performance and compatibility

- No additional polling, sensor scan, hardware scan, Window, Shell, PageHost, synchronous UI I/O, Advanced Sensors wait, PresentMon wait, or history accumulation was added.
- Version is `2.0.1`; assembly/file version is `2.0.1.0`; session schema versions are unchanged.
- Final candidate automation contains `2057` tests. Native gate/fail-open, COMMIT authorization/visual/no-relight, Reveal atomic/overlap/late-snapshot, stable Index Clip and clock cleanup are each independent `20 / 20` groups; Advanced Sensors remains `15 / 15` and SYSTEM REWIRE remains `20 / 20`.
- Release acceptance requires two identical full Release runs with zero failures and empty stderr, zero-warning/error Release/Debug/test builds, Runtime XAML, dependency audits, PR #9 CI, merge-commit main CI and the existing package workflow.
- The formal administrator EXE is not launched. Codex does not inspect screenshots or recordings, and no post-fix manual recording acceptance is claimed.

## Distribution

The stable release contains exactly one asset, `HardwareVision.exe`: Windows x64, .NET 8 WPF, framework-dependent, single-file, untrimmed. The annotated `v2.0.1` tag targets the verified main merge commit. Signing status, byte size and SHA-256 are taken from the existing package workflow artifact and verified again against the public download. The v2.0.0 tag, commit, notes and sole asset remain unchanged.
