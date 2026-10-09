using Quill.Core.Units;
using Xunit;

namespace Quill.Core.Tests.Units;

public class UnitTests
{
    [Fact]
    public void Twips_convert_exactly_between_inches_points_and_dips()
    {
        Twips inch = Twips.FromInches(1);
        Assert.Equal(1440, inch.Value);
        Assert.Equal(72, inch.ToPoints());
        Assert.Equal(96, inch.ToDips());
        Assert.Equal(inch, Twips.FromPoints(72));
        Assert.Equal(inch, Twips.FromDips(96));
        Assert.Equal(914400, inch.ToEmu());
        Assert.Equal(inch, Twips.FromEmu(914400));
    }

    [Fact]
    public void Twips_arithmetic_and_ordering()
    {
        var a = new Twips(100);
        var b = new Twips(250);
        Assert.Equal(new Twips(350), a + b);
        Assert.Equal(new Twips(-150), a - b);
        Assert.True(a < b);
        Assert.Equal(b, Twips.Max(a, b));
        Assert.Equal(new Twips(200), a * 2);
        Assert.Equal(new Twips(150), a * 1.5);
    }

    [Fact]
    public void HalfPoints_map_to_dips()
    {
        HalfPoints size = HalfPoints.FromPoints(12);
        Assert.Equal(24, size.Value);
        Assert.Equal(16, size.ToDips());
        Assert.Equal(10.5, HalfPoints.FromPoints(10.5).ToPoints());
    }

    [Fact]
    public void LineSpacing_factors()
    {
        Assert.Equal(1.0, LineSpacing.Single.Factor);
        Assert.Equal(1.15, LineSpacing.Multiple(1.15).Factor, 3);
        Assert.True(double.IsNaN(LineSpacing.Exactly(new Twips(300)).Factor));
        Assert.Equal(new Twips(300), LineSpacing.AtLeast(new Twips(300)).Height);
    }

    [Theory]
    [InlineData("2F5496", 0x2F, 0x54, 0x96)]
    [InlineData("#ffffff", 255, 255, 255)]
    public void DocColor_parses_hex(string hex, int r, int g, int b)
    {
        DocColor color = DocColor.Parse(hex);
        Assert.Equal((byte)r, color.R);
        Assert.Equal((byte)g, color.G);
        Assert.Equal((byte)b, color.B);
        Assert.Equal(255, color.A);
        Assert.False(color.IsAuto);
    }

    [Fact]
    public void DocColor_auto_and_invalid()
    {
        Assert.True(DocColor.Parse("auto").IsAuto);
        Assert.Throws<FormatException>(() => DocColor.Parse("12345"));
        Assert.Equal("2F5496", DocColor.Parse("2f5496").ToHex());
    }

    [Theory]
    [InlineData("1", LengthUnit.Inches, 1440)]
    [InlineData("0.5in", LengthUnit.Centimeters, 720)]
    [InlineData("0.5 inches", LengthUnit.Centimeters, 720)]
    [InlineData("1\"", LengthUnit.Centimeters, 1440)]
    [InlineData("2.54 cm", LengthUnit.Inches, 1440)]
    [InlineData("25.4mm", LengthUnit.Inches, 1440)]
    [InlineData("72pt", LengthUnit.Inches, 1440)]
    [InlineData("1,5", LengthUnit.Centimeters, 850)]
    [InlineData("  3  ", LengthUnit.Centimeters, 1701)]
    public void Twips_parse_user_input_with_units(string text, LengthUnit defaultUnit, int expected)
    {
        Assert.True(Twips.TryParse(text, defaultUnit, out Twips value));
        Assert.Equal(expected, value.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1.2.3")]
    [InlineData("in")]
    public void Twips_rejects_non_lengths(string text)
    {
        Assert.False(Twips.TryParse(text, LengthUnit.Inches, out _));
    }

    [Fact]
    public void Twips_format_for_display()
    {
        Assert.Equal("1.25", Twips.FromInches(1.25).Format(LengthUnit.Inches));
        Assert.Equal("2.54", Twips.FromInches(1).Format(LengthUnit.Centimeters));
        Assert.Equal("25.4", Twips.FromInches(1).Format(LengthUnit.Millimeters));
        Assert.Equal("72", Twips.FromInches(1).Format(LengthUnit.Points));
        Assert.Equal(Twips.FromInches(2), Twips.From(2, LengthUnit.Inches));
    }
}
