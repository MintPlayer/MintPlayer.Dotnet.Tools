using MintPlayer.EidReader.Core;
using MintPlayer.EidReader.Enums;
using MintPlayer.EidReader.Native;
using System.Security.Cryptography.X509Certificates;

namespace MintPlayer.EidReader;

public class EidCard : Card
{
    private static readonly byte[] ATR_VAL = { 0x3b, 0x98, 0x00, 0x40, 0x00, 0xa5, 0x03, 0x01, 0x01, 0x01, 0xad, 0x13, 0x00 };
    private static readonly byte[] ATR_MASK = { 0xff, 0xff, 0x00, 0xff, 0x00, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00 };
    private static readonly byte[] ATR18_VAL = { 0x3b, 0x7f, 0x96, 0x00, 0x00, 0x80, 0x31, 0x80, 0x65, 0xb0, 0x85, 0x04, 0x01, 0x20, 0x12, 0x0f, 0xff, 0x82, 0x90, 0x00 };
    private static readonly byte[] ATR18_MASK = { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff };

    private static readonly Dictionary<EEidFile, byte[]> fileSelectors = new Dictionary<EEidFile, byte[]>();

    static EidCard()
    {
        fileSelectors.Add(EEidFile.AuthCert, new byte[] { 0x3F, 0x00, 0xDF, 0x00, 0x50, 0x38 });
        fileSelectors.Add(EEidFile.SignCert, new byte[] { 0x3F, 0x00, 0xDF, 0x00, 0x50, 0x39 });
        fileSelectors.Add(EEidFile.CaCert, new byte[] { 0x3F, 0x00, 0xDF, 0x00, 0x50, 0x3A });
        fileSelectors.Add(EEidFile.RootCert, new byte[] { 0x3F, 0x00, 0xDF, 0x00, 0x50, 0x3B });
        fileSelectors.Add(EEidFile.RrnCert, new byte[] { 0x3F, 0x00, 0xDF, 0x00, 0x50, 0x3C });

        fileSelectors.Add(EEidFile.Id, new byte[] { 0x3F, 0x00, 0xDF, 0x01, 0x40, 0x31 });
        fileSelectors.Add(EEidFile.IdSig, new byte[] { 0x3F, 0x00, 0xDF, 0x01, 0x40, 0x32 });
        fileSelectors.Add(EEidFile.Address, new byte[] { 0x3F, 0x00, 0xDF, 0x01, 0x40, 0x33 });
        fileSelectors.Add(EEidFile.AddressSig, new byte[] { 0x3F, 0x00, 0xDF, 0x01, 0x40, 0x34 });
        fileSelectors.Add(EEidFile.Picture, new byte[] { 0x3F, 0x00, 0xDF, 0x01, 0x40, 0x35 });
    }

    public static bool IsEid(byte[] atr)
    {
        if (atr.Length == ATR_VAL.Length)
        {
            int i = 0;
            while (i < atr.Length && (atr[i] & ATR_MASK[i]) == ATR_VAL[i]) { i++; }
            if (i == atr.Length) return true;
        }

        if (atr.Length == ATR18_VAL.Length)
        {
            int i = 0;
            while (i < atr.Length && (atr[i] & ATR18_MASK[i]) == ATR18_VAL[i]) { i++; }
            if (i == atr.Length) return true;
        }

        return false;
    }

    internal EidCard(CardContextSafeHandler context, String readerName, byte[] atr)
        : base(context, readerName, atr) { }

    internal byte[] ReadRaw(EEidFile file) => ReadBinary(fileSelectors[file]);

    public X509Certificate2 AuthCert => new X509Certificate2(ReadRaw(EEidFile.AuthCert));

    public X509Certificate2 SignCert => new X509Certificate2(ReadRaw(EEidFile.SignCert));

    public X509Certificate2 CaCert => new X509Certificate2(ReadRaw(EEidFile.CaCert));

    public X509Certificate2 RootCert => new X509Certificate2(ReadRaw(EEidFile.RootCert));

    public X509Certificate2 RrnCert => new X509Certificate2(ReadRaw(EEidFile.RrnCert));

    public byte[] Picture => ReadRaw(EEidFile.Picture);

    public Address Address => new Address(ReadRaw(EEidFile.Address));

    public Identity Identity => new Identity(ReadRaw(EEidFile.Id));
}
