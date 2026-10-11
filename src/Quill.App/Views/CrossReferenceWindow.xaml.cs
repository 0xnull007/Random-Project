using System.Windows;
using System.Windows.Controls;
using Quill.Core.Editing;
using Quill.Core.Model;
using Quill.Core.Text;

namespace Quill.App.Views;

/// <summary>Word's Cross-reference dialog: pick a heading, bookmark or caption and insert its text or page number.</summary>
public partial class CrossReferenceWindow : Window
{
    private readonly EditingSession _session;
    private readonly List<(string Text, StoryId Story, BlockPath Path, string? Bookmark)> _targets = [];

    public CrossReferenceWindow(EditingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        InitializeComponent();
        _session = session;
        KindBox.SelectedIndex = 0;
        TypeBox.SelectedIndex = 0;
    }

    /// <summary>The chosen target: a bookmark name (created for a heading or caption when needed), or null when cancelled.</summary>
    public string? BookmarkName { get; private set; }

    public bool PageNumber => KindBox.SelectedIndex == 1;

    public bool AsLink => LinkBox.IsChecked == true;

    /// <summary>Position of the target paragraph (for the page number), null for plain bookmarks.</summary>
    public TextPosition? TargetPosition { get; private set; }

    private void OnTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        _targets.Clear();
        TargetList.Items.Clear();
        Document document = _session.Document;
        switch (TypeBox.SelectedIndex)
        {
            case 0:
                for (int s = 0; s < document.Sections.Count; s++)
                {
                    foreach ((BlockPath path, Paragraph paragraph) in BlockWalker.Paragraphs(document.Sections[s].Body))
                    {
                        if (_session.Resolver.ResolveParagraph(paragraph).OutlineLevel is { } level && level < 9 && paragraph.FlatText.Trim().Length > 0)
                        {
                            _targets.Add((new string(' ', level * 2) + paragraph.FlatText.Trim(), StoryId.Body(s), path, null));
                        }
                    }
                }

                HintText.Text = "Headings get a hidden bookmark so the reference follows them.";
                break;
            case 1:
                foreach (string name in _session.BookmarkNames)
                {
                    _targets.Add((name, StoryId.Body(0), BlockPath.Of(0), name));
                }

                HintText.Text = _targets.Count == 0 ? "No bookmarks yet: select text and use Insert > Bookmark." : string.Empty;
                break;
            default:
                string label = TypeBox.SelectedIndex == 2 ? "Figure" : "Table";
                foreach (CaptionEntry caption in TableOfFigures.Captions(document, label))
                {
                    _targets.Add((caption.Text, caption.Story, caption.Path, null));
                }

                HintText.Text = _targets.Count == 0 ? "No " + label.ToLowerInvariant() + " captions yet: use References > Insert Caption." : string.Empty;
                break;
        }

        foreach ((string text, _, _, _) in _targets)
        {
            TargetList.Items.Add(text);
        }

        if (TargetList.Items.Count > 0)
        {
            TargetList.SelectedIndex = 0;
        }
    }

    private void OnInsert(object sender, RoutedEventArgs e)
    {
        if (TargetList.SelectedIndex < 0 || TargetList.SelectedIndex >= _targets.Count)
        {
            return;
        }

        (string _, StoryId story, BlockPath path, string? bookmark) = _targets[TargetList.SelectedIndex];
        BookmarkName = bookmark ?? _session.EnsureParagraphBookmark(story, path);
        TargetPosition = bookmark is null ? new TextPosition(story, path, 0) : BookmarkSupport.Find(_session.Document, bookmark)?.Start;
        if (BookmarkName is null)
        {
            HintText.Text = "That paragraph is empty and cannot be referenced.";
            return;
        }

        DialogResult = true;
    }
}
