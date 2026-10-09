# Quill

A paged word processor for Windows 11, built with C# on .NET 10 and WPF. Think "a small Word": real pages with
margins, headers and footers, page breaks and sections, printing and PDF export, and `.docx` as the native format.

> **Status: early.** The core engine (document model, layout, pagination, editing, `.docx` round trip) exists and is
> unit tested; the desktop shell runs but is minimal. See [docs/PLAN.md](docs/PLAN.md) for the full plan and milestones.

## Build and run (Windows)

```powershell
winget install Microsoft.DotNet.SDK.10
git clone <this repo>; cd random
dotnet run --project src/Quill.App
```

Visual Studio 2022 17.14+ (or VS 2026) opens `Quill.slnx` directly. Tests run with `dotnet test Quill.slnx`
(they use Microsoft.Testing.Platform; the opt-in lives in `global.json`).

The WPF-free projects (`Quill.Core`, `Quill.Layout`, `Quill.Docx`) and their tests also build and run on Linux or
WSL. The WPF projects *compile* there (thanks to `EnableWindowsTargeting`) but only *run* on Windows.

## Layout of the solution

| Project | Depends on WPF | Purpose |
|---|---|---|
| `src/Quill.Core` | no | Immutable document model, units (twips), styles, positions/selection, pure edit operations, undo, navigation |
| `src/Quill.Layout` | no | Pagination engine over `ILineFormatter`/`IFormattedLine` abstractions; keep rules, widow/orphan, sections, headers/footers, fields |
| `src/Quill.Layout.Wpf` | yes | `TextFormatter` adapter (line breaking, hit testing), `DrawingContext` render target, page renderer |
| `src/Quill.Docx` | no | `.docx` import/export with the Open XML SDK; unknown blocks are preserved opaquely |
| `src/Quill.App` | yes | The desktop app: document view (virtualized pages, zoom, caret, selection, keyboard/mouse editing), command bar, status bar |
| `tests/*` | | xunit.v3 suites; `Quill.Layout.Tests` drives the paginator with a fake monospace formatter |

## Key design decisions

- **Immutable model with structural sharing.** Every edit returns a new `Document`; unchanged paragraphs keep their
  identity, so the layout cache reuses their lines and undo is a stack of snapshots.
- **Twips everywhere in the model** (1/1440 in, the `.docx` unit); DIPs only at the layout boundary.
- **Own layout engine.** Paragraph lines come from WPF's `TextFormatter`; page filling, keep-with-next, widow/orphan
  control, page breaks, section starts and page-number fields are ours and are tested without WPF.
- **Fluent theme via `ThemeMode="System"`**, a custom command bar built from stock controls (no ribbon library), all
  theme keys in one `Theme.xaml`.

## Roadmap

Milestones M0–M8 and acceptance criteria are in [docs/PLAN.md](docs/PLAN.md). The next items are IME composition,
print preview and PDF export, the UI Automation text pattern, and packaging as MSIX.
