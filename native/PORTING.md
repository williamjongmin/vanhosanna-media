# FreeShow native Windows port

Baseline: church-dev / package version 1.6.8-beta.5. The Electron implementation is the behavioral reference, not a design suggestion.

## Non-negotiable requirements
- Preserve the original UI layout, labels, shortcuts, interaction patterns, features and existing user data. No discretionary redesign or new features.
- Windows desktop runtime: Avalonia, C#, .NET 10. No Electron, Chromium, Node.js or WebView in the application runtime. Existing source remains reference code.
- Preserve existing FreeShow project/show/settings formats and unknown fields. Do not overwrite real church files during development. First implement read-only compatibility, then verified writes against copies.
- Target Dell OptiPlex 7490 with Intel integrated graphics and 16 GB RAM; Main, Online and Stage outputs. FoMaKo M1 handles broadcast encoding.
- Aim for physical click-to-display latency below 50 ms for prepared zero-transition lyrics; record median, p95, p99 and worst case. Do not confuse UI event dispatch with physical display latency. Preserve intentional transition settings.
- Maintain GPL-3.0 notices and obligations; preserve upstream attribution and track third-party dependencies.

## Porting gates
1. Inventory original source modules and persistence formats, capture baseline screenshots and interaction behavior.
2. Implement lossless native data access, layout ordering, and compatible output state; compare against original fixtures.
3. Port renderer and all layer types, retain the original slide geometry, text styles, media, output bindings and transitions; precompute where safe.
4. Port operator UI and editors against screenshots and original behavior, not substitute workflows.
5. Port remaining backend, middleware, device and network integrations, preserving configuration and behavior.
6. Compare functionality using actual copied FreeShow projects, including failure cases; run performance measurements on the target hardware.
7. Release only after functional parity and latency acceptance. No claim of parity before verification.

## Current implementation
`FreeShow.Native.Core/ShowDocument.cs` reads individual raw Show JSON objects according to `src/types/Show.ts`, preserving original bytes and raw layout entries. This is a first compatibility foundation, not a complete FreeShow storage implementation. Child expansion, global group resolution, on-disk wrapper/database formats, settings, rendering and the native UI are not yet implemented.

The previously developed VanHosanna native prototype is a separate reference for rendering and display lifecycle; its custom UI and database schema are not the port's specification.
