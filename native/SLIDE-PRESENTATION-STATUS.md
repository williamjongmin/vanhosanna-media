# Native Port 0.0.25 — prepared lyric application and source acknowledgment

## Implemented

- `FreeShowSlideOutputMutation.Prepare` ports the **data assignment subset** of `src/frontend/components/helpers/output.ts:setOutput("slide", ...)`: replace the old slide instead of merging reveal fields, carry the matching active project occurrence, remove resolved bindings, increment `refresh` for the same non-temporary slide/line, and retain all unrelated/unknown OutData. It only returns a clone. Malformed non-numeric refresh is refused rather than approximating JavaScript coercion.
- `FreeShowSlideAcknowledgement.Matches` replaces the 0.0.24 acknowledgment check. The old check skipped absent/null reveal properties, allowing a backend to retain an old `revealCount` or `itemClickReveal`. The new comparison requires the requested slide shape, including absence of old properties, with the original exceptions for removed `bindings` and source-added `projectIndex`/`attributionString`. JSON numeric values are compared semantically, not by their serialized spelling.
- `FreeShowPreparedSlideTransitionRuntime` is a concrete **single-command integration adapter**. It requires a separately implemented, complete `IFreeShowSlideTransitionSourceBackend` and an output-owned `IFreeShowPreparedSlideSurface`. Its sequence is: source before-actions, source setOutput lifecycle, capture actual source state/style, present the exact prepared Native control, source updateOut/effects, recapture complete state, confirm the selected control, return full acknowledgment to the existing coordinator. It rejects missing phases, changed target/style, out-of-order/replayed commands, lost visuals and partial failures. The coordinator retains responsibility for revisions and output quarantine.
- `NativeFreeShowPreparedSlideSurface` implements the visual part against `NativeFreeShowOutputSession.ShowPrewarmedReveal`. The usual UI-thread path swaps immediately without an additional dispatcher turn; calls arriving off-thread dispatch to Avalonia. No decoder, media path, seek, font layout or text-control creation is performed by this surface adapter.
- `IsPresentingPrewarmedReveal` follows the output session through its mounted video/fallback lyric host to the **exact cached Content control**. Clear, reprepare, another cue or replacement of the host/control invalidates this receipt. Selecting the same cached cue repairs an externally replaced Content rather than returning a false success.
- `NativeSlideTransitionVisualPreparation` exposes the exact source-batch styles used during prewarm. These are predictions for integration, not authoritative post-action snapshots.
- `FreeShow.Native.Checks/SlideTransitionRuntimeChecks.cs` adds synthetic regression cases for slide replacement/refresh/project data, stale reveal acknowledgments, full source/visual sequence, incomplete backends, missing visuals, changed source state/style, failed presentation, a cleared visual, source changes after effects, cancellation, phase order and replay. The existing check executable invokes them with its other checks.

## Integration boundary

The prepared runtime is **not a completed source action engine**. There is no production `IFreeShowSlideTransitionSourceBackend` implementation yet and none is registered in OperatorProbe. Tests use explicitly fake backends in the Checks assembly only.

For an audited source backend, construct one adapter per planned target after visual preparation:

```csharp
var runtime = new FreeShowPreparedSlideTransitionRuntime(
    target,
    visualPreparation.PreparedStyles[target.OutputId],
    sourceBackends[target.OutputId],
    new NativeFreeShowPreparedSlideSurface(sessions[target.OutputId]));
```

Pass those adapters to `DispatchSlideTransitionAsync`; it rechecks source revisions before execution. Prewarm/readiness and ordinary operator dispatch belong on the Avalonia UI thread. A source backend must serialize independent external changes with its command and capture the actual current output/style together. It must implement all original phase effects and other output layers; it cannot claim support just because the data assignment helper or prepared visual succeeds.

Remaining source work includes `checkActionTrigger`, before-action deferral/pending navigation, break recording, attribution/usage, category/group actions, foreground/media/audio behavior, the 50 ms slide-cache reset, `output_changed`, complete `updateOut`, actual timer scheduling, project/focus selection and cross-output effect ownership. Nonzero transitions, additional compositing, dynamic/scripture text and Stage/network output also remain outside the current prepared lyric renderer's supported subset.

## Validation and release state

- Reviewed against the repository's original `OutputHelper.ts:playSlide`, `output.ts:setOutput`, and `showActions.ts:updateOut` source.
- Source diff/whitespace review only. No .NET build, synthetic fixture execution, Avalonia GUI test, Windows run or hardware latency measurement was performed, preserving the recorded defer-builds/tests-until-Beta-2 instruction.
- A selected-control receipt proves only logical Native visual selection. It does **not** prove an open output window, GPU presentation, physical scanout or under-50 ms latency.
- This is an internal development checkpoint, not a usable release or permission to replace church FreeShow. Only `native/` source/docs are changed on `native-port`; the original operational branch and church files are untouched.

## Next implementation

Implement the source action backend and actual timer/event ownership against the existing original source, starting with the normal lyric path and explicit handling of unsupported effects. Then connect project/navigation input and physical output-window lifecycle. Keep the independent renderer receipt and full-source acknowledgment requirements when enabling that backend; a no-op backend is not a substitute.
