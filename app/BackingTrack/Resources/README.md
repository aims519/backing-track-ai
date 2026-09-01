# Resources

`Assets.xcassets` and the launch screen.

## Asset catalog

| Entry | What it is |
| --- | --- |
| `AppIcon` | App icon. Single 1024×1024 (`AppIcon-1024.png`); iOS downscales the rest. |
| `AccentColor` | App-wide tint (`ASSETCATALOG_COMPILER_GLOBAL_ACCENT_COLOR_NAME`). Amber, with a lighter dark-mode variant. |
| `LaunchLogo` | The cog / beamed-notes glyph, white (original rendering, not template). Shown on the launch screen. |
| `LaunchBackground` | Solid brand colour behind the launch logo. |

The launch screen is the `UILaunchScreen` dictionary in the partial `../Info.plist`
(`UIColorName` → `LaunchBackground`, `UIImageName` → `LaunchLogo`) — iOS composites it, no
storyboard or XIB. That partial plist is merged onto Xcode's generated Info.plist; `project.yml`
wires it via `INFOPLIST_FILE` alongside `GENERATE_INFOPLIST_FILE`.

## Icon / logo are generated from code

`AppIcon-1024.png` and the three `LaunchLogo` PNGs are **not hand-drawn** — they're output of
[`../../Tools/generate_icon.py`](../../Tools/generate_icon.py), committed alongside so the assets
are reproducible and reviewable in a diff. To change the mark, edit the script and re-run:

```bash
python3 app/Tools/generate_icon.py                       # rewrites the committed PNGs
python3 app/Tools/generate_icon.py --accent teal         # try another palette
python3 app/Tools/generate_icon.py --preview /tmp/icons  # accent comparison 1024s
```

Changing `--accent` only changes the icon gradient — `AccentColor` / `LaunchBackground` in the
catalog are set by hand and must be kept in step with `ACCENTS` in the script.

## Status

**v1 placeholder.** The cog-with-beamed-eighth-notes mark is functional but not final art; good
enough to ship on-device and iterate. No image-model output is used for any shipped asset here
or anywhere in this repo — see [ADR 0021](../../../docs/adr/0021-code-drawn-visual-assets.md) and
the Authorship section of the top-level `CLAUDE.md`.
