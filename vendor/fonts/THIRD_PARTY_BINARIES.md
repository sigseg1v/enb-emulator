# Vendored fonts

Binary font files we ship with the Freya launcher (and reuse anywhere a
native app needs the project's typefaces). They are bundled as Avalonia
resources into `tools/LaunchFreya` so the launcher renders in the same
typography as the Freya Online website (`freya/online/web`), which loads the
identical families from Google Fonts at runtime.

These are `.ttf` binaries with no buildable "source" in the usual sense, so
per the repo policy they live under `vendor/` with this note rather than in a
project tree. `.gitignore` re-includes `vendor/**`.

| File | Family | Role | Upstream |
|---|---|---|---|
| `Michroma-Regular.ttf` | Michroma | display / wordmark | google/fonts `ofl/michroma` |
| `IBMPlexSans-VariableFont.ttf` | IBM Plex Sans | body (variable, all weights) | google/fonts `ofl/ibmplexsans` (`IBMPlexSans[wdth,wght].ttf`) |
| `IBMPlexMono-Regular.ttf` | IBM Plex Mono | monospace | google/fonts `ofl/ibmplexmono` |

Fetched from `https://github.com/google/fonts/raw/main/ofl/<family>/`.

## License

All three are licensed under the **SIL Open Font License 1.1** (OFL), which
permits bundling and redistribution with software. The verbatim license texts
are kept alongside the fonts:

- `OFL-Michroma.txt` -- Copyright 2011 The Michroma Project Authors.
- `OFL-IBMPlex.txt` -- Copyright (c) 2017 IBM Corp., Reserved Font Name "Plex".

The OFL is compatible with the project's other licenses: it governs only these
font files, independent of the CC BY-NC-SA 3.0 project default and the MIT
`freya/` code. Do not rename the font files to a Reserved Font Name derivative
and do not sell the fonts on their own (standard OFL terms).
