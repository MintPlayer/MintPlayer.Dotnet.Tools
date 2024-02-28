using MintPlayer.EidReader.Core.Enums;
using MintPlayer.EidReader.Core.Extensions;

namespace MintPlayer.EidReader.Core;

public class Identity
{
    public String CardNr { get; } //0x01

    public byte[] ChipNr { get; } //0x02

    public DateTime ValidityBeginDate { get; } //0x03

    public DateTime ValidityEndDate { get; } //0x04

    public String IssuingMunicipality { get; } //0x05

    public String NationalNr { get; } //0x06

    public String Surname { get; } //0x07

    public String FirstNames { get; } //0x08

    public String FirstLetterOfThirdGivenName { get; } //0x09

    public String Nationality { get; } //0x0A

    public String LocationOfBirth { get; } //0x0B

    public DateTime DateOfBirth { get; } //0x0C

    public EGender Gender { get; } //0x0D

    public String Nobility { get; } //0x0E

    public EDocType DocumentType { get; } //0x0F

    public ESpec SpecialStatus { get; } //0x10


    //#define BEID_FIELD_TAG_ID_PhotoHash				0x11
    //#define BEID_FIELD_TAG_ID_Duplicata				0x12
    //#define BEID_FIELD_TAG_ID_SpecialOrganization	0x13
    //#define BEID_FIELD_TAG_ID_MemberOfFamily		0x14
    //#define BEID_FIELD_TAG_ID_DateAndCountryOfProtection	0x15
    //#define BEID_FIELD_TAG_ID_WorkPermitType		0x16
    //#define BEID_FIELD_TAG_ID_Vat1					0x17
    //#define BEID_FIELD_TAG_ID_Vat2					0x18
    //#define BEID_FIELD_TAG_ID_RegionalFileNumber	0x19
    //#define BEID_FIELD_TAG_ID_BasicKeyHash			0x1A


    public Identity(byte[] file)
    {
        IDictionary<byte, byte[]> d = file.Parse();
        CardNr = d[0x01].ToStr();
        ChipNr = d[0x02];
        ValidityBeginDate = d[0x03].ToDate();
        ValidityEndDate = d[0x04].ToDate();
        IssuingMunicipality = d[0x05].ToStr();
        NationalNr = d[0x06].ToStr();
        Surname = d[0x07].ToStr();
        FirstNames = d[0x08].ToStr();
        FirstLetterOfThirdGivenName = d[0x09].ToStr();
        Nationality = d[0x0A].ToStr();
        LocationOfBirth = d[0x0B].ToStr();
        DateOfBirth = d[0x0C].ToBirthDate();
        Gender = d[0x0D].ToGender();
        Nobility = d[0x0E].ToStr();
        DocumentType = d[0x0F].ToDocType();
        SpecialStatus = d[0x10].ToSpec();
    }
}
