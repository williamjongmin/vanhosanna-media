# Native 0.0.7 — Persistent video and lyric isolation

Goal: identical FreeShow operator behavior with video decoding removed from the lyric cue hot path.

## Source mapping
- src/frontend/components/output/Output.svelte: separate style, active media, slide, overlay layers
- src/frontend/components/output/layers/Background.svelte: background reuse and crossfade slots
- src/frontend/components/output/layers/BackgroundMedia.svelte: media vs NDI, screen and camera
- src/frontend/components/output/layers/Media.svelte: video and image media path handling
- src/frontend/components/output/layers/mediaLayers.ts: capture-worker composited slots per output
- Independent VanHosanna Native output prototype OutputWindow.cs: LibVLCSharp.Avalonia full-size lyric overlay

## Implemented source-code foundations
- NativeVideoCuePlanner: distinguishes style background and active output background from lyric selection. Warns and blocks when simultaneous videos would be silently lost.
- NativeVideoPlaybackState: Open / Keep / Stop / Unresolved decisions keyed by video ID and resolved path.
- FreeShowMediaLookup.ExistingLocalVideoPath: accepts existing local video files only; no remote URLs.
- NativePersistentVideoOutput: persistent Windows LibVLCSharp VideoView, separate full-size transparent Avalonia lyric overlay.
- NativeFreeShowOutputSession: independent owner for each physical output ID; ShowCue only swaps prepared lyrics, without calling the decoder or filesystem.
- NativeSlidePresenter: added transparent lyric-overlay mode while background is held externally.
- FreeShow.Native.VisualProbe: optional local-video argument for the developer comparison window.
- FreeShow.Native.Checks: source fixtures for repeat video, decoder lifecycle decisions and unsupported simultaneous video layers.

## Planned hot path
1. Parse original show/layout and selected template once, before the cue.
2. Start one local background video explicitly and keep its player open.
3. Advance lyric: NativeFreeShowOutputSession.ShowCue(index) -> NativeSlidePresenter.Show(index).
4. If no media background changed, never reopen/seek/restart the video.
5. Real background changes are separate ApplyBackgroundAsync calls.

This is structural separation, not a validated 50 ms claim.

## Development preview (do not use for live worship)
~~~powershell
dotnet run --project native/FreeShow.Native.VisualProbe -- "C:\FreeShow\Shows\Example.show" --video "C:\FreeShow\Media\loop.mp4"
dotnet run --project native/FreeShow.Native.VisualProbe -- "C:\FreeShow\Shows\Example.show" --data-root "C:\FreeShow" --style "my-style-id" --video "C:\FreeShow\Media\loop.mp4"
~~~

For active FreeShow output backgrounds, `NativeFreeShowOutputSession` accepts the currently selected background ID and an optional external media resolver. The real FreeShow media cache can live outside an individual .show file; callers must provide the correct lookup rather than inventing a media path.

## Not equivalent yet
- Two concurrent videos, scene media, camera, NDI/OMT, YouTube players, video transitions, output effects, crop/flip/filter/blend, media timing, output synchronization, stage/Online window composition
- Full original FreeShow operator UI and all workflows
- Pixel-perfect overlay alignment and audio/video handling on Dell OptiPlex 7490
- Original style images in the persistent-video prototype (the earlier still-image preview remains separate)
- Physical click-to-display latency

Dependencies match the separate native prototype: Avalonia 12.1.6, LibVLCSharp.Avalonia 3.10.1 and VideoLAN.LibVLC.Windows 3.0.24. Compatibility with the exact hardware remains untested.

## Data and verification
- Original source branch and church data remain untouched. Media paths read-only.
- Code committed only; no build/runtime/visual/hardware tests executed, per Beta 2 policy.
- Do not claim feature parity or improved latency until real OptiPlex measurements exist.
