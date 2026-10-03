# Icon artwork

Provider logos use filled SVG paths rendered by Avalonia at the destination
display scale. Artwork is sourced from Lobe Icons under the adjacent MIT license.
Most logos use revision `329f378cbd1a88f45b60cd096b9111ce16f3ea39`.
`codex.svg` uses the dedicated [Codex terminal mark](https://github.com/lobehub/lobe-icons/blob/82e641b4fece9d1028a127149af9ded00df5ac0c/packages/static-svg/icons/codex.svg)
from revision `82e641b4fece9d1028a127149af9ded00df5ac0c`, not the OpenAI/ChatGPT
knot. Grok Bot retains its existing 256px alpha artwork and uses high-quality
bitmap minification.

`VectorIcon` supports these checked-in, path-only SVGs, not arbitrary SVG files.
Preserve the source viewBox and fill rule when adding a logo.

The legacy `codex.png` is kept in sync with the vector source, but runtime
provider rendering uses `codex.svg` in every layout and theme. Regenerate the
512px transparent PNG with:

```sh
dotnet run --project tests/UsageWidget.UiChecks.csproj -c Release -- --generate-codex-icon source/Assets
```

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
