# TRACEWORK Visual Language

> Status: static visual-language reconstruction pilot complete for Dashboard and CPU only. Expansion to the remaining Tracework pages is still pending.

## 1. Purpose and ownership boundary

This document defines the independent static visual language of TRACEWORK / HardwareVision. It borrows only high-level ideas—logic-led composition, editorial hierarchy, controlled asymmetry, in-world instrumentation, and contrast in information density. It does not reproduce or depend on any Arknights, Hypergryph, Rhodes Island, third-party logo, icon, font, illustration, texture, copy, or promotional composition. No external proprietary asset was downloaded or added.

The pilot changes presentation only. FLOW RELAY, SYSTEM REWIRE, the single PageHost and CurrentPage binding, page lifecycle, page ViewModel caching, hardware acquisition, Polling, sensor/GPU history, PresentMon, the recorder, session formats, settings persistence, and tray behavior remain authoritative and unchanged.

## 2. Problem statement and design principles

The former Dashboard and CPU Tracework layouts overused equal-width, fully bordered panels. Repetition flattened hierarchy, made every signal appear equally important, and left the dark/grey/violet palette without a distinct telemetry or state vocabulary.

The reconstructed language follows these rules:

- logic decides decoration and content decides composition;
- every page has one visual subject;
- a strict grid permits controlled asymmetry, never arbitrary offsets;
- whitespace is structural, not leftover space;
- color communicates identity, telemetry, or real state;
- open and soft fields are the default; raised panels require a boundary reason;
- real data and existing projections drive every visible instrument.

## 3. Editorial layout and logical grid

The page content grid uses a 12px column gap and a 4px base vertical rhythm. Normal module separation is 16px; major sections use 24px.

| Internal content width | Mode | Logical columns | Rule |
|---|---:|---:|---|
| `>= 1360` | Wide | 12 | Full controlled asymmetry |
| `960–1359` | Standard | 8 | Preserve primary/secondary proportion |
| `680–959` | Compact | 4 | Stack primary and auxiliary regions |
| `< 680` | Narrow | 1 | Single column; no horizontal overflow |

`TraceworkResponsiveGrid` performs deterministic Measure/Arrange from the explicit internal content width. It uses attached placement properties for each mode, contains no timer or animation, clamps spans to the active column count, and ignores collapsed children.

## 4. Semantic color system

### Neutral foundation

| Role | Color |
|---|---|
| Canvas | `#0B0D10` |
| Surface | `#12171C` |
| Raised | `#182027` |
| Soft | `#202932` |
| Divider | `#34414B` |
| Trace Grey | `#64717B` |
| Dust | `#939CA3` |
| Paper | `#E7E4DD` |
| Secondary text | `#B2BAC0` |

### Information and state

| Role | Color | Meaning |
|---|---|---|
| Identity / IonViolet | `#8B7CFF` | Brand identity, page index, primary structure, selection |
| Telemetry / SignalCyan | `#58C4D6` | Charts, live readings, sample paths and windows |
| Active / PhosphorMint | `#79D9B1` | Real active, connected, locked, committed or successful state only |
| Attention / WarmAmber | `#D6A75B` | Real warning, limit, degradation or pause only |
| Fault / AlertCoral | `#D46A6A` | Real failure, disconnection, hazard or abnormal threshold only |

Neutral surfaces should occupy roughly 75–85% of the first view. Identity is normally 8–10%, telemetry 5–8%, active 1–3%, and attention/fault approach zero unless real state requires them. Mint, amber, and coral are never ambient decoration. Each semantic color has a corresponding brush and a low-opacity soft brush.

`TraceworkTechnicalGridBrush` is a static 24px tiled DrawingBrush with opacity `0.03`. It is limited to large open instrument fields and has no timer or animation.

## 5. Typography hierarchy

| Style | Size | Use |
|---|---:|---|
| `TraceworkDisplayValueTextStyle` | 48 | The page's unique primary live value |
| `TraceworkPageIndexTextStyle` | 36 | Page/device index |
| `TraceworkSectionTitleTextStyle` | 16 | Major region title |
| `TraceworkBodyValueTextStyle` | 12 | Main values and parameters |
| `TraceworkAnnotationTextStyle` | 9 | Source, mode, unit, window and supporting annotation |

Annotation text does not carry critical interaction. Long device names retain trimming, ToolTip, and accessible naming.

## 6. Surface and container rules

Four surface levels are available: Canvas, Open field, Soft field, and Raised technical panel. Open fields carry the page subject or primary value; soft fields group auxiliary channels; raised panels are reserved for operations, independently bounded devices, scrolling tables, report summaries, or other complex regions with a genuine boundary requirement.

The existing `TraceworkPanel` and old style keys remain compatible. `TraceworkTechnicalPanelStyle` narrows their intended use instead of deleting them.

## 7. Shared visual primitives

- **InstrumentField** — one primary live value in a large open field, using a local axis/edge rather than a generic panel header.
- **DataRail** — NODE, PLATFORM, UPDATED, STATE, DEVICE, TOPOLOGY, CHANNELS, and WINDOW share a horizontal or vertical baseline; items do not become independent cards.
- **AnnotationRail** — a narrow non-interactive source/index/mode rail. Decorative parts are non-focusable and do not hit-test.
- **ChartField** — one large primary chart with current value and a shared AVG/MIN/MAX baseline; auxiliary channels use compact micro charts.
- **SignalMatrix** — shared headers, weak row separation, aligned current values, preserved scrolling and long-text tooltips; rows are not cards.
- **TechnicalPanel** — the backwards-compatible fully bounded container, used only where interaction, scrolling, or complex containment justifies it.

## 8. Dashboard pilot: SYSTEM STATE

Wide mode uses a 7/5 split: CPU is the single primary InstrumentField on the left; GPU telemetry, Memory/Disk technical modules, and open Network/System modules form the secondary right column. Standard compresses to 5/3. Compact and Narrow stack the primary and secondary regions. NODE, PLATFORM, UPDATED, and STATE use one wrapping DataRail below the main composition.

All existing semantic cards, metrics, visibility rules, hardware selectors, two-way selection, ToolTips, automation names, and data references remain. Classic Dashboard is byte-identical to the pilot baseline.

## 9. CPU pilot: PACKAGE TELEMETRY

Wide mode uses a 4/8 split: package identity, primary InstrumentField, DEVICE/TOPOLOGY/CHANNELS/WINDOW rail are on the left; `Charts[0]` is the primary ChartField on the right. Standard uses 3/5. Compact and Narrow stack these regions. `Charts[1]`, `[2]`, and `[3]` remain the same ViewModels and Values collections but render as compact auxiliary channels. CoreRows remains a bounded SignalMatrix.

`CpuName`, `CoreThreadSummary`, `SelectedChartWindowSeconds`, `MetricProjection`, `Metrics`, `Charts`, Values/ranges/statistics, and `CoreRows` bindings are preserved. No chart, history source, subscription, timer, Polling path, or ViewModel was added. Classic CPU is byte-identical to the pilot baseline.

## 10. Accessibility and WPF performance boundary

- Key body text targets 4.5:1 contrast where practical; deterministic relative-luminance tests cover core semantic pairs.
- Identity, telemetry, and real-state meaning do not depend on color alone.
- Device selectors and long identity text retain ToolTip and AutomationProperties.Name.
- Decorative rails and grid texture are non-interactive; annotation does not enter the primary interaction sequence.
- Reduced and Off motion profiles do not alter static layout.
- The pilot adds no DispatcherTimer, CompositionTarget rendering loop, blur, shader, VisualBrush page copy, screenshot, external bitmap, or third-party UI dependency.
- Responsive layout is a lightweight Panel and performs only deterministic Measure/Arrange.

## 11. Automated validation

Six dedicated suites add 115 cases covering semantic colors and contrast, visual primitives and compatibility, Dashboard composition/bindings, CPU composition/bindings, internal widths `1600`, `1366`, `1100`, `920`, and `679`, and architecture/Classic preservation. Clean Release and Debug application builds pass with zero warnings and zero errors. Two independent full Release processes both pass `1006 passed, 0 failed, 1006 total`. The final documentation-head CI is recorded in PR #7 and the final task report.

Manual visual acceptance, screenshot analysis, real-DPI validation, and formal administrator EXE validation were not performed.

## 12. Expansion order and final-head record

The reconstructed language has not yet been expanded to the remaining Tracework pages. Recommended order: GPU, Memory, Disk, Network, Motherboard, Advanced Sensors, Game, Report, Settings, then Metric Visibility—reusing the semantic system while giving every page its own subject rather than copying either pilot.

The exact final full commit SHA cannot be self-recorded inside that same commit. It is recorded in Draft PR #7 and the final task report after the documentation commit is created.
