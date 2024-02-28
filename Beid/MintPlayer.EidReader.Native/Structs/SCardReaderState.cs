using MintPlayer.EidReader.Native.Enums;
using System.Runtime.InteropServices;
using System.Xml;

namespace MintPlayer.EidReader.Native.Structs;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct SCARD_READERSTATE
{
    public string szReader;

    public IntPtr pvUserData;

    public EReaderState dwCurrentState;

    public EReaderState dwEventState;

    public int cbAtr;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 36)]
    public byte[] rgbAtr;
}
