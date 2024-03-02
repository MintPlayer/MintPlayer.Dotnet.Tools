using MintPlayer.EidReader.Exceptions;
using MintPlayer.EidReader.Native;
using MintPlayer.EidReader.Native.Enums;
using MintPlayer.EidReader.Native.Structs;

namespace MintPlayer.EidReader;

public class Card : IDisposable
{
    private static readonly byte[] CMD_READ_BINARY = { 0x00, 0xB0, 0x00, 0x00, 0x00 };
    private static readonly byte[] CMD_SELECT_FILE = { 0x00, 0xA4, 0x08, 0x0C, 0x00 };

    private readonly CardContextSafeHandler context;
    
    private CardSafeHandler handler;
    private ECardProtocols protocol;
    public byte[] ATR { get; private set; }

    internal Card(CardContextSafeHandler context, String readerName, byte[] atr)
    {
        this.context = context;
        this.ReaderName = readerName;
        this.ATR = atr;
    }

    ~Card()
    {
        Dispose(false);
    }

    public String ReaderName { get; private set; }

    public void Open(Boolean exclusive = false)
    {
        uint retVal = WinSCard.SCardConnect(context, ReaderName, exclusive ? ECardShareMode.SCARD_SHARE_EXCLUSIVE : ECardShareMode.SCARD_SHARE_SHARED, ECardProtocols.SCARD_PROTOCOL_T0 | ECardProtocols.SCARD_PROTOCOL_T1, out handler, out protocol);
        if (retVal == 0x80100069L) throw new NoCardException("Not card was found in the reader");
        if (retVal == 0x8010000BL) throw new ReaderException("The card is being accessed from a different context");
        if (retVal == 0x80100009L) throw new ReaderException("The specified reader does not exist");
        if (retVal != 0) throw new InvalidOperationException("Failed to open card reader: 0x" + retVal.ToString("X"));
    }

    public byte[] ReadBinary(byte[] file)
    {
        uint retVal;
        uint attempts;
        var ioReq = new SCARD_IO_REQUEST(protocol);

        attempts = 0;
        do
        {
            retVal = WinSCard.SCardBeginTransaction(handler);
            if (retVal == 0x80100017) throw new ReaderException("The card reader isn't available any more");
            if (retVal == 0x80100069) throw new NoCardException("The card has been removed");
            if (retVal == 0x80100068 || retVal == 0x8010002F) attempts++; //former is reset (which is fine, we are only starting), the latter is "comm error, retry".
        } while (attempts > 0 && attempts < 5);
        if (retVal != 0) throw new InvalidOperationException("Failed to start transaction: 0x" + retVal.ToString("X"));
        
        try
        {
            byte[] cmd;
            int rspLen = 258;
            byte[] rsp = new byte[rspLen];

            cmd = new byte[CMD_SELECT_FILE.Length+file.Length];
            Array.Copy(CMD_SELECT_FILE, cmd, CMD_SELECT_FILE.Length);
            cmd[4] = (byte) file.Length;
            Array.Copy(file, 0, cmd, CMD_SELECT_FILE.Length, file.Length);
            attempts = 0;
            do
            {
                retVal = WinSCard.SCardTransmit(handler, ioReq, cmd, cmd.Length, null, rsp, ref rspLen);
                if (retVal == 0x80100017) throw new ReaderException("The card reader isn't available any more");
                if (retVal == 0x80100069) throw new NoCardException("The card has been removed");
                if (retVal == 0x8010002F) attempts++; //"comm error, retry".
            } while (attempts > 0 && attempts < 5);
            if (retVal != 0) throw new InvalidOperationException("Failed to select file: 0x" + retVal.ToString("X"));
            if (rspLen < 0)
            {
                throw new InvalidOperationException("Failed to select file SW1=? SW2=?");
            }
            if (rsp[0] != 0x90 || rsp[1] != 0x0)
            {
                throw new InvalidOperationException("Failed to select file SW1=" + rsp[0].ToString("X") + " SW2=" + rsp[1].ToString("X"));
            }

            int offset = 0;
            cmd = new byte[CMD_READ_BINARY.Length];
            Array.Copy(CMD_READ_BINARY, cmd, cmd.Length);
            MemoryStream bytes = new MemoryStream();
            do
            {
                cmd[2] = (byte)(offset >> 8);
                cmd[3] = (byte)(offset & 0xFF);

                rspLen = rsp.Length;
                retVal = 0x8010002F;//retry
                attempts = 0;
                do
                {
                    retVal = WinSCard.SCardTransmit(handler, ioReq, cmd, cmd.Length, null, rsp, ref rspLen);
                    if (retVal == 0x80100017) throw new ReaderException("The card reader isn't available any more");
                    if (retVal == 0x80100069) throw new NoCardException("The card has been removed");
                    if (retVal == 0x8010002F) attempts++; //"comm error, retry".
                } while (attempts > 0 && attempts < 5);
                if (retVal != 0) throw new InvalidOperationException("Failed to read bytes: " + retVal.ToString("X"));
                if (rspLen < 0)
                {
                    throw new InvalidOperationException("Failed to read bytes SW1=? SW2=?");
                }
                if (rsp[rspLen - 2] == 0x6B && rsp[rspLen - 1] == 0x0)
                {
                    break; // Finished, there where no more bytes
                }
                if (rsp[rspLen - 2] == 0x6C)
                {
                    cmd[4] = rsp[1];// Almost finished, reading less
                    continue;
                }
                if (rsp[rspLen - 2] != 0x90 || rsp[rspLen - 1] != 0x0)
                {
                    throw new InvalidOperationException("Failed to read bytes SW1=" + rsp[0].ToString("X") + " SW2=" + rsp[1].ToString("X"));
                }

                bytes.Write(rsp, 0, rspLen - 2);
                offset += rspLen - 2;
            } while (rspLen == 258 || rsp[0] == 0x6C);

            return bytes.ToArray();
        }
        finally
        {
            retVal = WinSCard.SCardEndTransaction(handler, ECardDisposition.SCARD_LEAVE_CARD);
            if (retVal == 0x80100017) throw new ReaderException("The card reader isn't available any more");
            if (retVal == 0x80100069) throw new NoCardException("The card has been removed");
            if (retVal != 0) throw new InvalidOperationException("Failed to end transaction: 0x" + retVal.ToString("X"));
        }
    }

    public void Close()
    {
        if (handler != null && !handler.IsInvalid && !handler.IsClosed) handler.Dispose();
        handler = null;
    }

    public void Dispose() => Dispose(true);

    protected virtual void Dispose(bool managed) => Close();
}
