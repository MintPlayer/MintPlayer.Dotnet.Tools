using MintPlayer.EidReader.Core.Extensions;

namespace MintPlayer.EidReader.Core;

public class Address
{
    public Address(byte[] file)
    {
        IDictionary<byte, byte[]> d = file.Parse();

        StreetAndNumber = d[0x01].ToStr();
        Zip = d[0x02].ToStr();
        Municipality = d[0x03].ToStr();
    }

    public String StreetAndNumber { get; }

    public String Zip { get; }

    public String Municipality { get; }


}
