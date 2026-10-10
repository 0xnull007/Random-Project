namespace Quill.Core.Editing;

/// <summary>Which AutoCorrect rules run while typing.</summary>
public sealed record AutoCorrectOptions(
    bool SmartQuotes = true,
    bool Dashes = true,
    bool Symbols = true,
    bool CapitalizeSentences = true,
    bool AutomaticLists = true)
{
    public static readonly AutoCorrectOptions Default = new();

    public static readonly AutoCorrectOptions Off = new(false, false, false, false, false);

    public bool Any => SmartQuotes || Dashes || Symbols || CapitalizeSentences || AutomaticLists;
}
