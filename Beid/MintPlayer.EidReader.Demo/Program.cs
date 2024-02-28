// See https://aka.ms/new-console-template for more information
using MintPlayer.EidReader;

Console.WriteLine("Hello, World!");

var worker = new EidWorker();
var cts = new CancellationTokenSource();

worker.ReaderAttached += (sender, e) => Console.WriteLine("Reader attached: {0}", e.ReaderName);
worker.ReaderDetached += (sender, e) => Console.WriteLine("Reader detached: {0}", e.ReaderName);
worker.CardInsert += (sender, e) =>
{
    e.Card.Open();

    if (e.Card is EidCard eid)
        Console.WriteLine($"""
            Card inserted:
            {eid.Identity.FirstNames} {eid.Identity.Surname}
            {eid.Identity.NationalNr}
            {eid.Identity.Gender}
            {eid.Identity.CardNr}
            {eid.Identity.DateOfBirth:dd/MM/yyyy}
            {eid.Identity.LocationOfBirth}
            {eid.Identity.Nationality}
            """);
    else
        Console.WriteLine("Card inserted");
};
worker.CardRemoved += (sender, e) => Console.WriteLine("Card removed from reader: {0}", e.ReaderName);

await worker.Run(MintPlayer.EidReader.Native.Enums.EReaderScope.System, cts);

