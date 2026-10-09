# Plan: Paged word processor for Windows 11 (C# / .NET 10 / WPF)

## Context

The user wants to build their own Word-like desktop application. Agreed constraints: Windows 11 only, C#/.NET (the user is comfortable with it), and a **paged document editor** for v1 (real pages, margins, page size and orientation, page breaks, sections, headers/footers with page numbers, print preview, print, PDF export, open/save .docx). Images, tables, lists, find/replace and spell check come after the core.

The working directory `/mnt/d/blackhole/random` is empty, so this is greenfield. The shell is WSL, which cannot build WPF; builds and runs happen on the Windows side (Visual Studio or `dotnet` CLI in PowerShell, or `cmd.exe /c dotnet ...` from WSL).

Stack decision (already made with the user): **WPF on .NET 10 with the built-in Fluent theme (`ThemeMode`)**, a custom document model, a custom layout/pagination engine on `TextFormatter`, the Open XML SDK for .docx, PDFsharp for PDF export. WinUI 3 was rejected (no managed line-layout API, weaker printing, no ribbon, rougher tooling).

Solution name below is the placeholder **Quill**; rename freely.

## Verified facts that shape the plan (as of Oct 2026)

| Fact | Consequence |
|---|---|
| WPF `ThemeMode` (Light/Dark/System/None) exists since .NET 9, still `[Experimental("WPF0001")]` in .NET 10 and .NET 11 previews. .NET 10 added more Fluent control styles; "Fluent UI style support is still in progress." ([net10 what's new](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100), [using-fluent.md](https://github.com/dotnet/wpf/blob/main/Documentation/docs/using-fluent.md)) | Set `ThemeMode="System"` at **Application** level; add `<NoWarn>WPF0001</NoWarn>`. Never use `SystemColors.*` (do not update in dark mode); use Fluent resource keys via `DynamicResource`. Isolate theme keys in one `Theme.xaml` in case of renames. |
| The Fluent theme already applies **Mica** via DWM automatically (opt-out switch `Switch.System.Windows.Appearance.DisableFluentThemeWindowBackdrop`). | No custom DWM code in v1. Keep the document canvas opaque so Mica does not bleed behind pages. |
| Built-in `System.Windows.Controls.Ribbon` QAT breaks under `ThemeMode` ([dotnet/wpf #10757](https://github.com/dotnet/wpf/issues/10757), open). | Do not use the built-in Ribbon. |
| Fluent.Ribbon 11.0.2 (MIT) works on .NET 10 but ships its own ControlzEx theme engine and `RibbonWindow` chrome; nothing documents coexistence with `ThemeMode`. | Default to a custom command bar built from Fluent-styled stock controls; run a one-day Fluent.Ribbon spike in M0 and adopt only if clean (see §8). |
| `DocumentFormat.OpenXml` 3.5.1; 3.x removed `Close()`/`SaveAs`. | Pin 3.5.1. SDK does not reorder children: emit `pPr`/`rPr`/`sectPr` children in schema order and validate with `OpenXmlValidator`. |
| PDFsharp 6.2.4 (MIT), package `PDFsharp-wpf` targets net10.0-windows. Microsoft Print to PDF always prompts for a filename; no supported WPF way to preset it. | PDF export = PDFsharp. Print-to-PDF stays available as a normal printer. |
| MSIX for WPF: documented route is a Windows Application Packaging Project (`.wapproj`). | Use `.wapproj`. |
| `TextFormatter`/`TextSource`/`TextLine` are current in .NET 10 docs; `TextParagraphProperties` exposes `Tabs`, `Indent`, `ParagraphIndent`, `LineHeight`, `TextMarkerProperties`. | Tab stops, indents, exact line height natively supported. |
| `Xunit.StaFact` 4.x gives `[WpfFact]`/`[StaFact]` for xunit.v3. | Test stack = xunit.v3 + Xunit.StaFact 4.x. |
| WPF managed UIA exposes `ITextProvider`/`ITextRangeProvider` only (no `ITextProvider2`/`ITextEditProvider`). | Implement those two; caret is reported via selection. |
| .NET 10 unified WPF/WinForms clipboard and obsoleted `BinaryFormatter`-based clipboard methods. | Only `string`/`byte[]`/`MemoryStream` clipboard payloads. |

## 1. Solution and project structure

```
/mnt/d/blackhole/random/
  Quill.sln
  global.json                 # pin .NET 10 SDK
  Directory.Build.props       # Nullable, ImplicitUsings, TreatWarningsAsErrors, NoWarn WPF0001
  Directory.Packages.props    # Central Package Management
  .editorconfig
  src/
    Quill.Core/          net10.0          WPF-free: model, units, styles, positions, selection, edit ops, undo
    Quill.Layout/        net10.0          WPF-free: pagination over ILineFormatter / IRenderTarget abstractions
    Quill.Layout.Wpf/    net10.0-windows  UseWPF: TextFormatter adapter, DrawingContext render target, font catalog
    Quill.Docx/          net10.0          WPF-free: Open XML import/export
    Quill.Pdf/           net10.0-windows  PDFsharp render target (may be merged into App)
    Quill.App/           net10.0-windows  UseWPF WinExe: DocumentView, editor controller, command bar, dialogs, print, UIA
    Quill.Package/       .wapproj         MSIX
  tests/
    Quill.Core.Tests/            xunit.v3
    Quill.Layout.Tests/          xunit.v3, FakeLineFormatter (no STA)
    Quill.Layout.Wpf.Tests/      xunit.v3 + Xunit.StaFact ([WpfFact])
    Quill.Docx.Tests/            corpus round-trip + OpenXmlValidator
    Quill.Rendering.GoldenTests/ RenderTargetBitmap vs PNG, layout snapshots
    Quill.App.UITests/           FlaUI.UIA3 end-to-end
  samples/docx-corpus/           reference documents authored in Word
```

Packages: `DocumentFormat.OpenXml` 3.5.1, `PDFsharp-wpf` 6.2.4, `Microsoft.Windows.CsWin32` (P/Invoke for IMM32, caret blink time, DWM, later Spell Checking COM), `CommunityToolkit.Mvvm` (optional), `xunit.v3`, `Xunit.StaFact` 4.x, `Verify.XunitV3`, `FlaUI.UIA3`, `BenchmarkDotNet`.

Dependency rule (project references only): `Core <- Layout <- Layout.Wpf <- App`; `Core <- Docx <- App`; `Layout.Wpf <- Pdf <- App`. `Core`, `Layout`, `Docx` never reference PresentationCore/Framework, so model, pagination rules and file I/O are testable on a plain thread and can run on background threads.

CI: GitHub Actions `windows-latest`, `dotnet build -c Release`, `dotnet test`; MSIX build on tags.

## 2. Document model (Quill.Core)

**Units.** `Twips` (1/1440 in) is the canonical length, matching every docx length exactly. `HalfPoints` for font size (docx `w:sz`). `LineSpacing { Rule: Auto|Exact|AtLeast, Value }`. Layout and WPF use DIPs (`twips / 15.0`); PDFsharp uses points (`twips / 20.0`). Conversions happen only at the layout boundary.

**Immutable, structurally shared tree** (recommended over a mutable tree with inverse commands):

```
Document  { ImmutableList<Section> Sections; StyleSheet Styles; DocumentDefaults; Settings; Metadata }
Section   { SectionProperties Props; BlockContainer Body; HeaderFooterSet Headers, Footers }
SectionProperties { PageWidth, PageHeight, Orientation, Margins(+Header,Footer,Gutter), SectionStart, TitlePage, PageNumbering }
HeaderFooterSet   { BlockContainer? Default, First, Even }   // null = linked to previous section
Block = Paragraph | OpaqueBlock (preserved unknown XML) | Table (later)
Paragraph { StyleId; ParagraphProperties Direct; RunProperties MarkProps; ImmutableArray<Inline> Inlines }
Inline    = Run { Text; StyleId; RunProperties Direct } | Break { Line|Page|Column } | Field { Page|NumPages|SectionPages|Unknown } | InlineImage (later)
StyleSheet { Styles by id; default paragraph/character style ids }
```

All property members are nullable (sparse direct formatting); `Merge` = other overrides non-null.

Why immutable: undo/redo is a stack of `(Document, Selection)` snapshots with structural sharing; the layout cache is keyed by `Paragraph` reference identity so unchanged paragraphs reuse formatted lines for free; `IsDirty == !ReferenceEquals(current, lastSaved)`; layout, docx export and PDF export can run on background threads against a snapshot.

**Positions.** Each paragraph exposes a cached `FlatText` (run texts concatenated; each non-run inline contributes one U+FFFC; tabs are `'\t'`). Offsets are UTF-16 code units; `Offset == FlatText.Length` is the paragraph mark.

```
StoryId(SectionIndex, StoryKind)              // Body, HeaderDefault, HeaderFirst, ..., FooterEven
BlockPath(ImmutableArray<int>)                // [blockIndex] now; [tbl,row,cell,block] later
TextPosition(StoryId, BlockPath, Offset) : IComparable
Selection(Anchor, Active, CaretAffinity)      // never spans stories
```

**Formatting resolution.** `StyleResolver` (pure, cached) applies docx precedence: document defaults -> paragraph style chain (`BasedOn`, root first) -> character style chain -> paragraph-mark run props -> direct run props. Output: fully populated `ResolvedRunProperties` / `ResolvedParagraphProperties`. Simplification: direct formatting wins (no toggle semantics); document this.

**Editing operations** (`DocumentEditor`, pure functions returning new `Document` + `ChangeSet`): `InsertText`, `DeleteRange`, `SplitParagraph` (applies `Style.Next`), `MergeParagraphs`, `InsertBreak`, `ApplyRunFormat`, `ApplyParagraphFormat`, `SetParagraphStyle`, `InsertFragment` / `ExtractFragment` (paste/copy), `SplitSection`, `SetSectionProperties`. Normalize run boundaries and merge adjacent equal runs. Grapheme-aware delete via `StringInfo.GetNextTextElementLength`.

`EditorState { Document; Selection; RunProperties? PendingFormat }` where `PendingFormat` is the sticky format for Ctrl+B with a collapsed selection (cleared on caret move).

Default stylesheet: Normal (Aptos/Calibri 11, 8pt after, 1.08 line), Heading 1-3, Title; Letter or A4 by `RegionInfo.IsMetric`; 1 inch margins.

## 3. Layout and pagination

**Line formatting: `TextFormatter` + custom `TextSource`** (AvalonEdit's approach). It gives Unicode line breaking, bidi, font fallback, justification, tab stops, exact line height, and caret/hit-test APIs on `TextLine`. Rejected: `FormattedText` (no per-line access), raw `GlyphRun` (own shaping), `FlowDocument` (no sections/headers/page fields, unpaginated editing), DirectWrite interop (deferred to v2 if limits are hit).

**WPF-free abstractions in `Quill.Layout`:**

```csharp
interface ILineFormatter { IReadOnlyList<IFormattedLine> FormatParagraph(ParagraphLayoutInput input); }
interface IFormattedLine : IDisposable {
    int Start, Length; BreakKind? ForcedBreakAfter;
    double Height, Baseline, Width, StartX;
    double GetCaretX(int offset, CaretAffinity affinity);
    CharacterHitResult HitTest(double x);
    IReadOnlyList<RectD> GetTextBounds(int start, int length);
    int NextCaretStop(int offset); int PreviousCaretStop(int offset); int BackspaceCaretStop(int offset);
    void Draw(IRenderTarget target, PointD origin);
    IEnumerable<TextSegment> GetSegments();     // word-granularity (text, props, x, baseline) for the PDF backend
}
interface IRenderTarget { FillRect; DrawLine; DrawImage; PushClip; Pop; DrawTextSegments; }
```

`Quill.Layout.Wpf` implements `WpfLineFormatter` (one `TextFormatter.Create(TextFormattingMode.Ideal)` per thread), `WpfFormattedLine` (wraps `TextLine`; `Draw` calls `TextLine.Draw` when the target is WPF), `WpfRenderTarget` (wraps `DrawingContext`).

**`ParagraphTextSource : TextSource`** built from resolved run spans. `GetTextRun` returns `TextCharacters` with `QuillTextRunProperties` (Typeface, em size in DIP, frozen brushes, decorations, culture, PixelsPerDip); `TextEndOfLine` for line/page breaks (paginator reads `ForcedBreakAfter`); field text for `Field`; `TextHidden` for hidden runs; `TextEndOfParagraph` at end. `QuillTextParagraphProperties`: alignment (incl. Justify), flow direction, first-line/hanging `Indent`, `ParagraphIndent`, `LineHeight` for Exact, `Tabs` (`TextTabProperties`), `DefaultIncrementalTab` (720 twips). Right indent = shrink the width passed to `FormatLine`. Loop `FormatLine(..., previousLineBreak)` with a `TextRunCache` per paragraph. `Auto`/`AtLeast` spacing applied post-format by scaling each line's slot height.

**Paginator (`Quill.Layout`, testable with a fake formatter).** Output:

```
LayoutDocument { ImmutableArray<PageLayout> Pages; int TotalPages }
PageLayout     { Index; SectionIndex; Size; BodyArea; HeaderArea; FooterArea; PageNumberText; Body fragments; Header/Footer story layouts }
ParagraphFragment { Paragraph; BlockPath; FirstLine; LastLine; Bounds; ParagraphLayout }
ParagraphLayout   { Paragraph Key; Width; Lines; LineTops; SpaceBefore; SpaceAfter; Height }
```

Algorithm:
1. Per section compute page geometry; apply `SectionStart` (NextPage; Even/Odd insert a blank page when parity is wrong; Continuous treated as NextPage in v1).
2. Walk body blocks with a cursor `y`; get `ParagraphLayout` from `LayoutCache` (key: paragraph reference + width + stylesheet reference) or format it.
3. Place lines honoring: suppress `SpaceBefore` at page top; **widow/orphan** (2/2 when `widowControl` on); **keep-lines** (move whole paragraph unless taller than a page); **page-break-before**; **keep-with-next** chains (backtrack chain to next page unless it exceeds a page); explicit `Break(Page)` ends the page and the paragraph continues as a new fragment on the next page.
4. Headers/footers: choose variant per page (`TitlePage` -> First; `EvenAndOddHeaders` -> Even; else Default; null -> inherit from previous section). Body top = max(top margin, header bottom); body bottom = min(page height - bottom margin, footer top). PAGE/NUMPAGES: body pass first, then headers/footers with final counts; repeat once if header height changed (digit count).
5. Incremental relayout: on `DocumentChanged(old, new, changeSet)`, mark-and-sweep the `LayoutCache` by reference into a fresh dictionary; re-paginate from the first page containing an affected block; earlier pages reused verbatim.
6. Chunked, resumable `LayoutSession.ContinueFor(budget)` at `DispatcherPriority.Background` so the first pages show immediately and the status bar reads "Page 3 of 120...".

## 4. Rendering (`DocumentView` in Quill.App)

`DocumentView : FrameworkElement, IScrollInfo` inside a `ScrollViewer` (`CanContentScroll=True`). Visual tree via `VisualCollection`:
- Opaque canvas background (Fluent key via `DynamicResource`).
- `PageVisual` per **materialized** page (viewport +/- 1 page): page chrome (white, 1-px border, shadow), `ContentVisual : DrawingVisual` (repainted only when its `PageLayout` instance changes), `SelectionVisual` (translucent accent rects from `GetTextBounds`), optional dim overlay while editing header/footer.
- Single `CaretVisual` moved between pages.

Zoom: `ScaleTransform` on the page container; layout stays in DIPs so zoom never re-paginates. Ctrl+wheel zooms around the mouse point; slider 10-500%; Fit Width / Whole Page presets.

Text settings: `TextOptions.TextFormattingMode="Ideal"` (must match the formatter mode), `TextRenderingMode="ClearType"`, `TextHintingMode="Fixed"` (`Animated` during continuous zoom), `UseLayoutRounding="True"`, `GuidelineSet` for borders and caret, `PixelsPerDip` from `VisualTreeHelper.GetDpi` refreshed in `OnDpiChanged`. Never put `Opacity` or `BitmapCache` on page visuals (kills ClearType); dim via overlay rectangle.

Caret: width `SystemParameters.CaretWidth`, height = line height, blink at `GetCaretBlinkTime()` (CsWin32), no blink when `INFINITE`; hidden when unfocused or dragging; ensure-visible after every edit and navigation.

Dark mode: page stays white with document colors (like Word); all chrome from Fluent keys.

## 5. Input and editing

- **Text input**: `Focusable=true`; override `OnTextInput` (handles dead keys, AltGr, Unicode; never synthesize chars from `KeyDown`); `OnKeyDown` for navigation; `CommandBindings` for `ApplicationCommands` (New/Open/Save/Print/Cut/Copy/Paste/Undo/Redo/SelectAll/Find) and `EditingCommands` (ToggleBold/Italic/Underline, Align*, Increase/DecreaseFontSize, Move*). Extra gestures: Enter (paragraph), Shift+Enter (line break), Ctrl+Enter (page break), Tab, Ctrl+Backspace/Delete (word), Ctrl+Home/End, PageUp/Down (keep x), Ctrl+1/2/5 spacing, F12, Ctrl+P.
- **Caret movement**: grapheme-aware via `TextLine.GetNextCaretCharacterHit` family; vertical movement via `LayoutQuery.MoveVertical(pos, +/-1, desiredX)` across fragments and pages; `WordBoundaryFinder` for Ctrl+arrows (Word's skip-trailing-spaces rule).
- **Hit testing**: view point -> unzoom/unscroll -> page (binary search) -> area (header/footer/body) -> fragment -> line -> `HitTest(x)`; beyond line end -> end-of-line, Upstream affinity. Double-click in header/footer enters header/footer editing mode (Esc or double-click body exits).
- **Mouse**: ClickCount 1/2/3 = caret/word/paragraph; Shift+click extends; drag extends by click unit; `CaptureMouse`; autoscroll timer outside viewport; right-click moves caret unless inside selection; `Cursors.IBeam`.
- **IME**: `InputMethod.SetIsInputMethodEnabled(this, true)`; handle `TextInputStart/Update/TextInput` for a provisional composition run (dotted underline, not on undo stack); position composition/candidate windows at the caret via `ImmGetContext` / `ImmSetCompositionWindow` / `ImmSetCandidateWindow` / `ImmAssociateContext` on focus (AvalonEdit `ImeSupport` pattern). v1 may show composition in the IME's own window. Test Japanese, Simplified Chinese, Korean.
- **Clipboard** (`IClipboardService` in Core, WPF impl in App): write `UnicodeText`, `Rtf` (own writer for the subset), `Html` (CF_HTML header), and a private `"Quill.Fragment"` `byte[]` format. Paste priority: private -> RTF (parse by `TextRange.Load` into a throwaway `FlowDocument`, then convert) -> HTML (later) -> text.
- **Undo/redo**: `UndoStack` of `EditRecord { Document Before, After; Selection Before, After; EditKind; Time }`. Coalesce consecutive typing when adjacent, same paragraph, no caret move, not starting a new word after whitespace, gap < ~1 s. Backspace/Delete coalesce with their own kind. Cap ~1000 records. Redo cleared on new edit.

## 6. .docx import/export (Quill.Docx, Open XML SDK 3.5.1)

Parts: `MainDocumentPart`, `StyleDefinitionsPart` (incl. `docDefaults`), `DocumentSettingsPart` (`defaultTabStop`, `evenAndOddHeaders`), `HeaderPart`/`FooterPart` (default/first/even), `ThemePart` (resolve theme fonts and colors; mandatory since most Word docs use theme fonts), `FontTablePart` (minimal), `CoreFilePropertiesPart`; later `NumberingDefinitionsPart`, `ImagePart`.

v1 mapping:
- `pPr`: `pStyle, keepNext, keepLines, pageBreakBefore, widowControl, spacing, ind, jc, tabs, contextualSpacing, rPr (mark), sectPr`.
- `rPr`: `rStyle, rFonts (+theme attrs), b, i, u, strike, dstrike, color (+themeColor), sz, highlight, vertAlign, vanish, lang`.
- Run content: `w:t` (honor/emit `xml:space="preserve"`), `w:tab`, `w:br` (textWrapping/page/column), `w:cr`, `noBreakHyphen`, `softHyphen`, `w:sym`.
- Fields: `fldSimple` and complex `fldChar` runs -> `Field` (PAGE/NUMPAGES/SECTIONPAGES parsed; others `Unknown` with cached result, exported as `fldSimple`).
- `sectPr`: `pgSz, pgMar, type, titlePg, pgNumType, headerReference, footerReference`; `cols` read, single column laid out (warning if >1). Convert sectPr-in-last-paragraph form to explicit `Section`s on import and back on export.
- Transparent wrappers: `hyperlink`, `sdt`/`sdtContent`, `smartTag`, `ins` (accept), `del` (drop), bookmarks, `proofErr`, `lastRenderedPageBreak`, `mc:AlternateContent` -> `mc:Fallback`.
- Unknown **block** elements (`tbl`, anchored `drawing`, unmappable `sdt`): `OpaqueBlock { LocalName, OuterXml }` rendered as a grey placeholder, re-emitted verbatim on save. Unknown inline elements dropped and recorded in `LoadReport.Warnings`.
- Non-mapped parts (comments, footnotes, custom XML, macros) dropped; package regenerated from scratch on save.

Fidelity UX: info bar listing dropped content; on first save over a file with warnings prompt "Save a copy instead?"; read fully into memory and close (no lock); save via temp file + `File.Replace`.

## 7. Printing and PDF

- **Print**: `PrintDialog` (`UserPageRangeEnabled`), `PrintTicket.PageMediaSize`/`PageOrientation` from the first section; `QuillDocumentPaginator : DocumentPaginator` returning `DocumentPage(DrawingVisual, ...)` painted by the same `WpfRenderTarget` without selection/caret; for mixed orientation/size use `CreateVisualsCollator` with per-page tickets; warn when margins fall inside `PageImageableArea`.
- **Preview**: reuse `DocumentView` read-only in `PrintPreviewWindow`; identical renderer guarantees WYSIWYG.
- **PDF export**: `PdfRenderTarget : IRenderTarget` over PDFsharp `XGraphics` (`XUnit.FromPoint`), per-page size/orientation, metadata, Unicode font embedding. Text drawn from `IFormattedLine.GetSegments()` at word granularity using layout-computed x/baseline so breaks, justification and tabs match the screen. Documented limitation: complex-script shaping in PDFsharp is weaker than WPF's. Microsoft Print to PDF remains available through the print dialog.

## 8. Windows 11 integration and ribbon decision

- `<Application ThemeMode="System">`; Light/Dark/System setting writes `Application.Current.ThemeMode`. Fluent keys only (`TextFillColorPrimaryBrush`, `AccentFillColorDefaultBrush`, `SolidBackgroundFillColorBaseBrush`, ...). Test runtime light/dark switch, accent change, HighContrast.
- Mica comes free with the Fluent theme. Mica Alt/Acrylic via `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE)` deferred.
- `app.manifest` with `<dpiAwareness>PerMonitorV2</dpiAwareness>`; handle `OnDpiChanged`.
- `System.Windows.Shell.JumpList` with `ShowRecentCategory`, `JumpList.AddToRecentCategory(path)` after open/save; in-app MRU in settings.
- MSIX `.wapproj` with `uap:FileTypeAssociation` for `.docx` as an additional handler; `App.OnStartup(e.Args)` opens files; multi-instance acceptable in v1.
- **Ribbon / command surface**: build a lightweight custom one from Fluent-styled stock controls: `TabControl` (Home/Insert/Layout/View) hosting `ToolBar`/`WrapPanel` groups of `Button`/`ToggleButton`/`ComboBox` (font family/size), a File overlay panel (New/Open/Recent/Save/Export/Print/Settings), and a status bar (page x of y, words, zoom). Keep it behind view-models so swapping to Fluent.Ribbon later is UI-only. **M0 spike**: Fluent.Ribbon `Ribbon` inside a plain `Window` with `ThemeMode=System`; check resource collisions, dark sync, popup styling, Mica; adopt only if clean. Rejected: built-in Ribbon (#10757), WPF-UI (second theme engine).

## 9. Accessibility (UI Automation)

`DocumentViewAutomationPeer : FrameworkElementAutomationPeer` (control type Document, name, focusable) with `ITextProvider` (`DocumentRange`, `GetSelection`, `GetVisibleRanges`, `RangeFromPoint`) and `ITextRangeProvider` over `(TextPosition, TextPosition)` (`ExpandToEnclosingUnit` for Character/Word/Line/Paragraph/Page/Document, `Move*`, `GetText`, `GetBoundingRectangles` zoom-aware, `GetAttributeValue` for font name/size/weight/italic/underline/color/alignment with `MixedAttributeValue`, `FindText`, `Select`, `ScrollIntoView`). Raise `TextPatternOnTextChanged` / `TextPatternOnTextSelectionChanged` guarded by `ListenerExists`. `AutomationProperties.Name` on every icon-only button; visible focus rectangles; HighContrast selection/caret.

Schedule: bare peer in M3 (hours); full Text pattern in M7 before the first public build. Validate with Accessibility Insights FastPass and Narrator. The Text pattern doubles as the FlaUI test hook.

## 10. Milestones

| # | Milestone | Deliverables | Acceptance criteria |
|---|---|---|---|
| **M0** (wk 1) | Skeleton + spikes | Solution, CPM, CI, PMv2 manifest, `ThemeMode=System` window with the command bar shell; spikes: TextFormatter (format/draw/hit-test one paragraph), PDFsharp text drawing, Fluent.Ribbon cohabitation | `dotnet build/test` green on `windows-latest`; spike notes recorded; ribbon decision made |
| **M1** (wk 2-3) | Core model | Units, immutable tree, positions/selection, `StyleResolver`, `DocumentEditor`, `UndoStack` with coalescing, default stylesheet | >=150 unit tests; property tests over random edit sequences (runs merged, offsets valid, undo returns identical references); zero WPF references |
| **M2** (wk 4-7) | Layout + pagination | `ILineFormatter`/`IFormattedLine`, `WpfLineFormatter`/`ParagraphTextSource`, `Paginator` with all break rules, sections, line spacing, tabs, `LayoutCache`, incremental relayout, chunked sessions | Fake-formatter tests for every rule; `[WpfFact]` formatting tests; 300-page lorem paginates < 2 s cold, single-paragraph edit relayout < 5 ms; layout snapshot tests |
| **M3** (wk 8-9) | Read-only viewer + minimal docx import | `DocumentView` (virtualization, zoom, scroll, DPI, chrome), bare UIA peer, docx import of paragraphs/runs/styles/sections/theme fonts, load report | Opens real Word documents; no frame > 16 ms; crisp text at 100/150/200% DPI and 50-400% zoom; golden images pass |
| **M4** (wk 10-14) | Editing | Caret/selection (mouse, keyboard, grapheme-aware), typing, IME, clipboard, undo/redo UI, character/paragraph formatting commands, sticky format, font/size pickers, paragraph/page breaks, autoscroll | FlaUI scripts for 40+ scenarios; typing latency < 16 ms on 100-page doc; JA/ZH/KO IME works; RTF paste into Word/Outlook |
| **M5** (wk 15-17) | Sections, headers/footers, full docx round trip | Page Setup dialog, section breaks, header/footer edit mode, PAGE/NUMPAGES, first/even pages, link-to-previous, docx export of everything, `OpaqueBlock` preservation, fidelity prompts, atomic Save/Save As | Corpus round trip equals model; `OpenXmlValidator` clean; Word opens every file without repair; tall header pushes body |
| **M6** (wk 18-19) | Print, preview, PDF | `QuillDocumentPaginator`, `PrintDialog`, per-page tickets, page ranges, `PrintPreviewWindow`, `PdfRenderTarget` with metadata/embedded fonts | Printed/PDF pages overlay screen render within tolerance; PDF opens in Edge/Acrobat with subset fonts |
| **M7** (wk 20-22) | Shell polish, Windows 11, accessibility, packaging | File panel, recent files, jump list, theme setting, Font/Paragraph dialogs, status bar, access keys, full UIA Text pattern, HighContrast pass, crash log, MSIX, file association | MSIX installs on a clean Win11 VM; `.docx` opens from Explorer; Accessibility Insights no critical issues; Narrator reads/navigates; runtime theme switch clean |
| **M8+** (post-v1) | Lists, inline images, tables, find/replace, spell check (Windows Spell Checking API), hyperlinks, autosave/recovery | Per-feature round-trip and layout tests |

**Riskiest milestone: M2.** Everything consumes its outputs; TextFormatter quirks surface there (justified trailing whitespace, `Indent`/`ParagraphIndent` interplay, tab alignment vs indents, exact line height clipping, bidi caret stops); its contracts (`TextPosition`, identity-keyed cache) are expensive to change later. Mitigations: M0 spike, compare against Word on a corpus, WPF-free engine tested with a fake formatter, thin `IFormattedLine` so DirectWrite could replace TextFormatter later.

## 11. Testing strategy

- **Core** (xunit.v3, no STA): model invariants, style resolution tables, position comparison, undo coalescing with injected clock, property-based random edit sequences.
- **Pagination** (no STA): `FakeLineFormatter` (monospace 10 DIP/char, 20 DIP lines) makes every rule deterministic ("page 2 starts at paragraph 7 line 2", widow/orphan matrices, keep-next chains, section parity blank pages, header growth).
- **WPF** (`[WpfFact]`): anything touching `TextFormatter`, `Typeface`, `DrawingVisual`, `RenderTargetBitmap`.
- **Layout snapshots** (`Verify.XunitV3`): JSON of line boxes per page for corpus docs.
- **Golden images**: 96 DPI, Grayscale rendering, bundled open font via `pack://` so CI matches; < 0.1% differing pixels; 10-20 images.
- **docx**: corpus per feature, round-trip equality, `OpenXmlValidator`, manual "opens in Word without repair" gate per milestone.
- **UI**: FlaUI launches the app, types, presses buttons by `AutomationId`, asserts through the Text pattern; separate CI job with retries.
- **Performance**: BenchmarkDotNet for formatting/pagination; soft-threshold `Stopwatch` tests.
- **Manual checklist per milestone**: IME languages, mixed DPI, runtime theme switch, HighContrast, Print to XPS/PDF, OneDrive file.

## 12. Risks and commonly overlooked behaviors

| Risk | Mitigation |
|---|---|
| TextFormatter diverges from Word | M0 spike, corpus comparison, `IFormattedLine` abstraction |
| Experimental `ThemeMode` changes in .NET 11 | Centralize keys in `Theme.xaml`; pin SDK in `global.json`; Application-level only |
| Destroying user content on save | `OpaqueBlock` preservation, load report, "Save a copy?" prompt, atomic save, no file locks |
| Large documents | Chunked pagination, identity-keyed cache, bounded undo, page virtualization |
| IME edge cases | Follow AvalonEdit `ImeSupport`; test three IMEs in M4 |
| PDF text fidelity | Word-granularity positioning from layout; overlay tests; Print-to-PDF fallback |
| Mixed orientation printing | Per-page `PrintTicket` via `VisualsToXpsDocument` |
| Missing fonts | WPF fallback; "(substituted)" in picker; theme-font resolution on import |
| MSIX signing | Self-signed sideload during dev; decide Store vs Trusted Signing before M7 |

Do not forget: paragraph-mark run properties (empty-paragraph height, font of new paragraph); `Style.Next` on Enter; sticky formatting; typing replaces selection; caret affinity at wraps; `SpaceBefore` suppressed at page top; selection across page boundaries with autoscroll; header/footer edit mode and link-to-previous; PAGE field relayout per page; `xml:space="preserve"`; schema child order in `pPr`/`rPr`; cache `Fonts.SystemFontFamilies` (slow); emoji ZWJ and combining marks in backspace vs delete; `\t` in justified lines; file locked by Word; unsaved-changes prompt on close and `SessionEnding`; strings in `.resx` from day one; shortcuts colliding with IME (Ctrl+Shift); dark mode for dialogs and context menus.

## 13. Dev environment setup (Windows side, PowerShell)

```powershell
winget install Microsoft.DotNet.SDK.10
# optional: Visual Studio 2022 17.14+ with ".NET desktop development" and "MSIX Packaging Tools"
cd D:\blackhole\random
dotnet new sln -n Quill
dotnet new classlib -n Quill.Core   -o src/Quill.Core   -f net10.0
dotnet new classlib -n Quill.Layout -o src/Quill.Layout -f net10.0
dotnet new classlib -n Quill.Docx   -o src/Quill.Docx   -f net10.0
dotnet new wpf      -n Quill.Layout.Wpf -o src/Quill.Layout.Wpf      # then change OutputType to Library
dotnet new wpf      -n Quill.App    -o src/Quill.App
dotnet new xunit    -n Quill.Core.Tests -o tests/Quill.Core.Tests     # switch to xunit.v3 template if available
dotnet sln add (Get-ChildItem -Recurse *.csproj)
```

Then: `Directory.Build.props` (Nullable, ImplicitUsings, TreatWarningsAsErrors, `<NoWarn>$(NoWarn);WPF0001</NoWarn>`), `Directory.Packages.props` with the pinned versions in §1, `global.json` pinning the .NET 10 SDK, `app.manifest` with PerMonitorV2, `<Application ThemeMode="System">` in `App.xaml`.

From WSL, builds can be invoked with `cmd.exe /c "dotnet build D:\blackhole\random\Quill.sln"` but editing is fine from either side.

## 14. Verification (end-to-end)

1. **Build/test**: `dotnet build -c Release` and `dotnet test` green on Windows and on `windows-latest` CI for every milestone.
2. **M0 spike check**: a window with `ThemeMode=System` shows Mica and follows OS light/dark; one paragraph formatted by `TextFormatter` renders, hit-tests and draws a caret; PDFsharp draws the same text to a PDF; Fluent.Ribbon spike result recorded.
3. **M2**: fake-formatter tests cover widow/orphan, keep-next, keep-lines, page-break-before, explicit breaks, section parity, header growth; benchmark targets met.
4. **M3**: open 5+ real Word documents from `samples/docx-corpus`; scroll/zoom smooth; golden images pass at 100/150/200% DPI.
5. **M4**: FlaUI editing scripts pass; type across a page boundary; IME composition in JA/ZH/KO; paste into Word yields matching formatting.
6. **M5**: round-trip corpus equals model; `OpenXmlValidator` clean; Word opens generated files without repair.
7. **M6**: print to Microsoft Print to PDF and compare against PDFsharp export and screen render; mixed-orientation document prints correctly.
8. **M7**: install MSIX on a clean Windows 11 VM; open `.docx` from Explorer; Accessibility Insights FastPass clean; Narrator reads document text; runtime theme switch without visual breakage.

## Open points to settle during M0

- Ribbon: custom command bar (default) vs Fluent.Ribbon, decided by the spike.
- Whether `Quill.Pdf` stays a separate project or merges into `Quill.App`.
- New-document defaults: Aptos vs Calibri, Letter vs A4 by region.
- Store distribution vs sideload (affects signing before M7).
