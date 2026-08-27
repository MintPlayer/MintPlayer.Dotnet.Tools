using MintPlayer.EidReader.Core.Enums;

namespace MintPlayer.EidReader.Core.Tests;

public class IdentityAndAddressTests
{
    #region Identity

    [Fact]
    public void Identity_ReadsEveryField()
    {
        var identity = new Identity(Tlv.ValidIdentityFile());

        identity.CardNr.Should().Be("592176100000");
        identity.ChipNr.Should().Equal([0xDE, 0xAD, 0xBE, 0xEF]);
        identity.ValidityBeginDate.Should().Be(new DateTime(2020, 1, 1));
        identity.ValidityEndDate.Should().Be(new DateTime(2030, 1, 1));
        identity.IssuingMunicipality.Should().Be("Brussel");
        identity.NationalNr.Should().Be("80010112345");
        identity.Surname.Should().Be("De Clippel");
        identity.FirstNames.Should().Be("Pieterjan Jan");
        identity.FirstLetterOfThirdGivenName.Should().Be("K");
        identity.Nationality.Should().Be("Belg");
        identity.LocationOfBirth.Should().Be("Gent");
        identity.DateOfBirth.Should().Be(new DateTime(1980, 1, 1));
        identity.Gender.Should().Be(EGender.Male);
        identity.Nobility.Should().Be(string.Empty);
        identity.DocumentType.Should().Be(EDocType.IdentityCard);
        identity.SpecialStatus.Should().Be(ESpec.None);
    }

    [Fact]
    public void Identity_ReadsAFemaleCard()
        => new Identity(Tlv.ValidIdentityFile(gender: "V")).Gender.Should().Be(EGender.Female);

    [Fact]
    public void Identity_ReadsAKidsCard()
        => new Identity(Tlv.ValidIdentityFile(docType: "6")).DocumentType.Should().Be(EDocType.KidsCard);

    [Fact]
    public void Identity_ReadsACombinedSpecialStatus()
        => new Identity(Tlv.ValidIdentityFile(spec: "3")).SpecialStatus
            .Should().Be(ESpec.WhiteCane | ESpec.ExtendedMinor);

    [Fact]
    public void Identity_ReadsAYearOnlyBirthDate()
        => new Identity(Tlv.ValidIdentityFile(birthDate: "1965")).DateOfBirth
            .Should().Be(new DateTime(1965, 1, 1));

    [Fact]
    public void Identity_HandlesALeapDayValidityDate()
        => new Identity(Tlv.ValidIdentityFile(validityBegin: "29.02.2024")).ValidityBeginDate
            .Should().Be(new DateTime(2024, 2, 29));

    [Fact]
    public void Identity_OnAFileMissingATag_Throws()
    {
        // The ctor indexes the dictionary directly, so a truncated file is a hard failure
        // rather than a partially-populated Identity.
        var truncated = new Tlv().Add(0x01, "592176100000").Build();

        var act = () => new Identity(truncated);

        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void Identity_OnAnEmptyFile_Throws()
    {
        var act = () => new Identity([]);
        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void Identity_OnAnUnknownDocumentType_Throws()
    {
        var act = () => new Identity(Tlv.ValidIdentityFile(docType: "99"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Unknown Document Type*");
    }

    #endregion

    #region Address

    [Fact]
    public void Address_ReadsEveryField()
    {
        var address = new Address(Tlv.ValidAddressFile());

        address.StreetAndNumber.Should().Be("Kerkstraat 1");
        address.Zip.Should().Be("9000");
        address.Municipality.Should().Be("Gent");
    }

    [Fact]
    public void Address_TrimsTrailingPadding()
    {
        var file = Tlv.ValidAddressFile(streetAndNumber: "Kerkstraat 1    ");

        new Address(file).StreetAndNumber.Should().Be("Kerkstraat 1");
    }

    [Fact]
    public void Address_HandlesNonAsciiNames()
    {
        var file = Tlv.ValidAddressFile(municipality: "Liège");

        new Address(file).Municipality.Should().Be("Liège");
    }

    [Fact]
    public void Address_IgnoresExtraTags()
    {
        var file = new Tlv()
            .Add(0x01, "Kerkstraat 1")
            .Add(0x02, "9000")
            .Add(0x03, "Gent")
            .Add(0x04, "unused")
            .Build();

        new Address(file).Municipality.Should().Be("Gent");
    }

    [Fact]
    public void Address_OnAFileMissingATag_Throws()
    {
        var truncated = new Tlv().Add(0x01, "Kerkstraat 1").Build();

        var act = () => new Address(truncated);

        act.Should().Throw<KeyNotFoundException>();
    }

    #endregion
}
