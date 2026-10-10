# Quill test checklist

Everything the app can do, grouped by area, with what to expect. Run it with `dotnet run --project src/Quill.App`
on Windows 11. Tick what works; for anything broken note the step and what happened (a screenshot helps). Crash
details land in `%LOCALAPPDATA%\Quill\crash.log`; settings in `%LOCALAPPDATA%\Quill\settings.json`.

Items marked **(new)** were written overnight and have not been tried on Windows yet.

## 1. Startup and window

- [ ] App starts, shows an empty page named "Document1", caret blinking, status bar "Page 1 of 1", "0 words".
- [ ] Theme follows Windows (dark/light). All drop-downs, tooltips and the "Aa" menu are readable in dark mode.
- [ ] Resize/move the window, change zoom, close and reopen: size, position, maximized state and zoom come back.
- [ ] Title bar shows `Document1 - Quill`, then `Document1* - Quill` after the first edit.
- [ ] Run `dotnet run --project src/Quill.App -- "C:\path\file.docx"`: the file opens at startup.

## 2. Typing, navigation, selection

- [ ] Type text; it wraps at the right margin and flows onto page 2 when the page is full ("Page 2 of 2").
- [ ] Enter = new paragraph, Shift+Enter = line break, Ctrl+Enter = page break, Tab = tab stop.
- [ ] Backspace/Delete, Ctrl+Backspace / Ctrl+Delete (whole word), delete across a paragraph boundary.
- [ ] Arrows, Ctrl+Left/Right (by word), Home/End, Ctrl+Home/End, Page Up/Down, Up/Down keep the horizontal position.
- [ ] Shift + any of the above extends the selection; selection highlight follows across pages.
- [ ] Mouse: click places the caret, drag selects, double-click selects a word, triple-click a paragraph; dragging
      below the window autoscrolls.
- [ ] Emoji and combining characters: Backspace removes one unit at a time without breaking the text.
- [ ] Dead keys / AltGr (e.g. `´` + `e` on an international layout) produce the accented letter.
- [ ] IME (Japanese/Chinese/Korean, Win+Space to switch): composition text appears at the caret with the candidate
      window next to it; committing inserts the text. **(not yet verified)**

## 3. Undo and redo

- [ ] Ctrl+Z undoes a whole typed word/sentence at once (typing is grouped), Ctrl+Y redoes; the ↶ ↷ buttons too.
- [ ] Undo after formatting, paste, list toggle, picture insert, Page Setup, Properties: each is one step.

## 4. Character formatting (Home tab)

- [ ] Bold/Italic/Underline/Strikethrough/Superscript/Subscript buttons and Ctrl+B/I/U, Ctrl+Shift+Plus, also on headings
      (Calibri Light has no bold face of its own; bold headings use Calibri Bold).
- [ ] With no selection, press Ctrl+B then type: the new text is bold (sticky format); moving the caret cancels it.
- [ ] Font family (editable combo, type a name) and size; A↑ / A↓ (Ctrl+Shift+> / <); Ctrl+] / Ctrl+[ by 1 pt.
- [ ] Font color and highlight pickers (swatch shows the caret's current color); highlight "No color" clears it.
- [ ] Toolbar state mirrors the caret: put the caret in bold text and the B button lights up.
- [ ] Select the whole text of a bullet item (or several items) and grow the font: the bullets grow with it.
      Triple-click a paragraph and press Ctrl+B: only that paragraph changes and the B button lights up.


## 5. Paragraph formatting

- [ ] Align left/center/right/justify (Ctrl+L/E/R/J). Justified lines stretch; the last line does not.
- [ ] Line spacing combo (Ctrl+1 / Ctrl+2 / Ctrl+5), space Before / After combos.
- [ ] Styles combo: Normal, Heading 1-3, Title. Ctrl+Alt+1/2/3, Ctrl+Shift+N. Enter after a heading gives Normal.
- [ ] Increase / decrease indent buttons (⇥ ⇤).

## 6. Change case (new)

- [ ] Select text, click **Aa**: Sentence case, lowercase, UPPERCASE, Capitalize Each Word, tOGGLE cASE.
      Bold/colored runs inside the selection keep their formatting.
- [ ] Shift+F3 with a selection cycles lowercase → UPPERCASE → Capitalize Each Word.
- [ ] Shift+F3 with no selection changes the word at the caret. Ctrl+Z reverts.

## 7. Lists

- [ ] "• List" and "1. List" toggles; Enter continues the list; Enter on an empty item ends it.
- [ ] Tab / Shift+Tab at the start of an item changes the level (bullets change shape, numbers become a./i.).
- [ ] Backspace at the start of an item removes the bullet first, then merges.
- [ ] Deleting an item in the middle renumbers the rest.
- [ ] Save and reopen: lists survive. Open in Word: they are real Word lists.

## 8. Pages, breaks, sections (Insert and Layout tabs)

- [ ] Page Break (Ctrl+Enter) starts a new page; Section Break starts a new section on the next page.
- [ ] Layout > Orientation toggles portrait/landscape for the current section; Margins Normal/Narrow/Moderate.
- [ ] Page Setup...: paper size (Letter/A4/...), orientation, margins, header/footer distance, "Apply to whole
      document", "Different first page", "Different odd and even pages". Invalid values are rejected.
- [ ] Keep-with-next: a heading at the bottom of a page moves to the next page with its paragraph.
- [ ] Widow/orphan control: a paragraph never leaves a single line alone at the top or bottom of a page.

## 9. Headers and footers

- [ ] Double-click the top or bottom margin, or Insert > Header / Footer: the body dims, blue guides appear.
- [ ] Type in the header; Insert > Page Number and Page Count insert fields that show the right numbers on every
      page; "Page X of Y" updates when pages are added.
- [ ] Esc or Close Header/Footer (or double-click the body) returns to the body.
- [ ] With "Different first page" / "Different odd and even" set, each variant is edited and shown separately.
- [ ] A tall header pushes the body text down.

## 10. Pictures (new)

- [ ] Insert > Picture...: PNG, JPEG, GIF, BMP, TIFF insert at the caret at their natural size; a picture wider
      than the text column is shrunk to fit. WebP/ICO are converted to PNG.
- [ ] Text wraps around the line with the picture; the line grows to the picture's height; the caret moves across
      it as one character; Backspace/Delete remove it; Ctrl+Z brings it back.
- [ ] Clicking a picture switches the command bar to a **Picture** tab (Size, Original Size, Fit Width, 50%, 200%,
      alignment, Replace, Delete); clicking text switches back to the tab you were on.
- [ ] Click on the picture: it becomes selected (highlighted). Insert > Picture Size...: width/height in inches
      or cm, "Lock aspect ratio" keeps proportions while typing, "Original Size" resets. OK resizes, Ctrl+Z undoes.
- [ ] Paste a screenshot (Win+Shift+S, then Ctrl+V in Quill): it is inserted as a PNG.
- [ ] Drag a picture file from Explorer onto the window: it is inserted at the caret (a .docx still opens).
- [ ] Copy a picture with text (Ctrl+C), paste elsewhere in the document and into a *new* document (Ctrl+N then
      Ctrl+V): the picture comes along.
- [ ] Save, reopen: pictures are back at the same size. Open the file in Word: pictures show correctly.
- [ ] Open a Word document with pictures: inline pictures show; floating pictures are placed in the text (status
      bar says so); EMF/WMF clip art shows as a grey box but is preserved on save.
- [ ] Pictures appear in Print Preview, on paper and in the exported PDF.
- [ ] Pictures in a header or footer work too.

## 11. Symbol and Date & Time (new)

- [ ] Insert > Symbol...: clicking a symbol inserts it; the window stays open for more; typing a hex code
      (e.g. `2192` or `U+1F600`) previews it and Insert inserts it; Close closes.
- [ ] Insert > Date & Time...: pick a format (double-click or Insert); the text is inserted at the caret.

## 12. Find and replace

- [ ] Ctrl+F opens Find pre-filled with the selected word; Enter / Find Next / F3 moves forward, Shift+F4 back,
      wrapping around with a note; the match is selected and scrolled into view.
- [ ] Match case and Whole word options.
- [ ] Ctrl+H: Replace replaces the current match and finds the next; Replace All reports the count; Ctrl+Z undoes.
- [ ] Close button and Esc close the window; the window can stay open while editing (non-modal).

## 13. Clipboard

- [ ] Copy/paste inside Quill keeps formatting, lists and pictures.
- [ ] Copy from Quill and paste into Word or WordPad: fonts, bold/italic/colors, alignment and list bullets arrive.
- [ ] Copy from Word/browser and paste into Quill: formatting arrives (RTF); Ctrl+Shift+V pastes plain text.
- [ ] Ctrl+X cuts. The Cut/Copy/Paste buttons do the same.

## 14. View tab and status bar

- [ ] Zoom 50-200% buttons, slider, +/- buttons, Ctrl+Plus/Minus, Ctrl+mouse wheel (zooms around the pointer);
      Fit Page and Fit Width.
- [ ] ¶ toggle shows paragraph marks, tab arrows, space dots and break markers; the setting is remembered.
- [ ] Theme: System / Light / Dark switches instantly and is remembered **(new)**.
- [ ] Word Count (Ctrl+Shift+G) shows pages, words, characters with/without spaces, paragraphs, lines; with a
      selection it counts the selection only.
- [ ] Go To Page (Ctrl+G) scrolls to the page and puts the caret there; out-of-range numbers are rejected.
- [ ] Select All (Ctrl+A). Status bar: "Page X of Y" follows the caret, the word count updates while typing.

## 15. File tab

- [ ] New (Ctrl+N) asks to save when there are unsaved changes (Yes / No / Cancel).
- [ ] Open (Ctrl+O), drag-and-drop of a .docx, Recent list (last 10; a missing file is reported and removed).
- [ ] Save (Ctrl+S) and Save As (F12) write .docx; the file opens in Word without a repair prompt.
- [ ] Opening a document with unsupported content shows "Opened with limitations: ..." in the status bar; saving
      over the original warns that those parts will be lost.
- [ ] Properties... edits title, author, subject, comments; shows created/modified dates and statistics; the
      values are stored in the .docx (visible in Word's File > Info) and in the PDF metadata **(new)**.
- [ ] Print (Ctrl+P): printer dialog with page range; "Microsoft Print to PDF" works. Landscape sections print
      in landscape.
- [ ] Print Preview: pages exactly as printed, Fit Page / Fit Width, scrolling, Close.
- [ ] Export PDF...: the PDF opens in Edge/Acrobat; text is selectable; line breaks match the screen; a highlight
      over several words is one continuous band.
- [ ] Shortcuts (F1) lists all keys; About shows the version **(new)**.
- [ ] Exit / closing the window asks to save when dirty.

## 16. Autosave and recovery

- [ ] Type something, wait ~2 minutes: status bar shows "Autosaved at HH:MM".
- [ ] Kill the app from Task Manager with unsaved changes, start it again: it offers to recover; Yes restores the
      text (title shows "Recovered unsaved changes").
- [ ] A clean exit leaves no recovery prompt next time.

## 17. .docx fidelity

- [ ] Open a real Word document (styles, theme fonts like Aptos, colored headings, fields, a table, headers and
      footers, lists, pictures). Text and formatting look like Word; tables show as grey placeholder boxes.
- [ ] Save it under a new name and open that in Word: content, styles, lists, pictures, headers/footers are intact;
      the table is still there (preserved opaquely); no repair prompt.

## 18. Display

- [ ] Text is crisp at 100%, 125%, 150% display scaling and at every zoom level.
- [ ] Moving the window to a monitor with a different DPI re-renders correctly.
- [ ] Switching Windows between light and dark while the app runs updates the chrome; the page stays white.

## Known limitations (expected, not bugs)

- Tables are shown as placeholders and preserved on save, not editable.
- No spell check, no hyperlinks (link text is kept, the link is dropped), no footnotes, comments, track changes
  or equations (dropped with a warning), single column only.
- Floating pictures become inline; EMF/WMF pictures show as grey boxes; pasting a picture *into* another app
  gives its text only.
- Pasting RTF from other apps brings text formatting but not pictures.
