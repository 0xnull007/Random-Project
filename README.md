# Quill

A paged word processor for Windows 11, built with C# on .NET 10 and WPF. Think "a small Word": real pages with
margins, headers and footers, page breaks and sections, printing and PDF export, and `.docx` as the native format.

> **Status: usable alpha.** The core engine (document model, layout, pagination, editing, `.docx` round trip) is
> unit tested, and the desktop app runs on Windows 11 with editing, lists, pictures, headers and footers, Page
> Setup, find and replace, printing, print preview and PDF export. See [docs/PLAN.md](docs/PLAN.md) for the plan
> and [docs/TESTING.md](docs/TESTING.md) for the feature-by-feature test checklist.


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
| `src/Quill.App` | yes | The desktop app: document view (virtualized pages, zoom, caret, selection, keyboard/mouse editing), ribbon, status bar |
| `tests/*` | | xunit.v3 suites; `Quill.Layout.Tests` drives the paginator with a fake monospace formatter |

## Key design decisions

- **Immutable model with structural sharing.** Every edit returns a new `Document`; unchanged paragraphs keep their
  identity, so the layout cache reuses their lines and undo is a stack of snapshots.
- **Twips everywhere in the model** (1/1440 in, the `.docx` unit); DIPs only at the layout boundary.
- **Own layout engine.** Paragraph lines come from WPF's `TextFormatter`; page filling, keep-with-next, widow/orphan
  control, page breaks, section starts and page-number fields are ours and are tested without WPF.
- **Fluent theme via `ThemeMode="System"`**, a Word-style ribbon (tabs, named groups, Segoe Fluent Icons) built from stock controls (no ribbon library), all
  theme keys in one `Theme.xaml`.


## Roadmap

Milestones M0–M8 and acceptance criteria are in [docs/PLAN.md](docs/PLAN.md). Done so far: editing with undo,
character and paragraph formatting (toolbar, Font and Paragraph dialogs, Format Painter, Modify Style, New Style), a styles gallery with previews,
bulleted, numbered and multilevel lists with numbering styles, change case, AutoCorrect, page and section breaks, multi-column
sections, page colour, page borders, watermarks, line numbers and automatic hyphenation, paragraph shading and borders, headers and footers with page-number fields and a Header & Footer tab (link to previous, first-page and odd/even variants), Page Setup, horizontal and vertical rulers with draggable indents and tab stops, a mini toolbar on selection,
inline and floating pictures (resize handles; square, top-and-bottom, behind and in-front wrapping; drag to move), hyperlinks, tables (editable cells, rows and columns, draggable column borders, shading, merged cells, repeating header rows, table style presets, alignment, autofit, text-table conversion), a table of contents, captions (Figure 1, Table 2) with a table of figures, bookmarks and cross-references, date and document-property fields, footnotes and endnotes,
comments, track changes (Review tab, with All/Simple/No Markup and Original views) and document compare, heading numbering, symbols and date/time, cover and blank pages, paragraph sorting, .docx
open and save (and .dotx templates), RTF and plain-text import/export, rich clipboard and drag-and-drop (tables included, HTML tables from Excel and browsers), find and replace, spell
checking (Windows Spell Checking API) with a Spelling dialog, IME input, print, print preview, PDF export, recent files, autosave
recovery, formatting marks, navigation pane with document search and page thumbnails, word count, light/dark theme, document properties, and a UI
Automation text pattern. Next: packaging as MSIX. The full manual
checklist is in [docs/TESTING.md](docs/TESTING.md).
