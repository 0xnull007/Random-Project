using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Quill.App.Spelling;

// COM declarations for the Windows Spell Checking API (spellcheck.h, Windows 8+). Member order is the vtable order.

internal enum CorrectiveAction
{
    None = 0,
    GetSuggestions = 1,
    Replace = 2,
    Delete = 3,
}

[ComImport]
[Guid("8E018A9D-2415-4677-BF08-794EA61F94BB")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISpellCheckerFactory
{
    IEnumString GetSupportedLanguages();

    [return: MarshalAs(UnmanagedType.Bool)]
    bool IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag);

    ISpellChecker CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag);
}

[ComImport]
[Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISpellChecker
{
    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetLanguageTag();

    IEnumSpellingError Check([MarshalAs(UnmanagedType.LPWStr)] string text);

    IEnumString Suggest([MarshalAs(UnmanagedType.LPWStr)] string word);

    void Add([MarshalAs(UnmanagedType.LPWStr)] string word);

    void Ignore([MarshalAs(UnmanagedType.LPWStr)] string word);

    void AutoCorrect([MarshalAs(UnmanagedType.LPWStr)] string from, [MarshalAs(UnmanagedType.LPWStr)] string to);

    byte GetOptionValue([MarshalAs(UnmanagedType.LPWStr)] string optionId);

    IEnumString GetOptionIds();

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetId();

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetLocalizedName();

    uint AddSpellCheckerChanged(IntPtr handler);

    void RemoveSpellCheckerChanged(uint eventCookie);

    IntPtr GetOptionDescription([MarshalAs(UnmanagedType.LPWStr)] string optionId);

    IEnumSpellingError ComprehensiveCheck([MarshalAs(UnmanagedType.LPWStr)] string text);
}

[ComImport]
[Guid("803E3BD4-2828-4410-8290-418D1D73C762")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumSpellingError
{
    [PreserveSig]
    int Next(out ISpellingError value);
}

[ComImport]
[Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISpellingError
{
    uint GetStartIndex();

    uint GetLength();

    CorrectiveAction GetCorrectiveAction();

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetReplacement();
}

internal static class SpellCheckerFactoryClass
{
    public static readonly Guid Clsid = new("7AB36653-1796-484B-BDFA-E74F1DB7C1DC");
}
