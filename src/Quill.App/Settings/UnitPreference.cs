using System.Globalization;
using Quill.Core.Units;

namespace Quill.App.Settings;

/// <summary>The length unit dialogs show (inches or centimeters): the user's choice, else what the Windows region suggests.</summary>
public static class UnitPreference
{
    private static LengthUnit? s_override;

    public static LengthUnit Current => s_override ?? (RegionInfo.CurrentRegion.IsMetric ? LengthUnit.Centimeters : LengthUnit.Inches);

    public static string Name => Current == LengthUnit.Centimeters ? "cm" : "in";

    /// <summary>"Inches", "Centimeters" or anything else for automatic.</summary>
    public static void Apply(string? setting) => s_override = setting switch
    {
        "Inches" => LengthUnit.Inches,
        "Centimeters" => LengthUnit.Centimeters,
        _ => null,
    };
}
