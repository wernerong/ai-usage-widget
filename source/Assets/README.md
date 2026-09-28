# Icon artwork

Provider logos use filled SVG paths rendered by Avalonia at the destination
display scale. Artwork is sourced from Lobe Icons, revision
`329f378cbd1a88f45b60cd096b9111ce16f3ea39`, under the adjacent MIT license.
`codex.svg` is Lobe's `openai.svg`; other names match upstream. Grok Bot retains
its existing 256px alpha artwork and uses high-quality bitmap minification.

`VectorIcon` supports these checked-in, path-only SVGs, not arbitrary SVG files.
Preserve the source viewBox and fill rule when adding a logo.

`widget.svg` is the editable application icon master. The small app/tray variant
uses pixel-aligned bars in `tests/IconAssets.cs` for clarity at 16–48px. Regenerate:

```sh
dotnet run --project tests/UsageWidget.UiChecks.csproj -c Release -- --generate-icons source/Assets
```

Commit the generated PNG, ICO and `AppIcon.iconset` representations together.
Windows gets individual 16, 20, 24, 32, 40, 48, 64, 128 and 256px ICO frames;
macOS gets standard 1x/2x representations through 1024px and a separate 44px
menu-bar image. Run the UI checks after regeneration for integrity checks and
light/dark icon proof sheets in the test output directory.
