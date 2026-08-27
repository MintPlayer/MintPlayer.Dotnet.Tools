using System.Text;
using MintPlayer.EidReader.Core.Enums;
using MintPlayer.EidReader.Core.Extensions;

namespace MintPlayer.EidReader.Core.Tests;

public class ByteArrayExtensionsTests
{
    #region Parse

    [Fact]
    public void Parse_ReadsASingleTag()
    {
        var file = new Tlv().Add(0x01, "hello").Build();

        var parsed = file.Parse();

        parsed.Should().ContainKey((byte)0x01);
        parsed[0x01].ToStr().Should().Be("hello");
    }

    [Fact]
    public void Parse_ReadsSeveralTagsInOrder()
    {
        var file = new Tlv().Add(0x01, "a").Add(0x02, "bb").Add(0x03, "ccc").Build();

        var parsed = file.Parse();

        parsed.Should().HaveCount(3);
        parsed[0x01].ToStr().Should().Be("a");
        parsed[0x02].ToStr().Should().Be("bb");
        parsed[0x03].ToStr().Should().Be("ccc");
    }

    [Fact]
    public void Parse_PreservesRawBytes()
    {
        var file = new Tlv().Add(0x02, [0xDE, 0xAD, 0xBE, 0xEF]).Build();

        file.Parse()[0x02].Should().Equal([0xDE, 0xAD, 0xBE, 0xEF]);
    }

    [Fact]
    public void Parse_ReadsAZeroLengthValue()
    {
        var file = new Tlv().Add(0x01, string.Empty).Add(0x02, "x").Build();

        var parsed = file.Parse();

        parsed[0x01].Should().BeEmpty();
        parsed[0x02].ToStr().Should().Be("x");
    }

    [Fact]
    public void Parse_StopsAtATerminatorTag()
    {
        var file = new Tlv().Add(0x01, "kept").AddTerminator().Add(0x02, "dropped").Build();

        var parsed = file.Parse();

        parsed.Should().ContainKey((byte)0x01);
        parsed.Should().NotContainKey((byte)0x02);
    }

    [Fact]
    public void Parse_OnAnEmptyFile_ReturnsNothing()
        => Array.Empty<byte>().Parse().Should().BeEmpty();

    [Fact]
    public void Parse_HandlesAMaximumSingleByteLength()
    {
        var value = Enumerable.Repeat((byte)0x41, 0x7F).ToArray();
        var file = new Tlv().Add(0x01, value).Build();

        file.Parse()[0x01].Should().HaveCount(0x7F);
    }

    /// <summary>
    /// Regression for D14 in docs/PRD-TestCoverage.md. The continuation check read
    /// <c>(lenByte &amp; 0x08) == 0x80</c>; `&amp; 0x08` yields only 0 or 8, so it could never
    /// equal 0x80 and the multi-byte length form was unreachable dead code. A length byte of
    /// 0x81 was read as length 1 and the rest of the length treated as payload.
    /// </summary>
    [Fact]
    public void Parse_ReadsAMultiByteLength()
    {
        var value = Enumerable.Repeat((byte)0x42, 200).ToArray();
        var file = new Tlv().AddMultiByteLength(0x01, value).Build();

        var parsed = file.Parse();

        parsed[0x01].Should().HaveCount(200);
        parsed[0x01].Should().Equal(value);
    }

    [Fact]
    public void Parse_ReadsAMultiByteLengthFollowedByAnotherTag()
    {
        var value = Enumerable.Repeat((byte)0x43, 130).ToArray();
        var file = new Tlv().AddMultiByteLength(0x01, value).Add(0x02, "after").Build();

        var parsed = file.Parse();

        parsed[0x01].Should().HaveCount(130);
        parsed[0x02].ToStr().Should().Be("after");
    }

    [Fact]
    public void Parse_OnADuplicateTag_Throws()
    {
        // Dictionary.Add, so a malformed file with a repeated tag is rejected rather than
        // silently keeping one of the two.
        var file = new Tlv().Add(0x01, "a").Add(0x01, "b").Build();

        var act = () => file.Parse();

        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region ToStr

    [Fact]
    public void ToStr_DecodesUtf8()
        => Encoding.UTF8.GetBytes("Küßner").ToStr().Should().Be("Küßner");

    [Fact]
    public void ToStr_TrimsTrailingWhitespace()
        => Encoding.UTF8.GetBytes("value   ").ToStr().Should().Be("value");

    [Fact]
    public void ToStr_KeepsLeadingWhitespace()
        => Encoding.UTF8.GetBytes("  value").ToStr().Should().Be("  value");

    [Fact]
    public void ToStr_OnEmpty_IsEmpty()
        => Array.Empty<byte>().ToStr().Should().Be(string.Empty);

    #endregion

    #region ToDate

    [Theory]
    [InlineData("01.01.2020", 2020, 1, 1)]
    [InlineData("31.12.1999", 1999, 12, 31)]
    [InlineData("29.02.2024", 2024, 2, 29)]
    public void ToDate_ParsesDottedDates(string input, int year, int month, int day)
        => Encoding.UTF8.GetBytes(input).ToDate().Should().Be(new DateTime(year, month, day));

    [Fact]
    public void ToDate_StripsSpaces()
        => Encoding.UTF8.GetBytes("01 01 2020").ToDate().Should().Be(new DateTime(2020, 1, 1));

    [Fact]
    public void ToDate_AcceptsAnUnseparatedDate()
        => Encoding.UTF8.GetBytes("01012020").ToDate().Should().Be(new DateTime(2020, 1, 1));

    [Fact]
    public void ToDate_OnGarbage_Throws()
    {
        var act = () => Encoding.UTF8.GetBytes("not-a-date").ToDate();
        act.Should().Throw<FormatException>();
    }

    #endregion

    #region ToBirthDate

    [Theory]
    [InlineData("01.JAN. 1980", 1980, 1, 1)]
    [InlineData("15 MAAR 1975", 1975, 3, 15)]
    [InlineData("28.FEV.2001", 2001, 2, 28)]
    [InlineData("07 JUIL 1990", 1990, 7, 7)]
    public void ToBirthDate_ParsesDayMonthYear(string input, int year, int month, int day)
        => Encoding.UTF8.GetBytes(input).ToBirthDate().Should().Be(new DateTime(year, month, day));

    [Fact]
    public void ToBirthDate_WithOnlyAYear_DefaultsToTheFirstOfJanuary()
        => Encoding.UTF8.GetBytes("1980").ToBirthDate().Should().Be(new DateTime(1980, 1, 1));

    [Fact]
    public void ToBirthDate_WithAnUnknownMonth_Throws()
    {
        var act = () => Encoding.UTF8.GetBytes("01.XXX.1980").ToBirthDate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Unknown Birth Month*");
    }

    #endregion

    #region ToMonth

    [Theory]
    [InlineData("JAN", 1)]
    [InlineData("FEB", 2)]
    [InlineData("FEV", 2)]
    [InlineData("MÄR", 3)]
    [InlineData("MARS", 3)]
    [InlineData("MAAR", 3)]
    [InlineData("APR", 4)]
    [InlineData("AVR", 4)]
    [InlineData("MAI", 5)]
    [InlineData("MEI", 5)]
    [InlineData("JUIN", 6)]
    [InlineData("JUN", 6)]
    [InlineData("JUIL", 7)]
    [InlineData("JUL", 7)]
    [InlineData("AOUT", 8)]
    [InlineData("AUG", 8)]
    [InlineData("SEPT", 9)]
    [InlineData("SEP", 9)]
    [InlineData("OCT", 10)]
    [InlineData("OKT", 10)]
    [InlineData("NOV", 11)]
    [InlineData("DEC", 12)]
    [InlineData("DEZ", 12)]
    public void ToMonth_MapsEveryDocumentedAbbreviation(string abbreviation, int expected)
        => abbreviation.ToMonth().Should().Be(expected);

    [Fact]
    public void ToMonth_CoversAllTwelveMonths()
    {
        var abbreviations = new[] { "JAN", "FEB", "MAAR", "APR", "MEI", "JUN", "JUL", "AUG", "SEP", "OKT", "NOV", "DEC" };

        abbreviations.Select(a => a.ToMonth()).Should().Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]);
    }

    [Fact]
    public void ToMonth_IsCaseSensitive()
    {
        var act = () => "jan".ToMonth();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ToMonth_OnAnUnknownAbbreviation_Throws()
    {
        var act = () => "ZZZ".ToMonth();
        act.Should().Throw<InvalidOperationException>().WithMessage("*Unknown Birth Month: ZZZ*");
    }

    #endregion

    #region ToGender

    [Theory]
    [InlineData("M", EGender.Male)]
    [InlineData("V", EGender.Female)]
    [InlineData("F", EGender.Female)]
    [InlineData("W", EGender.Female)]
    public void ToGender_MapsTheKnownCodes(string code, EGender expected)
        => Encoding.UTF8.GetBytes(code).ToGender().Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("X")]
    [InlineData("m")]
    public void ToGender_OnAnythingElse_IsUnknown(string code)
        => Encoding.UTF8.GetBytes(code).ToGender().Should().Be(EGender.Unknown);

    #endregion

    #region ToDocType

    [Theory]
    [InlineData("1", EDocType.IdentityCard)]
    [InlineData("01", EDocType.IdentityCard)]
    [InlineData("6", EDocType.KidsCard)]
    [InlineData("06", EDocType.KidsCard)]
    [InlineData("7", EDocType.BootstrapCard)]
    [InlineData("07", EDocType.BootstrapCard)]
    [InlineData("8", EDocType.HabilitationCard)]
    [InlineData("08", EDocType.HabilitationCard)]
    [InlineData("11", EDocType.ForeignerA)]
    [InlineData("12", EDocType.ForeignerB)]
    [InlineData("13", EDocType.ForeignerC)]
    [InlineData("14", EDocType.ForeignerD)]
    [InlineData("15", EDocType.ForeignerE)]
    [InlineData("16", EDocType.ForeignerEplus)]
    [InlineData("17", EDocType.ForeignerF)]
    [InlineData("18", EDocType.ForeignerFplus)]
    [InlineData("19", EDocType.EuBlueCard)]
    [InlineData("20", EDocType.ICard_2011_98_EU)]
    [InlineData("21", EDocType.JCard_2011_98_EU)]
    [InlineData("22", EDocType.MCardBrexit)]
    [InlineData("23", EDocType.NCardBrexit)]
    [InlineData("27", EDocType.KCard_Council_EC_1030_2002)]
    [InlineData("28", EDocType.LCard_Council_EC_1030_2002)]
    [InlineData("31", EDocType.EU_Card)]
    [InlineData("32", EDocType.EU_Card_Plus)]
    [InlineData("33", EDocType.ACard_Council_EC_1030_2002)]
    [InlineData("34", EDocType.BCard_Council_EC_1030_2002)]
    [InlineData("35", EDocType.FCard_Council_EC_1030_2002)]
    [InlineData("36", EDocType.FCardPlus_Council_EC_1030_2002)]
    public void ToDocType_MapsEveryDocumentedCode(string code, EDocType expected)
        => Encoding.UTF8.GetBytes(code).ToDocType().Should().Be(expected);

    [Fact]
    public void ToDocType_CoversEveryEnumMember()
    {
        // If a new EDocType is added without a mapping, this fails rather than the gap
        // being discovered by a card in the field.
        var codes = new[] { "1", "6", "7", "8", "11", "12", "13", "14", "15", "16", "17", "18",
                            "19", "20", "21", "22", "23", "27", "28", "31", "32", "33", "34", "35", "36" };

        var mapped = codes.Select(c => Encoding.UTF8.GetBytes(c).ToDocType()).ToHashSet();

        mapped.Should().HaveCount(Enum.GetValues<EDocType>().Length);
    }

    [Theory]
    [InlineData("99")]
    [InlineData("")]
    [InlineData("24")]
    public void ToDocType_OnAnUnknownCode_Throws(string code)
    {
        var act = () => Encoding.UTF8.GetBytes(code).ToDocType();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Unknown Document Type*");
    }

    #endregion

    #region ToSpec

    [Theory]
    [InlineData("0", ESpec.None)]
    [InlineData("1", ESpec.WhiteCane)]
    [InlineData("2", ESpec.ExtendedMinor)]
    [InlineData("4", ESpec.YellowCane)]
    public void ToSpec_MapsTheSingleFlags(string code, ESpec expected)
        => Encoding.UTF8.GetBytes(code).ToSpec().Should().Be(expected);

    [Fact]
    public void ToSpec_MapsTheCombinedFlags()
    {
        Encoding.UTF8.GetBytes("3").ToSpec().Should().Be(ESpec.WhiteCane | ESpec.ExtendedMinor);
        Encoding.UTF8.GetBytes("5").ToSpec().Should().Be(ESpec.YellowCane | ESpec.ExtendedMinor);
    }

    [Theory]
    [InlineData("6")]
    [InlineData("")]
    [InlineData("x")]
    public void ToSpec_OnAnUnknownCode_Throws(string code)
    {
        var act = () => Encoding.UTF8.GetBytes(code).ToSpec();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Unknown Spec*");
    }

    #endregion
}
