using MintPlayer.EidReader.Native.Enums;
using System.Runtime.InteropServices;

namespace MintPlayer.EidReader.Native.Structs;

[StructLayout(LayoutKind.Sequential)]
public class SCARD_IO_REQUEST
{
    internal static readonly SCARD_IO_REQUEST T0 = new SCARD_IO_REQUEST(ECardPCI.SCARD_PCI_T0);
    internal static readonly SCARD_IO_REQUEST T1 = new SCARD_IO_REQUEST(ECardPCI.SCARD_PCI_T1);

    private readonly uint dwProtocol;
    private readonly int cbPciLength;

    internal SCARD_IO_REQUEST(ECardPCI protocol)
    {
        this.dwProtocol = (uint)protocol;
#if NET20 || NET40
            this.cbPciLength = Marshal.SizeOf(typeof(SCARD_IO_REQUEST));
#else
        this.cbPciLength = Marshal.SizeOf<SCARD_IO_REQUEST>();
#endif
    }

    public SCARD_IO_REQUEST(ECardProtocols protocol)
    {
        this.dwProtocol = (uint)protocol;
#if NET20 || NET40
            this.cbPciLength = Marshal.SizeOf(typeof(SCARD_IO_REQUEST));
#else
        this.cbPciLength = Marshal.SizeOf<SCARD_IO_REQUEST>();
#endif
    }
}
