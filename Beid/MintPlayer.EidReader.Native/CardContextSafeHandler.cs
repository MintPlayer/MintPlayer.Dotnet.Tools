using System.Runtime.InteropServices;

namespace MintPlayer.EidReader.Native;

public class CardContextSafeHandler : SafeHandle
{
    private CardContextSafeHandler() : base(IntPtr.Zero, true)
    {
    }

    public CardContextSafeHandler(IntPtr preexistingHandle) : base(IntPtr.Zero, true)
    {
        this.handle = preexistingHandle;
    }

    protected override bool ReleaseHandle()
    {
        return WinSCard.SCardReleaseContext(handle) == 0;
    }

    public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);
}