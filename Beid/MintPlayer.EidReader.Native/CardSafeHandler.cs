using MintPlayer.EidReader.Native.Enums;
using System.Runtime.InteropServices;

namespace MintPlayer.EidReader.Native;

public class CardSafeHandler : SafeHandle
{
    private CardSafeHandler() : base(IntPtr.Zero, true) { }

    public CardSafeHandler(IntPtr preexistinghandle) : base(IntPtr.Zero, true)
    {
        handle = preexistinghandle;
    }

    protected override bool ReleaseHandle()
    {
        return WinSCard.SCardDisconnect(handle, ECardDisposition.SCARD_LEAVE_CARD) == 0;
    }

    public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);
}
