using System.Text;

namespace MintPlayer.EidReader.Core.Tests;

/// <summary>
/// Builds the tag-length-value files an eID card returns, so the parser can be tested
/// without a card reader. Single-byte lengths only unless <see cref="AddMultiByteLength"/>
/// is used, matching what a real card emits.
/// </summary>
internal sealed class Tlv
{
    private readonly List<byte> _bytes = [];

    public Tlv Add(byte tag, string value) => Add(tag, Encoding.UTF8.GetBytes(value));

    public Tlv Add(byte tag, params byte[] value)
    {
        if (value.Length > 0x7F)
            throw new ArgumentException("Use AddMultiByteLength for values longer than 127 bytes.", nameof(value));

        _bytes.Add(tag);
        _bytes.Add((byte)value.Length);
        _bytes.AddRange(value);
        return this;
    }

    /// <summary>
    /// Encodes the length in the 7-bit continuation form: every byte but the last has the
    /// 0x80 bit set, and each contributes 7 bits.
    /// </summary>
    public Tlv AddMultiByteLength(byte tag, byte[] value)
    {
        _bytes.Add(tag);

        var length = value.Length;
        var groups = new List<byte>();
        do
        {
            groups.Insert(0, (byte)(length & 0x7F));
            length >>= 7;
        } while (length > 0);

        for (var i = 0; i < groups.Count; i++)
            _bytes.Add(i == groups.Count - 1 ? groups[i] : (byte)(groups[i] | 0x80));

        _bytes.AddRange(value);
        return this;
    }

    /// <summary>A 0x00 tag, which the parser treats as end-of-file.</summary>
    public Tlv AddTerminator()
    {
        _bytes.Add(0x00);
        return this;
    }

    public byte[] Build() => [.. _bytes];

    /// <summary>A complete, valid identity file with every tag the Identity ctor reads.</summary>
    public static byte[] ValidIdentityFile(
        string cardNr = "592176100000",
        string validityBegin = "01.01.2020",
        string validityEnd = "01.01.2030",
        string birthDate = "01.JAN. 1980",
        string gender = "M",
        string docType = "1",
        string spec = "0")
        => new Tlv()
            .Add(0x01, cardNr)
            .Add(0x02, [0xDE, 0xAD, 0xBE, 0xEF])
            .Add(0x03, validityBegin)
            .Add(0x04, validityEnd)
            .Add(0x05, "Brussel")
            .Add(0x06, "80010112345")
            .Add(0x07, "De Clippel")
            .Add(0x08, "Pieterjan Jan")
            .Add(0x09, "K")
            .Add(0x0A, "Belg")
            .Add(0x0B, "Gent")
            .Add(0x0C, birthDate)
            .Add(0x0D, gender)
            .Add(0x0E, string.Empty)
            .Add(0x0F, docType)
            .Add(0x10, spec)
            .Build();

    /// <summary>A complete, valid address file.</summary>
    public static byte[] ValidAddressFile(
        string streetAndNumber = "Kerkstraat 1",
        string zip = "9000",
        string municipality = "Gent")
        => new Tlv()
            .Add(0x01, streetAndNumber)
            .Add(0x02, zip)
            .Add(0x03, municipality)
            .Build();
}
