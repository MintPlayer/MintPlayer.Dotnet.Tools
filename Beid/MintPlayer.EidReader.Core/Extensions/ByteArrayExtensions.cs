using MintPlayer.EidReader.Core.Enums;
using System.Globalization;
using System.Text;

namespace MintPlayer.EidReader.Core.Extensions;

internal static class ByteArrayExtensions
{
    public static IDictionary<byte, byte[]> Parse(this byte[] file)
    {
        int i = 0;
        var retVal = new Dictionary<byte, byte[]>();
        while (i < file.Length - 1)
        {
            byte tag = file[i++];
            if (tag == 0) break;

            int len = 0;
            byte lenByte;
            do
            {
                lenByte = file[i++];
                len = (len << 7) + (lenByte & 0x7F);
            } while ((lenByte & 0x08) == 0x80);

            byte[] val = new byte[len];
            Array.Copy(file, i, val, 0, len);
            retVal.Add(tag, val);
            i += len;
        }

        return retVal;
    }

    public static String ToStr(this byte[] value) => Encoding.UTF8.GetString(value).TrimEnd();

    public static DateTime ToDate(this byte[] value) => DateTime.ParseExact(ToStr(value).Replace(" ", "").Replace(".", ""), "ddMMyyyy", CultureInfo.InvariantCulture);

    public static DateTime ToBirthDate(this byte[] value)
    {
        String stringValue = ToStr(value);

        String[] parts = stringValue.Split(new char[] { '.', ' ' }, StringSplitOptions.RemoveEmptyEntries); //split on . and ' '
        if (parts.Length == 3)
        {
            return new DateTime(
                Int32.Parse(parts[2]),
                parts[1].ToMonth(),
                Int32.Parse(parts[0]));
        }
        else
        {
            //only year, set to 1st of Jan.
            return new DateTime(Int32.Parse(parts[0]), 1, 1);
        }
    }

    public static EGender ToGender(this byte[] value)
    {
        switch (ToStr(value))
        {
            case "M":
                return EGender.Male;
            case "V":
            case "F":
            case "W":
                return EGender.Female;
            default:
                return EGender.Unknown;

        }
    }

    public static EDocType ToDocType(this byte[] value)
    {
        switch (ToStr(value))
        {
            case "1":
            case "01":
                return EDocType.IdentityCard;
            case "6":
            case "06":
                return EDocType.KidsCard;
            case "7":
            case "07":
                return EDocType.BootstrapCard;
            case "8":
            case "08":
                return EDocType.HabilitationCard;
            case "11":
                return EDocType.ForeignerA;
            case "12":
                return EDocType.ForeignerB;
            case "13":
                return EDocType.ForeignerC;
            case "14":
                return EDocType.ForeignerD;
            case "15":
                return EDocType.ForeignerE;
            case "16":
                return EDocType.ForeignerEplus;
            case "17":
                return EDocType.ForeignerF;
            case "18":
                return EDocType.ForeignerFplus;
            case "19":
                return EDocType.EuBlueCard;
            case "20":
                return EDocType.ICard_2011_98_EU;
            case "21":
                return EDocType.JCard_2011_98_EU;
            case "22":
                return EDocType.MCardBrexit;
            case "23":
                return EDocType.NCardBrexit;
            case "27":
                return EDocType.KCard_Council_EC_1030_2002;
            case "28":
                return EDocType.LCard_Council_EC_1030_2002;
            case "31":
                return EDocType.EU_Card;
            case "32":
                return EDocType.EU_Card_Plus;
            case "33":
                return EDocType.ACard_Council_EC_1030_2002;
            case "34":
                return EDocType.BCard_Council_EC_1030_2002;
            case "35":
                return EDocType.FCard_Council_EC_1030_2002;
            case "36":
                return EDocType.FCardPlus_Council_EC_1030_2002;
            default:
                throw new InvalidOperationException("Unknown Document Type: " + value.ToStr());
        }
    }

    public static ESpec ToSpec(this byte[] value)
    {
        switch (ToStr(value))
        {
            case "0":
                return ESpec.None;
            case "1":
                return ESpec.WhiteCane;
            case "2":
                return ESpec.ExtendedMinor;
            case "3":
                return ESpec.WhiteCane | ESpec.ExtendedMinor;
            case "4":
                return ESpec.YellowCane;
            case "5":
                return ESpec.YellowCane | ESpec.ExtendedMinor;
            default:
                throw new InvalidOperationException("Unknown Spec: " + value.ToStr());
        }
    }
}
