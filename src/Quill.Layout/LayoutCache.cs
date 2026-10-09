using Quill.Core.Model;

namespace Quill.Layout;

/// <summary>
/// Caches <see cref="ParagraphLayout"/> by paragraph identity, column width and field values. Because the
/// document is immutable, an unchanged paragraph is the same object and its lines can be reused.
/// </summary>
public sealed class LayoutCache : IDisposable
{
    private Dictionary<Key, ParagraphLayout> _entries = new();
    private Dictionary<Key, ParagraphLayout>? _survivors;

    public int Count => _entries.Count;

    /// <summary>Starts a mark phase: entries not fetched before <see cref="EndSweep"/> are disposed.</summary>
    public void BeginSweep() => _survivors = new Dictionary<Key, ParagraphLayout>();

    public void EndSweep()
    {
        if (_survivors is null)
        {
            return;
        }

        foreach ((Key key, ParagraphLayout layout) in _entries)
        {
            if (!_survivors.ContainsKey(key))
            {
                layout.Dispose();
            }
        }

        _entries = _survivors;
        _survivors = null;
    }

    public ParagraphLayout GetOrAdd(Paragraph paragraph, double width, string fieldSignature, Func<ParagraphLayout> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        var key = new Key(paragraph, Math.Round(width, 3), fieldSignature);
        if (!_entries.TryGetValue(key, out ParagraphLayout? layout))
        {
            layout = create();
            _entries[key] = layout;
        }

        _survivors?.TryAdd(key, layout);
        return layout;
    }

    public void Clear()
    {
        foreach (ParagraphLayout layout in _entries.Values)
        {
            layout.Dispose();
        }

        _entries.Clear();
        _survivors = null;
    }

    public void Dispose() => Clear();

    private readonly struct Key(Paragraph paragraph, double width, string fields) : IEquatable<Key>
    {
        private readonly Paragraph _paragraph = paragraph;
        private readonly double _width = width;
        private readonly string _fields = fields;

        public bool Equals(Key other) =>
            ReferenceEquals(_paragraph, other._paragraph) && _width == other._width && string.Equals(_fields, other._fields, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is Key other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_paragraph), _width, _fields);
    }
}
