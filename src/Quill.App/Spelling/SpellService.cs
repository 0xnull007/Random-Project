using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Quill.Core.Model;
using Quill.Core.Styles;

namespace Quill.App.Spelling;

/// <summary>A word the spell checker flagged, as offsets into the paragraph's flat text.</summary>
public sealed record Misspelling(int Start, int Length, string Word)
{
    public int End => Start + Length;
}

/// <summary>Spelling results for one paragraph (keyed by paragraph identity; the paragraph is immutable).</summary>
public sealed record ParagraphSpelling(string LanguageTag, ImmutableArray<Misspelling> Errors);

/// <summary>
/// Spell checking through the Windows Spell Checking API (the engine Word, Edge and the OS use, with the user's
/// custom dictionaries). Results are cached per paragraph instance. All calls happen on the UI thread.
/// </summary>
public sealed class SpellService
{
    public static SpellService Shared { get; } = new();

    private readonly Dictionary<string, ISpellChecker?> _checkers = new(StringComparer.OrdinalIgnoreCase);
    private ConditionalWeakTable<Paragraph, ParagraphSpelling> _cache = new();
    private ISpellCheckerFactory? _factory;
    private bool _initialized;
    private bool _failed;

    /// <summary>Raised after Ignore or Add, when every paragraph needs checking again.</summary>
    public event EventHandler? Changed;

    public bool IsAvailable
    {
        get
        {
            EnsureFactory();
            return _factory is not null;
        }
    }

    public bool TryGetCached(Paragraph paragraph, out ParagraphSpelling spelling) => _cache.TryGetValue(paragraph, out spelling!);

    /// <summary>Checks a paragraph (or returns the cached result).</summary>
    public ParagraphSpelling Check(Paragraph paragraph, StyleResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(resolver);
        if (_cache.TryGetValue(paragraph, out ParagraphSpelling? cached))
        {
            return cached;
        }

        ParagraphSpelling result = Compute(paragraph, resolver);
        _cache.AddOrUpdate(paragraph, result);
        return result;
    }

    public IReadOnlyList<string> Suggest(string word, string languageTag)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (GetChecker(languageTag) is not { } checker)
        {
            return [];
        }

        try
        {
            return ReadStrings(checker.Suggest(word), 6);
        }
        catch (Exception ex) when (IsComFailure(ex))
        {
            Log("Suggest", ex);
            return [];
        }
    }

    /// <summary>Skips the word for the rest of this session.</summary>
    public void Ignore(string word, string languageTag)
    {
        ArgumentNullException.ThrowIfNull(word);
        try
        {
            GetChecker(languageTag)?.Ignore(word);
        }
        catch (Exception ex) when (IsComFailure(ex))
        {
            Log("Ignore", ex);
        }

        Invalidate();
    }

    /// <summary>Adds the word to the user's Windows dictionary (shared with other apps).</summary>
    public void Add(string word, string languageTag)
    {
        ArgumentNullException.ThrowIfNull(word);
        try
        {
            GetChecker(languageTag)?.Add(word);
        }
        catch (Exception ex) when (IsComFailure(ex))
        {
            Log("Add", ex);
        }

        Invalidate();
    }

    private void Invalidate()
    {
        _cache = new ConditionalWeakTable<Paragraph, ParagraphSpelling>();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private ParagraphSpelling Compute(Paragraph paragraph, StyleResolver resolver)
    {
        string tag = resolver.ResolveParagraphMark(paragraph).Language;
        if (paragraph.Length == 0 || GetChecker(tag) is not { } checker)
        {
            return new ParagraphSpelling(tag, ImmutableArray<Misspelling>.Empty);
        }

        // Blank out everything that is not ordinary text: pictures, fields, breaks and hyperlink text.
        char[] chars = paragraph.FlatText.ToCharArray();
        foreach (InlineSpan span in paragraph.Spans())
        {
            if (span.Inline is not Run || span.Inline.Properties.Link is not null || span.Inline.Properties.Hidden == true)
            {
                for (int i = span.Start; i < span.End && i < chars.Length; i++)
                {
                    chars[i] = ' ';
                }
            }
        }

        for (int i = 0; i < chars.Length; i++)
        {
            if (chars[i] is '\t' or ' ' or Paragraph.ObjectReplacementChar)
            {
                chars[i] = ' ';
            }
        }

        var text = new string(chars);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ParagraphSpelling(tag, ImmutableArray<Misspelling>.Empty);
        }

        var errors = ImmutableArray.CreateBuilder<Misspelling>();
        try
        {
            IEnumSpellingError enumerator = checker.Check(text);
            while (enumerator.Next(out ISpellingError error) == 0 && error is not null)
            {
                int start = (int)error.GetStartIndex();
                int length = (int)error.GetLength();
                CorrectiveAction action = error.GetCorrectiveAction();
                Marshal.FinalReleaseComObject(error);
                if (action == CorrectiveAction.None || start < 0 || length <= 0 || start + length > text.Length)
                {
                    continue;
                }

                errors.Add(new Misspelling(start, length, text.Substring(start, length)));
            }

            Marshal.FinalReleaseComObject(enumerator);
        }
        catch (Exception ex) when (IsComFailure(ex))
        {
            Log("Check", ex);
        }

        return new ParagraphSpelling(tag, errors.ToImmutable());
    }

    private ISpellChecker? GetChecker(string languageTag)
    {
        EnsureFactory();
        if (_factory is null)
        {
            return null;
        }

        string tag = string.IsNullOrWhiteSpace(languageTag) ? CultureInfo.CurrentCulture.Name : languageTag;
        if (_checkers.TryGetValue(tag, out ISpellChecker? cached))
        {
            return cached;
        }

        ISpellChecker? checker = Create(tag);
        if (checker is null && tag.Contains('-', StringComparison.Ordinal))
        {
            checker = Create(tag[..tag.IndexOf('-', StringComparison.Ordinal)]);
        }

        if (checker is null && string.IsNullOrWhiteSpace(languageTag))
        {
            checker = Create("en-US");
        }

        _checkers[tag] = checker;
        return checker;
    }

    private ISpellChecker? Create(string tag)
    {
        try
        {
            return _factory is not null && _factory.IsSupported(tag) ? _factory.CreateSpellChecker(tag) : null;
        }
        catch (Exception ex) when (IsComFailure(ex))
        {
            Log("CreateSpellChecker " + tag, ex);
            return null;
        }
    }

    private void EnsureFactory()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        try
        {
            if (Environment.OSVersion.Version.Major >= 6 && Type.GetTypeFromCLSID(SpellCheckerFactoryClass.Clsid) is { } type && Activator.CreateInstance(type) is ISpellCheckerFactory factory)
            {
                _factory = factory;
            }
        }
        catch (Exception ex) when (IsComFailure(ex))
        {
            _failed = true;
            Log("SpellCheckerFactory", ex);
        }
    }

    private static List<string> ReadStrings(IEnumString enumerator, int max)
    {
        var result = new List<string>();
        var buffer = new string[1];
        while (result.Count < max && enumerator.Next(1, buffer, IntPtr.Zero) == 0)
        {
            if (!string.IsNullOrEmpty(buffer[0]))
            {
                result.Add(buffer[0]);
            }
        }

        Marshal.FinalReleaseComObject(enumerator);
        return result;
    }

    private static bool IsComFailure(Exception ex) =>
        ex is COMException or InvalidCastException or NotSupportedException or TypeLoadException or PlatformNotSupportedException
            or MissingMethodException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or System.Reflection.TargetInvocationException;

    private void Log(string what, Exception exception)
    {
        if (_failed && what != "SpellCheckerFactory")
        {
            return;
        }

        try
        {
            string path = Path.Combine(Path.GetDirectoryName(App.CrashLogPath)!, "spelling.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, DateTimeOffset.Now.ToString("u", CultureInfo.InvariantCulture) + " " + what + " failed: " + exception + Environment.NewLine);
        }
        catch (Exception)
        {
            // Diagnostics must never break the app.
        }
    }
}
