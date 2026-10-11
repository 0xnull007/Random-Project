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

## 1b. Ribbon (new)

- [ ] The command bar looks like Word's classic ribbon: tabs File, Home, Insert, Layout, References, Review, View;
      each tab shows groups with their names underneath (Home: Undo, Clipboard, Font, Paragraph, Styles, Editing),
      large icon-and-label buttons for the main commands and rows of small icon buttons for the rest.
- [ ] Icons render as Fluent glyphs (not boxes) in light and dark mode: Bold/Italic/Underline/Strikethrough, the
      alignments, bullets, grow/shrink font, clear formatting, cut/copy/paste, find/replace, undo/redo.
- [ ] Margins, Columns, Breaks (Layout) and Delete (Table Layout) open drop-down menus under the button.
- [ ] Hovering any button shows a tooltip with its shortcut; everything that was on the old single-row bar is still
      reachable (Format Painter and Cut/Copy sit beside Paste; Font... and Paragraph... at the end of their groups;
      Go To Page and Select All under Editing; Word Count and Spelling under Review > Proofing).
- [ ] Narrow the window: groups wrap to a second row instead of being cut off; widen it again and they return.
- [ ] Selecting a picture shows the **Picture Format** tab and switches to it; placing the caret in a table shows the
      **Table Layout** tab (not switched to automatically); leaving either hides the tab again.
- [ ] Select text with the mouse (drag, double-click or Shift+click): Word's mini toolbar pops up just above the
      selection with font, size, grow/shrink, B/I/U, highlight, font colour, bullets, numbering, centre and Format
      Painter, readable in dark mode; it closes when you type, click elsewhere, scroll or collapse the selection **(new)**.
- [ ] View > Ruler shows a vertical ruler down the left edge as well: top and bottom margins shaded, the text area
      white, inch or centimetre ticks counted from the top margin; it follows scrolling and zoom **(new)**.
- [ ] The chevron at the right end of the tab strip (or Ctrl+F1) collapses the ribbon to its tabs, like Word's
      "Show tabs only": clicking a tab opens it, clicking back into the text hides it again; the choice is remembered
      across restarts **(new)**.

## 2. Typing, navigation, selection

- [ ] Press Insert: "Overtype" appears in the status bar and typing replaces the characters after the caret (at a
      paragraph end it appends); Insert again returns to inserting; Shift+Insert and Ctrl+Insert still paste and copy **(new)**.
- [ ] Type text; it wraps at the right margin and flows onto page 2 when the page is full ("Page 2 of 2").
- [ ] Enter = new paragraph, Shift+Enter = line break, Ctrl+Enter = page break, Tab = tab stop.
- [ ] Backspace/Delete, Ctrl+Backspace / Ctrl+Delete (whole word), delete across a paragraph boundary.
- [ ] Arrows, Ctrl+Left/Right (by word), Home/End, Ctrl+Home/End, Page Up/Down, Up/Down keep the horizontal position.
- [ ] Shift + any of the above extends the selection; selection highlight follows across pages.
- [ ] Mouse: click places the caret, drag selects, double-click selects a word, triple-click a paragraph; dragging
      below the window autoscrolls.
- [ ] Drag and drop: select text, press inside the selection and drag it elsewhere (a drop cursor follows the
      mouse): the text moves there and stays selected; hold Ctrl to copy instead; drag into WordPad to copy it out;
      click inside a selection without dragging just places the caret **(new)**.
- [ ] Status bar shows "12 of 345 words" while text is selected **(new)**. Print offers "Current page" **(new)**.
      File > New Window opens a second window for another document **(new)**.
- [ ] Emoji and combining characters: Backspace removes one unit at a time without breaking the text.
- [ ] Dead keys / AltGr (e.g. `´` + `e` on an international layout) produce the accented letter.
- [ ] IME (Japanese/Chinese/Korean, Win+Space to switch): composition text appears at the caret with the candidate
      window next to it; committing inserts the text. **(not yet verified)**

## 2b. AutoCorrect (new)

- [ ] Type `"hello"` and `it's`: curly quotes and a curly apostrophe appear.
- [ ] Type `(c)`, `(r)`, `(tm)`, `...`, `-->`: they become © ® ™ … →. Type `1/2 ` (with the space): ½.
- [ ] Type `word--word ` : the hyphens become an em dash; `word -- word ` gives an en dash.
- [ ] Type `hello. this is` : "this" is capitalized after the sentence end; the first word of a paragraph too;
      `e.g.` and words with capitals or dots are left alone. Ctrl+Z undoes just the capitalization.
- [ ] Type `* ` or `1. ` at the start of a paragraph: it becomes a bullet or numbered item (Ctrl+Z reverts).
- [ ] Nothing is rewritten inside a hyperlink. File > Options turns each rule off; the autosave interval and the
      measurement unit (inches/centimeters) can be changed there too.

## 2c. Spelling (new)

- [ ] Review > Spelling (F7) opens Word's Spelling dialog: it selects each misspelling from the caret on, shows the
      sentence with the word in red and suggestions; Change/Change All replace, Ignore Once skips, Ignore All and Add
      to Dictionary stop the word being flagged (squiggles update too); it wraps to the start and ends with "Spelling
      check complete" **(new)**.
- [ ] Misspelled words get a red wavy underline shortly after you finish typing them (the word at the caret is
      not flagged until you move on). View > Spelling toggles it; it is remembered.
- [ ] Right-click a flagged word: suggestions appear at the top of the menu; choosing one replaces the word.
      Ignore All stops flagging the word for this session; Add to Dictionary adds it to the Windows dictionary
      (shared with Edge/Word) and the underline disappears everywhere.
- [ ] Hyperlink text, numbers and pictures are not flagged. Text marked with another language (e.g. a Word
      document in German) is checked with that language when Windows has it installed.
- [ ] Zoom in/out and scroll: underlines stay under the words; pages further down are checked when they appear.

## 3. Undo and redo

- [ ] Ctrl+Z undoes a whole typed word/sentence at once (typing is grouped), Ctrl+Y redoes; the ↶ ↷ buttons too.
- [ ] Undo after formatting, paste, list toggle, picture insert, Page Setup, Properties: each is one step.

## 4. Character formatting (Home tab)

- [ ] The Styles box shows each style's name in its own font, weight, slant and colour (Heading 1 big and blue,
      Title bigger), like Word's gallery; New Style... creates a style from the caret paragraph's formatting, lists
      it in the box and applies it; a duplicate name is refused **(new)**.
- [ ] Bold/Italic/Underline/Strikethrough/Superscript/Subscript buttons and Ctrl+B/I/U, Ctrl+Shift+Plus, also on headings
      (Calibri Light has no bold face of its own; bold headings use Calibri Bold).
- [ ] With no selection, press Ctrl+B then type: the new text is bold (sticky format); moving the caret cancels it.
- [ ] Font family (editable combo, type a name) and size; A↑ / A↓ (Ctrl+Shift+> / <); Ctrl+] / Ctrl+[ by 1 pt.
- [ ] Font color and highlight pickers (swatch shows the caret's current color); highlight "No color" clears it.
- [ ] Caret inside a word (no selection), Ctrl+B: the whole word turns bold, like Word. At the end of a word it
      only affects what you type next.
- [ ] Clear (Ctrl+Space) removes bold/color/size from the selection but keeps the paragraph style.
- [ ] Format Painter: put the caret in bold red text, click Format Painter, then drag over other text: it becomes
      bold red. Double-click Format Painter to keep painting; Esc stops. Ctrl+Shift+C / Ctrl+Shift+V do the same
      from the keyboard.
- [ ] Toolbar state mirrors the caret: put the caret in bold text and the B button lights up.
- [ ] Select the whole text of a bullet item (or several items) and grow the font: the bullets grow with it.
      Triple-click a paragraph and press Ctrl+B: only that paragraph changes and the B button lights up.


- [ ] Font... (Ctrl+D): family, style, size, underline style, color, effects (strike, double strike, super/sub,
      small caps, all caps, hidden) with a live preview; OK applies only what you changed **(new)**.
- [ ] Paragraph...: alignment, left/right indent, first-line or hanging indent, before/after, line spacing
      (single, 1.5, double, at least, exactly, multiple), widow/orphan, keep with next, keep lines, page break
      before **(new)**.
- [ ] Right-click in the text: context menu with Cut/Copy/Paste, Font, Paragraph, Bullets/Numbering, Link entries
      (Edit/Open/Remove when on a link), Picture Size when on a picture, Select All. Right-click outside the
      selection moves the caret first **(new)**.

## 5. Paragraph formatting

- [ ] Home > Paragraph > shading swatch colours the background of the selected paragraphs between their indents;
      "Automatic" clears it. The borders button (⊞) toggles Bottom/Top/Left/Right lines, Outside Borders draws a box,
      No Border removes them; borders sit just outside the text, double style shows two lines; print, PDF and Word
      show the same **(new)**.
- [ ] Align left/center/right/justify (Ctrl+L/E/R/J). Justified lines stretch; the last line does not.
- [ ] Line spacing combo (Ctrl+1 / Ctrl+2 / Ctrl+5), space Before / After combos.
- [ ] Styles combo: Normal, Heading 1-3, Title. Ctrl+Alt+1/2/3, Ctrl+Shift+N. Enter after a heading gives Normal.
- [ ] Home > Modify Style...: change the font/paragraph formatting of the current style (preview and summary); every
      paragraph in that style updates; "Update to Match Selection" copies the caret paragraph's formatting into the
      style; the style list refreshes when a Word document brings its own styles **(new)**.
- [ ] Home > Number Headings: Heading 1-3 paragraphs show 1, 1.1, 1.1.1 in front; clicking again removes the
      numbers; the numbering survives save/reopen and shows in Word **(new)**.
- [ ] Export PDF: the PDF has bookmarks for the headings (nested) and clickable web/mail links **(new)**.
- [ ] Increase / decrease indent buttons (⇥ ⇤).

## 6. Change case (new)

- [ ] Select text, click **Aa**: Sentence case, lowercase, UPPERCASE, Capitalize Each Word, tOGGLE cASE.
      Bold/colored runs inside the selection keep their formatting.
- [ ] Shift+F3 with a selection cycles lowercase → UPPERCASE → Capitalize Each Word.
- [ ] Shift+F3 with no selection changes the word at the caret. Ctrl+Z reverts.

## 7. Lists

- [ ] Home > Paragraph > 1.a.i (Multilevel List) offers 1. a. i. / 1 1.1 1.1.1 / I. A. 1. a) (1) and three bullet
      outlines: the selected paragraphs join the list, Tab and Shift+Tab move between levels and show the outline's
      markers, and picking another outline reformats the whole list; one undo step **(new)**.
- [ ] "• List" and "1. List" toggles; Enter continues the list; Enter on an empty item ends it.
- [ ] Tab / Shift+Tab at the start of an item changes the level (bullets change shape, numbers become a./i.).
- [ ] Backspace at the start of an item removes the bullet first, then merges.
- [ ] Deleting an item in the middle renumbers the rest.
- [ ] Right-click in a list > List: Restart Numbering at 1, Continue Previous List, and numbering (1. 1) a. a) A.
      i. I.) or bullet (• ○ ▪ – ✓ ➢) styles; changes survive save/reopen and show the same in Word **(new)**.
- [ ] Save and reopen: lists survive. Open in Word: they are real Word lists.

## 8. Pages, breaks, sections (Insert and Layout tabs)

- [ ] Layout > Line Numbers > Continuous numbers every body line of the section in the left margin (Restart Each Page
      starts at 1 on every page; Suppress for Current Paragraph leaves a paragraph out; None removes them); numbers
      print, export to PDF and show in Word after a save **(new)**.
- [ ] Insert > Pages > Cover Page puts a title page in front (Title, Subtitle, author from Options, today's date,
      then a page break) with the title selected so typing replaces it; the cover shows no header or footer
      (Different First Page is switched on). Blank Page inserts an empty page at the caret **(new)**.
- [ ] Home > Paragraph > A↓Z sorts the selected paragraphs (or all, when nothing is selected) A to Z or Z to A,
      numerically when they all start with numbers; formatting travels with each paragraph; one undo step **(new)**.
- [ ] Page Break (Ctrl+Enter) starts a new page; Section Break starts a new section on the next page.
- [ ] Layout > Orientation toggles portrait/landscape for the current section; Margins Normal/Narrow/Moderate.
- [ ] Layout > Columns One/Two/Three: the section's text flows down the first column and then the next; Column
      Break (Ctrl+Shift+Enter) jumps to the next column; a page break skips the remaining columns; Word shows the
      same columns when the file is opened there **(new)**.
- [ ] Page Setup...: paper size (Letter/A4/...), orientation, margins, header/footer distance, "Apply to whole
      document", "Different first page", "Different odd and even pages". Invalid values are rejected.
- [ ] Page Setup also sets the page number format (1 / i / I / a / A) and "Start at" (empty continues from the
      previous section), plus columns and column spacing; page-number fields follow **(new)**.
- [ ] Keep-with-next: a heading at the bottom of a page moves to the next page with its paragraph.
- [ ] Widow/orphan control: a paragraph never leaves a single line alone at the top or bottom of a page.

## 8b. Design tab: page colour and page borders (new)

- [ ] Design > Page Color: pick a colour and every page is tinted on screen (dark mode too); "Automatic" clears it;
      Print Preview, printing and PDF export stay white, as in Word; the colour survives save and reload and shows in
      Word (which asks for "display background" only when the setting is missing, so it should just appear).
- [ ] Design > Page Borders: Box, Double, 1½ pt, blue, 18 pt from the page edge, whole document: every page gets a
      double blue frame; "Text" measures from the text area instead; "This section" limits it; None removes it;
      undo reverts in one step; the border prints and exports to PDF; Word shows the same frame.
- [ ] Design > Document Formatting repeats the style picker, Modify Style and Number Headings from Home/References.
- [ ] Design > Watermark > DRAFT: light-grey "DRAFT" runs diagonally across every page behind the text, on screen, in
      Print Preview and in the PDF; Custom Watermark lets you type the text and pick a colour; Remove Watermark
      clears it; after save and reload Word shows the same watermark (it is written as Word's own WordArt shape in the
      header) and opening a Word document with a watermark shows it here **(new)**.

## 9. Headers and footers

- [ ] Entering a header or footer switches the ribbon to a **Header & Footer** tab (Header, Footer, Page Number,
      Page Count, Date & Time, Link to Previous, Different First Page, Different Odd & Even Pages, Close); leaving
      returns to the previous tab **(new)**.
- [ ] In a document with two sections, edit the header on a page of section 2: Link to Previous is on; switch it
      off and type: only section 2's header changes; switch it on again and section 2 shows section 1's header
      once more; the toggle is disabled in the first section **(new)**.
- [ ] Different First Page / Different Odd & Even Pages on the tab behave like the Page Setup options and undo in
      one step **(new)**.
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
- [ ] Clicking a picture switches the ribbon to a **Picture Format** tab (Size, Original Size, Fit Width, 50%, 200%,
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

## 10b. Tables (new)

- [ ] Insert > Table...: enter 3 columns and 2 rows; a bordered grid as wide as the text column appears at the
      caret (before the paragraph when the caret is at its start, after it at its end, splitting it in the middle)
      and the caret is in the first cell. The Table tab appears while the caret is inside a table and disappears
      when it leaves (if it was the selected tab, the previous tab comes back).
- [ ] Type in cells; rows grow with their content; Tab in the last cell adds a row; the Table tab's Row Above/Below
      and Column Left/Right add rows and columns (the table keeps its width, other columns shrink); Delete Row,
      Delete Column and Delete Table remove them; deleting the only row removes the table and leaves the paragraphs
      around it intact; Borders hides and shows the grid lines; Align Left/Center/Right align the cell paragraph.
- [ ] Right-click inside a table: Insert Row Below, Insert Column Right, Delete Row/Column/Table are offered only there.
- [ ] Point at a vertical cell border: the cursor becomes a horizontal resize arrow; drag it and the two columns on
      either side trade width live (the right edge widens or narrows the last column); release and one Ctrl+Z undoes
      the whole drag. Dragging the same border in Word's file keeps the other columns as they were **(new)**.
- [ ] Table tab > Header Row on the first row, then add rows until the table runs onto a second page: the header row
      is drawn again at the top of the continuation (not editable there); Header Row off stops that **(new)**.
- [ ] Table tab > shading swatch: pick a colour and the caret's cell is filled (text stays readable); "Automatic"
      clears it; the swatch shows the current cell's colour; saved files keep the shading **(new)**.
- [ ] Table Layout > Cell Size: Distribute Columns equalises the columns, AutoFit Window stretches the table to the
      text column; Alignment > Table Position centres or right-aligns a narrow table; Data > Convert to Text turns the
      table into tab-separated paragraphs and Insert > Tables > Text to Table turns tab- (or comma-) separated
      paragraphs back into a table; all survive save and reload and match Word **(new)**.
- [ ] Table Layout > Table Styles: Grid Table Blue gives a blue bold-white header row, light-blue banded rows and
      borders; Plain Table takes it all away again; the look survives save and reload and shows in Word (as direct
      shading, not a table style) **(new)**.
- [ ] Table tab > Merge Right joins the caret's cell with its right neighbour (contents one after the other), Merge
      Down joins it with the cell beneath (one tall cell without the inner border), Split Cell undoes either; merged
      cells survive save and reload and look the same in Word **(new)**.
- [ ] Headings, comments and footnotes inside table cells behave like elsewhere: the heading appears in the
      navigation pane and the table of contents, the comment pane jumps to the commented cell text, Esc from a
      footnote returns to the cell, and word count includes cell text **(new)**.
- [ ] Open a Word document whose header holds a table (a typical letterhead): the header table is drawn and its
      cells can be edited in header mode **(new)**.
- [ ] Undo (Ctrl+Z) reverts each table command in one step.
- [ ] Save as .docx and open in Word: the inserted table has its borders, equal columns and cell text; no repair prompt.
- [ ] Export to PDF and print preview show the table like the page does.

## 11. Link, Symbol and Date & Time (new)

- [ ] Insert > Date & Time with "Update automatically" inserts a DATE or TIME field that shows the current value (check
      the next day, or in Word, where it is a real DATE field); without the box it is plain text as before. Insert >
      Text > Quick Parts > Title/Author/Subject/Comments insert document property fields that follow File >
      Properties and update with F9; Word shows them as AUTHOR/TITLE fields **(new)**.
- [ ] Insert > Bookmark: select text, type a name (letters, digits, underscores), Add; the dialog lists bookmarks,
      Go To selects one, Delete removes it; Ctrl+Click a link to "#name" jumps to the bookmark **(new)**.
- [ ] Insert > Cross-reference: pick a heading, bookmark, figure or table caption and insert its text or page
      number, as a link or plain; edit the heading or move it to another page and press F9 (References > Update
      Fields): the reference text and page number follow; saved files keep bookmarks and REF/PAGEREF fields and Word
      shows the same **(new)**.
- [ ] Insert > Symbol...: clicking a symbol inserts it; the window stays open for more; typing a hex code
      (e.g. `2192` or `U+1F600`) previews it and Insert inserts it; Close closes.
- [ ] Insert > Link... (Ctrl+K) with text selected makes it a blue underlined link; with nothing selected it inserts
      the text. Hover shows the address; Ctrl+Click opens it in the browser. Ctrl+K on a link edits it; Remove Link
      turns it back into plain text. Links survive save/reopen and open in Word. Word documents with links
      (including HYPERLINK fields) show them as links.
- [ ] Insert > Table of Contents: a "Contents" heading plus one line per Heading 1-3 paragraph with dot leaders and
      page numbers; clicking it again refreshes it in place; it saves as toc 1-3 styles that Word recognizes **(new)**.
- [ ] Insert > Footnote (Ctrl+Alt+F): a superscript number appears at the caret and the caret jumps to the note at
      the bottom of the page (separator line, small text); type the note, Esc returns to the text. Notes are
      numbered in order, move with their reference to the next page, round-trip through .docx and show in Word.
      Opening a Word file with footnotes shows them **(new)**.
- [ ] Comments: select text, Insert > Comment (Ctrl+Alt+M): the text gets a light orange background, the Comments
      pane opens on the right with a card (your name from Options, date) and the caret is in its text box; type the
      comment (saved when you leave the box); clicking a card selects its text; ✕ deletes it. Word documents with
      comments show them; saved comments open in Word's review pane **(new)**.
- [ ] Insert > Endnote (Ctrl+Alt+D): a superscript roman numeral at the caret; the note text collects after the
      last paragraph of the document under a separator line; Esc returns; round-trips through .docx **(new)**.
- [ ] Insert > Date & Time...: pick a format (double-click or Insert); the text is inserted at the caret.

## 11b. Track changes (new)

- [ ] Review > Track Changes (or Ctrl+Shift+E): the toggle lights up and "Track Changes" appears in the status bar.
      Type: new text is underlined in a colour; select text and press Delete: it stays, struck through in the same
      colour; type over a selection: the old text is struck and the new text follows underlined; Backspace over
      text you just typed removes it outright; Backspace over struck text only steps over it.
- [ ] Options > Author sets the name recorded; a document edited under two names shows two colours.
- [ ] Word count in the status bar and Word Count dialog ignore struck (deleted) text.
- [ ] Review > Next / Previous select each change in turn and wrap around; Accept keeps the insertion or removes the
      deleted text under the caret and jumps to the next change; Reject does the opposite; Accept All / Reject All
      clear every mark (undo brings the marks back).
- [ ] Turn tracking off: typing is normal again, even right next to tracked text; existing marks stay.
- [ ] Save as .docx with tracking on and open in Word: Word shows the same insertions and deletions with the author
      and date, Track Changes is on, and no repair prompt appears. Open a Word document with tracked changes: its
      changes appear as markup and can be accepted or rejected here.
- [ ] Cut (Ctrl+X) while tracking strikes the text instead of removing it; Paste marks the pasted text inserted.

## 11c. Captions (new)

- [ ] Click a picture, References > Insert Caption: the dialog offers Figure/Table/Equation (or a label of your own),
      Below/Above, a text box and a preview "Figure 1"; OK adds "Figure 1: text" under the picture in the Caption
      style (9 pt, blue-grey) with the caret at its end.
- [ ] With the caret in a table the dialog defaults to "Table" and "Above"; the caption lands above the table.
- [ ] Add a caption above an earlier figure: the later captions renumber on screen at once (2, 3...); undo renumbers back.
- [ ] Save and open in Word: captions are SEQ fields with the right numbers and the Caption style; F9 in Word keeps
      them. Open a Word document with captions: the numbers follow document order here too.
- [ ] Copy a caption and paste into Notepad: the current number appears in the text.
- [ ] References > Table of Figures (Figures / Tables / Equations): a list of that series' captions with dot-leader page
      numbers appears at the caret in the "table of figures" style; inserting again replaces it with fresh numbers;
      with no captions a hint line is inserted instead **(new)**.

## 12. Find and replace

- [ ] Ctrl+F opens Find pre-filled with the selected word; Enter / Find Next / F3 moves forward, Shift+F4 back,
      wrapping around with a note; the match is selected and scrolled into view.
- [ ] Match case and Whole word options. While the Find window is open every match is highlighted in yellow and the
      status shows the match count; the highlights go away when it closes **(new)**.
- [ ] Ctrl+H: Replace replaces the current match and finds the next; Replace All reports the count; Ctrl+Z undoes.
- [ ] Close button and Esc close the window; the window can stay open while editing (non-modal).

## 13. Clipboard

- [ ] Select from a paragraph above a table to one below it and Copy, then Paste elsewhere: the table arrives with
      its cells, widths, shading and header rows, the paragraphs around it join the target as usual; Cut removes the
      table; dragging such a selection moves the table **(new)**.
- [ ] Copy a table in Excel or a web page and paste: it arrives as a real table (cells, column spans, cell colours,
      header cells bold), not tab-separated text; paste as plain text (Ctrl+Alt+V) gives the tab-separated form **(new)**.
- [ ] Copy a table here and paste into Word, Outlook or a browser: it is a table there too (HTML clipboard) **(new)**.
- [ ] Copy/paste inside Quill keeps formatting, lists and pictures.
- [ ] Copy from Quill and paste into Word or WordPad: fonts, bold/italic/colors, alignment and list bullets arrive.
- [ ] Copy from a web page (Chrome/Edge) and paste: headings, bold/italic, links, colors, lists and table rows
      arrive; copy from Quill and paste into Gmail/a browser editor: formatting and links arrive **(new)**.
- [ ] Copy from Word/browser and paste into Quill: formatting arrives (RTF); Ctrl+Alt+V pastes plain text.
- [ ] Ctrl+X cuts. The Cut/Copy/Paste buttons do the same.

## 14. View tab and status bar

- [ ] Navigation pane > Pages: thumbnails of every page (page colour, borders, watermark included) refresh a moment
      after you edit; clicking one scrolls to that page **(new)**.
- [ ] Navigation pane: the search box at the top highlights every match in the document and lists them under a
      Results tab with their context; clicking a result or pressing Enter (Shift+Enter backwards) selects the match;
      Esc clears; the list refreshes as you edit; the Headings tab is still there **(new)**.
- [ ] Zoom 50-200% buttons, slider, +/- buttons, Ctrl+Plus/Minus, Ctrl+mouse wheel (zooms around the pointer);
      Fit Page and Fit Width.
- [ ] ¶ toggle shows paragraph marks, tab arrows, space dots and break markers; the setting is remembered.
- [ ] Theme: System / Light / Dark switches instantly and is remembered **(new)**.
- [ ] View > Ruler: the ruler above the page shows margins (grey), the text column (white), inch/cm ticks, the
      indent markers and tab stops of the caret paragraph, and follows zoom and horizontal scrolling. Drag the top
      triangle (first line), the bottom triangle (hanging) or the box (both) and the right triangle; click in the
      ruler to add a left tab stop (Tab then jumps there); drag a tab stop off the ruler to remove it **(new)**.
- [ ] View > Navigation opens a side pane listing the headings (indented by level); clicking one jumps there and the
      current heading is highlighted as you move the caret; the pane state is remembered **(new)**.
- [ ] Word Count (Ctrl+Shift+G) shows pages, words, characters with/without spaces, paragraphs, lines; with a
      selection it counts the selection only.
- [ ] Go To Page (Ctrl+G) scrolls to the page and puts the caret there; out-of-range numbers are rejected.
- [ ] Select All (Ctrl+A). Status bar: "Page X of Y" follows the caret, the word count updates while typing.

## 15. File tab

- [ ] New (Ctrl+N) asks to save when there are unsaved changes (Yes / No / Cancel).
- [ ] Open (Ctrl+O), drag-and-drop of a .docx, Recent list (last 10; a missing file is reported and removed).
- [ ] Open (or drop) a .rtf or .txt file: it is imported into a new document ("Imported ..." in the status bar);
      Save As offers .docx, .rtf and .txt; .rtf/.txt are exports (the open document stays a .docx) **(new)**.
- [ ] Ctrl+Q resets paragraph formatting to the style; Ctrl+Shift+L toggles bullets; Ctrl+M / Ctrl+Shift+M change
      the indent; Ctrl+Shift+8 toggles formatting marks **(new)**.
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
      footers, lists, pictures). Text, formatting and tables look like Word.
- [ ] Open a Word document with tables: they are drawn with their columns, row heights, merged cells, cell shading
      and borders (Table Grid style); text in cells wraps; long tables continue on the next page between rows.
- [ ] Click inside a cell: the caret lands in that cell (not the first cell of the row); type, press Enter for a
      second paragraph in the cell, Backspace joins it again; Backspace at the start of a cell and Delete at its end
      do nothing (cells never merge); the row grows with its content and the table below moves down **(new)**.
- [ ] Tab selects the contents of the next cell (row by row), Shift+Tab the previous one; Left/Right arrows cross
      from one cell into the next and out of the table; Up/Down move into the cell beneath/above (same column) and
      out of the table at its last/first row; dragging a selection stays inside one cell **(new)**.
- [ ] Find (Ctrl+F) also finds text inside table cells **(new)**.
- [ ] Save: an untouched table is written exactly as it was read; an edited table is rewritten with its widths,
      merged cells, shading and borders intact and opens in Word without a repair prompt **(new)**.
- [ ] Save it under a new name and open that in Word: content, styles, lists, pictures, headers/footers are intact;
      the table is still there; no repair prompt.

## 18. Display

- [ ] Text is crisp at 100%, 125%, 150% display scaling and at every zoom level.
- [ ] Moving the window to a monitor with a different DPI re-renders correctly.
- [ ] Switching Windows between light and dark while the app runs updates the chrome; the page stays white.

## 19. Accessibility (new)

- [ ] Narrator (Win+Ctrl+Enter): the document is announced as a document; arrow keys read characters, words and
      lines; the selection is reported when it changes.
- [ ] Accessibility Insights for Windows (FastPass): the editor appears as a Document control with the Text
      pattern (text, selection, visible ranges, bounding rectangles, font attributes); toolbar buttons have names
      (taken from their tooltips).

## Known limitations (expected, not bugs)

- Tables: no table styles and no selection spanning several cells yet (merge works cell by cell); unedited tables
  are saved exactly as read, edited and inserted ones are written as plain grids with their widths, shading, merges
  and header rows.
- Track changes records insertions and deletions of text only: formatting changes, paragraph marks, moved
  pictures and tables are not tracked, and deleted text is always shown (Word's "All Markup").
- No equations (dropped with a warning).
- Floating pictures become inline; EMF/WMF pictures show as grey boxes; pasting a picture *into* another app
  gives its text only.
- Pasting RTF from other apps brings text formatting but not pictures.
