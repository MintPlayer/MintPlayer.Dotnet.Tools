using MintPlayer.EidReader.Native.Enums;
using MintPlayer.EidReader.Native.Structs;
using System.Runtime.InteropServices;

namespace MintPlayer.EidReader.Native;

public static class WinSCard
{
    [DllImport("winscard.dll", CharSet = CharSet.Unicode)]
    public static extern uint SCardEstablishContext([MarshalAs(UnmanagedType.U4)] EContextScope dwScope, IntPtr pvReserved1, IntPtr pvReserved2, out CardContextSafeHandler phContext);

    [DllImport("winscard.dll", CharSet = CharSet.Unicode)]
    public static extern uint SCardReleaseContext(IntPtr hContext);

    [DllImport("winscard.dll", CharSet = CharSet.Unicode)]
    public static extern uint SCardListReaders(CardContextSafeHandler hContext, [In, MarshalAs(UnmanagedType.LPArray)] Char[]? mszGroups, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] Char[]? mszReaders, [In, Out] ref int pcchReaders);

    [DllImport("winscard.dll", CharSet = CharSet.Unicode)]
    public static extern uint SCardLocateCards(CardContextSafeHandler hContext, [In, MarshalAs(UnmanagedType.LPArray)] Char[] mszCards, [In, Out] SCARD_READERSTATE[] rgReaderStates, int cReaders);

    [DllImport("winscard.dll", CharSet = CharSet.Unicode)]
    public static extern uint SCardListCards(CardContextSafeHandler hContext, byte[] pbAtr, IntPtr rgguidInterfaces, int cguidInterfaceCount, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 5)] Char[] mszCards, [In, Out] ref int pcchCards);

    [DllImport("winscard.dll", CharSet = CharSet.Unicode)]
    public static extern uint SCardGetStatusChange(CardContextSafeHandler hContext, int dwTimeout, [In, Out] SCARD_READERSTATE[] rgReaderStates, int cReaders);

    [DllImport("winscard.dll", CharSet = CharSet.Unicode)]
    public static extern uint SCardConnect(CardContextSafeHandler hContext, string szReader, ECardShareMode dwShareMode, ECardProtocols dwPreferredProtocols, out CardSafeHandler phCard, out ECardProtocols pdwActiveProtocol);

    [DllImport("winscard.dll", CharSet = CharSet.Unicode)]
    public static extern uint SCardDisconnect(IntPtr hCard, ECardDisposition dwDisposition);

    [DllImport("winscard.dll", CharSet = CharSet.Unicode)]
    public static extern uint SCardGetAttrib(CardSafeHandler hCard, ECardAttrId dwAttrId, IntPtr pbAttr, ref int pcbAttrLen);

    [DllImport("winscard.dll", CharSet = CharSet.Unicode)]
    public static extern uint SCardBeginTransaction(CardSafeHandler hCard);

    [DllImport("winscard.dll", CharSet = CharSet.Unicode)]
    public static extern uint SCardEndTransaction(CardSafeHandler hCard, ECardDisposition dwDisposition);

    [DllImport("winscard.dll", CharSet = CharSet.Unicode)]
    public static extern uint SCardTransmit(CardSafeHandler hCard, SCARD_IO_REQUEST pioSendPci, byte[] pbSendBuffer, int cbSendLength, [In, Out] SCARD_IO_REQUEST pioRecvPci, [Out] byte[] pbRecvBuffer, [In, Out] ref int pcbRecvLength);

    [DllImport("winscard.dll", CharSet = CharSet.Unicode)]
    public static extern uint SCardFreeMemory(CardContextSafeHandler hContext, IntPtr pvMem);
}
