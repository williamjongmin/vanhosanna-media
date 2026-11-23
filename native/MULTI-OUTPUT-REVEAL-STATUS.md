# Native Port 0.0.22–0.0.23 — coherent multi-output lyric preview and template safety

## Objective
Keep the exact source FreeShow project, output ID, layout, line/click-reveal and template semantics while preventing hidden lyrics from being displayed by a mismatched or stale Native Preview. This milestone builds **read-only preparation** and does not enable live church output.

## Upstream references reviewed
- \`src/frontend/components/helpers/output.ts:getOutputLines()\`: retrieves the original Show slide (not the output-template merged clone) for \`getItemWithMostLines\` and source reveal flags; combines output-specific style.lines with global minimum style lines and highest line position.
- \`src/frontend/components/helpers/showActions.ts:getItemWithMostLines()\`: counts lines with at least one run whose \`value\` is defined.
- \`src/frontend/components/output/Output.svelte\`: keeps per-output \`out.slide\` and derived line states; in upstream FreeShow line updates may arrive after a short delay.
- \`src/frontend/components/output/layers/SlideContent.svelte\`: an unrevealed \`clickReveal\` item does not render; line-range changes avoid unnecessary full slide transitions.
- \`src/frontend/components/slide/TextboxLines.svelte\`: half-open line windows, final-available-line fallback, virtual breaks and showAsFaded behavior.
- \`native/FreeShow.Native.Core/FreeShowTemplateMerger.cs\`: the output-only template may change source item flags, and unported modes must be rejected.

## Changes implemented

### 0.0.22 — coherent multi-output snapshots
1. \`FreeShowOutputSnapshotBatch.Capture()\` reads original enabled-output \`OutData\` and Native revision values, checking each before/after and then checking the whole batch again. Missing or changing acknowledged output states produce an error. No source setting or .show is changed.
2. \`FreeShowOutputPreviewProjection.Prepare()\` accepts an optional captured batch. With a batch supplied, the current source \`out.slide\` is read **from that batch**, not from the initial \`settings.outputs[].out.slide\`. Incoherent/missing captures cannot silently fall back to startup data.
3. \`FreeShowRevealStyleResolver.ResolveCaptured()\` calculates source style inputs from the **same batch**, not multiple independent live state reads.
4. \`FreeShowMultiOutputPreview.Refresh()\` now uses one capture for the output identity, style and line reveal status; rechecks captured Native revisions after constructing the cards and replaces potentially stale cards with an explicit warning.
5. \`FreeShowOutputRevealBatchPlanner\` can derive one supported, original-shaped reveal frame for **every active normal output** in a shared snapshot, respecting each output's own style.lines and the shared maximum line position. Any unsupported output blocks complete batch prewarming; it does not lie about full-source parity.
6. \`NativeMultiOutputRevealPrewarmer\` validates that all independent Native output sessions are prepared for the matching source Show/layout and versions before allocating any cached lyric control. It then warms each output independently without displaying it, changing video position, triggering audio, or altering an output store. A partial prewarm or revision change reports failure.

### 0.0.23 — source/template reveal contract
1. \`FreeShowRevealRenderProjection\` now takes line count and reveal semantics from \`SlideFrame.RawSlide.items\`, matching the source's **original Show** rather than relying only on already merged Native item style records.
2. When output template merging changes source clickReveal/lineReveal flags or the original item count, the Native reveal projection is marked unsupported. This prevents a stripped visual-template reveal flag from showing original text that upstream FreeShow would keep hidden.
3. In the original getOutputLines zero-source-text case, Native now also refrains from constructing a fictional line-reveal range.
4. Unported CSS, effects or media warnings in read-only Preview now result in a blank diagnostic instead of a seemingly complete lyric output.
5. Additional synthetic fixture source code covers two active outputs, separate line counts, original slide indices, lost current output state, stale revisions, missing frames, and changed template reveal flags.

## Unimplemented and unverified
- **No actual synchronized physical output transition.** The batch is a planning/prewarming structure. It does not call \`ShowCue\`, \`setOutput\`, output_changed, or custom source actions.
- The Native revisions are acknowledged in-memory revisions. The batch retries/rejects stale snapshots but is **not** a substitute for locking real physical output controllers or obtaining an atomic external frame snapshot.
- Correct native rendering of showAsFaded, virtual breaks, template modes, per-word formatting, item animations, dynamic values, chord overlays, multiple media layers and stage/network outputs remains unported.
- Actual original FreeShow audio, timer, media and full project/slide action behavior is not production-ready.
- **No builds or tests were run**, per the user's explicit Beta 2 testing gate. The synthetic checks were authored and cannot be claimed as passing. No Windows screenshot parity, source-to-display latency measurements or stable livestream rehearsal results exist.
- The user's 1.0.0 goal remains conditional on the acceptance gates in \`RELEASE-1.0.0-GATES.md\`; do not tag a release without evidence.

## Safety
Only \`native/\` on \`native-port\` changed. Original \`church-dev\`, FreeShow projects, church .show, media, SETTINGS and active worship output devices remain untouched. The developer OperatorProbe stays read-only.
