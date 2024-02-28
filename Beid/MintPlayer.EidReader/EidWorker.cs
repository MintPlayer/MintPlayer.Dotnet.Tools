﻿using MintPlayer.EidReader.Events.EventArgs;
using MintPlayer.EidReader.Native;
using MintPlayer.EidReader.Native.Enums;
using MintPlayer.EidReader.Native.Structs;
using System.Xml;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace MintPlayer.EidReader;

public class EidWorker
{
    public async Task Run(EReaderScope scope, CancellationTokenSource cancellationToken)
    {
        int numberOfReaders = 0;
        var context = CreateCardContext(scope);
        //var readerStateList = new List<SCARD_READERSTATE>();
        //var readerStateArray = RecomputeReaderStateArray(readerStateList);
        SCARD_READERSTATE[] readerStateArray = [
            new SCARD_READERSTATE() { szReader = @"\\?PnP?\Notification", dwCurrentState = EReaderState.SCARD_STATE_UNKNOWN }
        ];

        while (true)
        {
            if (cancellationToken.IsCancellationRequested) break;
            await Task.Delay(1000);


            // Detect new/old readers
            var getNumberOfReadersStatus = WinSCard.SCardListReaders(context, null, null, ref numberOfReaders);
            switch (getNumberOfReadersStatus)
            {
                case 0:
                    var readers = new char[numberOfReaders];
                    var getReaderPointersStatus = WinSCard.SCardListReaders(context, null, readers, ref numberOfReaders);
                    //Console.WriteLine("readers: {0}", new string(readers));
                    var currentlyConnected = new string(readers).Split('\0', StringSplitOptions.RemoveEmptyEntries);
                    var alreadyConnected = readerStateArray.SkipLast(1).Select(r => r.szReader).ToList();

                    var newReaders = currentlyConnected.Except(alreadyConnected).ToList();
                    var oldReaders = alreadyConnected.Except(currentlyConnected).ToList();

                    if (oldReaders.Any() || newReaders.Any())
                    {
                        readerStateArray = readerStateArray.SkipLast(1)
                            .Where(r => !oldReaders.Contains(r.szReader))
                            .Concat(newReaders.Select(r => new SCARD_READERSTATE() { szReader = r, dwCurrentState = EReaderState.SCARD_STATE_UNKNOWN }))
                            .Concat([readerStateArray.Last()])
                            .ToArray();

                        // Fire events
                        foreach (var oldReader in oldReaders)
                            ReaderDetached?.Invoke(this, new ReaderDetachedEventArgs { ReaderName = oldReader });
                        foreach (var newReader in newReaders)
                            ReaderAttached?.Invoke(this, new ReaderAttachedEventArgs { ReaderName = newReader });
                    }

                    //// Update the list
                    //Readers.AddRange(newReaders.Select(r => new ConnectedReader { ReaderName = r }));
                    //foreach (var r in Readers.Where(x => oldReaders.Contains(x.ReaderName)).ToList())
                    //    Readers.Remove(r);
                    ////readerStateList.AddRange(newReaders.Select(r => new SCARD_READERSTATE { szReader = r, dwEventState =  }))

                    //// If readers were attached/detached, update the structs array.
                    //if (oldReaders.Any() || newReaders.Any())
                    //    readerStateArray = readerStateList.ToArray();


                    break;
                case 0x8010001E:
                    // Service stopped
                    DisposeCardContext(context);
                    context = CreateCardContext(scope);

                    // Update the list
                    var previouslyConnected1 = readerStateArray.SkipLast(1).ToArray();
                    readerStateArray = [readerStateArray.Last()];

                    // Fire events
                    foreach (var oldReader in previouslyConnected1)
                        ReaderDetached?.Invoke(this, new ReaderDetachedEventArgs { ReaderName = oldReader.szReader });

                    //// If readers were attached/detached, update the structs array.
                    //if (previouslyConnected1.Any())
                    //    readerStateArray = readerStateList.ToArray();

                    break;
                case 0x8010002E:
                    // No readers
                    // Update the list
                    var previouslyConnected2 = readerStateArray.SkipLast(1).ToArray();
                    readerStateArray = [readerStateArray.Last()];

                    // Fire events
                    foreach (var oldReader in previouslyConnected2)
                        ReaderDetached?.Invoke(this, new ReaderDetachedEventArgs { ReaderName = oldReader.szReader });

                    //// If readers were attached/detached, update the structs array.
                    //if (previouslyConnected2.Any())
                    //    readerStateArray = readerStateList.ToArray();

                    break;
                default:
                    throw new InvalidOperationException("Failed to list readers (length): 0x" + getNumberOfReadersStatus.ToString("X"));
            }


            // Read the new states from the readers
            for (int i = 0; i < readerStateArray.Length; i++)
                readerStateArray[i].dwCurrentState = readerStateArray[i].dwEventState;

            var statusChangeStatus = WinSCard.SCardGetStatusChange(context, 1000, readerStateArray, readerStateArray.Length);
            switch (statusChangeStatus)
            {
                case 0x8010000A:
                    continue; //timeout
                case 0:
                    break;
                default:
                    //todo:logging ("Failed to update the status: 0x" + retVal.ToString("X"))
                    throw new Exception("Failed to update the status");
            }

            for (var i = 0; i < readerStateArray.Length; i++)
            {
                var state = readerStateArray[i];
                if (CheckFlag(state.dwEventState, EReaderState.SCARD_STATE_CHANGED))
                {
                    // Check for insert
                    var isInsert =
                        (CheckFlag(state.dwCurrentState, EReaderState.SCARD_STATE_EMPTY) || (state.dwCurrentState == EReaderState.SCARD_STATE_UNAWARE))
                        && CheckFlag(state.dwEventState, EReaderState.SCARD_STATE_PRESENT);
                    var isRemoved =
                        CheckFlag(state.dwCurrentState, EReaderState.SCARD_STATE_PRESENT)
                        && CheckFlag(state.dwEventState, EReaderState.SCARD_STATE_EMPTY);

                    if (isInsert)
                        CardInsert?.Invoke(this, new CardInsertEventArgs { Card = CreateCard(context, state), ReaderName = state.szReader });

                    if (isRemoved)
                        CardRemoved?.Invoke(this, new CardRemovedEventArgs { ReaderName = state.szReader });
                }
            }
        }
    }

    private bool CheckFlag(EReaderState state, EReaderState flag) => (state & flag) == flag;

    private Card CreateCard(CardContextSafeHandler context, SCARD_READERSTATE readerstate)
    {
        if (!CheckFlag(readerstate.dwEventState, EReaderState.SCARD_STATE_PRESENT))
            throw new ArgumentException("No card is present in the reader");

        var atr = new byte[readerstate.cbAtr];
        Buffer.BlockCopy(readerstate.rgbAtr, 0, atr, 0, readerstate.cbAtr);
        return EidCard.IsEid(atr) ? new EidCard(context, readerstate.szReader, atr) : new Card(context, readerstate.szReader, atr);
    }

    //private List<ConnectedReader> Readers = new List<ConnectedReader>();

    public delegate void ReaderAttachedHandler(object sender, ReaderAttachedEventArgs e);
    public event ReaderAttachedHandler? ReaderAttached;
    public delegate void ReaderDetachedHandler(object sender, ReaderDetachedEventArgs e);
    public event ReaderDetachedHandler? ReaderDetached;
    public delegate void CardInsertEventHandler(object sender, CardInsertEventArgs e);
    public event CardInsertEventHandler? CardInsert;
    public delegate void CardRemovedEventHandler(object sender, CardRemovedEventArgs e);
    public event CardRemovedEventHandler? CardRemoved;

    private CardContextSafeHandler CreateCardContext(EReaderScope scope)
    {
        switch (scope)
        {
            case EReaderScope.Null:
                return new CardContextSafeHandler(IntPtr.Zero);
            case EReaderScope.System:
                var status1 = WinSCard.SCardEstablishContext(EContextScope.SCARD_SCOPE_SYSTEM, IntPtr.Zero, IntPtr.Zero, out var context1);
                if (status1 != 0) throw new InvalidOperationException("Failed to create static context for reader: 0x" + status1.ToString("X"));
                return context1;
            case EReaderScope.User:
                var status2 = WinSCard.SCardEstablishContext(EContextScope.SCARD_SCOPE_USER, IntPtr.Zero, IntPtr.Zero, out var context2);
                if (status2 != 0) throw new InvalidOperationException("Failed to create static context for reader: 0x" + status2.ToString("X"));
                return context2;
            default:
                throw new InvalidOperationException();
        }
    }

    //private SCARD_READERSTATE[] RecomputeReaderStateArray(List<SCARD_READERSTATE> list)
    //{
    //    return list.Concat([new SCARD_READERSTATE() { szReader = @"\\?PnP?\Notification", dwEventState = (EReaderState)(list.Count << 16) }]).ToArray();
    //}

    private void DisposeCardContext(CardContextSafeHandler context)
    {
        context.Close();
        context.Dispose();
    }
}
