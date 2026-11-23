# Native Port 0.0.8 — measurement instrumentation

## Why
The user reports roughly 1,000 ms of perceived FreeShow lyric cue delay on a Dell OptiPlex 7490. It is not established whether this comes from FreeShow state propagation, text layout, GPU composition, output routing, USB graphics/D3100, HDMI capture or intentional transitions. No other platform can guarantee a fix without measurement.

## Implemented
- NativeCueDispatchMetrics stores a bounded in-memory sample window with p50, p95, p99 and max synchronous UI dispatch times.
- NativePersistentVideoOutput.ShowCue records only the synchronous duration of swapping prepared Avalonia content.
- Developer-only VisualProbe prints the rolling p95 with an explicit warning that this **is not physical screen latency**.
- Synthetic checks for bounded retention, percentiles and no-sample state are written but not run.

## Explicitly not measured
- User keypress/mouse event to dispatcher arrival
- Avalonia measure/arrange/paint, GPU texture upload or vertical synchronization
- VideoView/LibVLC overlay scheduling, video frame latency or source synchronization
- Dell D3100 DisplayLink pathway, monitor refresh, HDMI, FoMaKo M1 capture, projector
- Physical click-to-visible latency (the relevant church user experience)

## Beta 2 physical acceptance setup (not executed)
1. Use a copied FreeShow song with ordinary Korean/English lyrics and no intentional transitions.
2. Compare unmodified FreeShow and native port at identical 1080p output resolution/refresh rate, same Windows user session and the same display path.
3. Use synchronized high-frame-rate capture or another hardware timestamp method to measure operator event to actual output pixel change. Document measurement method and uncertainty.
4. Report medians, p95, p99 and max. Separate first cue, repeated cue, continuous video background and background changes.
5. Repeat with original Main/Online/Stage output routing and Dell D3100 present; compare native display versus USB graphics path.
6. Check not only speed but identical lyric text, position, line spacing, font, shadow, transition settings and background continuity. Any omissions fail parity.

### Acceptance targets
- Goal is below 50 ms physical click-to-visible for prepared zero-transition lyric changes.
- No guarantee of target achievement; no measured speedups claimed.
- Original animations/transitions must be kept when the user has enabled them, even if those animations take longer.
- Keep the same FreeShow operator UI/UX; do not substitute a developer benchmark window for the production application.

Status: code committed; compilation, execution, visual parity, and physical latency measurements are pending Beta 2.
