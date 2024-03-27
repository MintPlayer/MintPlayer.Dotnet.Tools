// See https://aka.ms/new-console-template for more information
using MintPlayer.EidReader;

Console.WriteLine("Hello, World!");

var worker = new EidWorker();
var cts = new CancellationTokenSource();

worker.ReaderAttached += (sender, e) => Console.WriteLine($"Reader attached: {e.ReaderName}");
worker.ReaderDetached += (sender, e) => Console.WriteLine($"Reader detached: {e.ReaderName}");
worker.CardInsert += (sender, e) =>
{
    try
    {
        e.Card.Open();

        if (e.Card is EidCard eid)
        {
            Console.WriteLine($"""
                Card inserted into reader: {e.ReaderName}
                {eid.Identity.FirstNames} {eid.Identity.Surname}
                {eid.Identity.NationalNr}
                {eid.Identity.Gender}
                {eid.Identity.CardNr}
                {eid.Identity.DateOfBirth:dd/MM/yyyy}
                {eid.Identity.LocationOfBirth}
                {eid.Identity.Nationality}
                """);
            //CspParameters csp = new CspParameters(1, "", "",);
        }
        else
            Console.WriteLine($"Card inserted into reader: {e.ReaderName}");
    }
    catch (Exception)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Could not read data from the card");
        Console.ResetColor();
    }
};
worker.CardRemoved += (sender, e) => Console.WriteLine($"Card removed from reader: {e.ReaderName}");

await worker.Run(MintPlayer.EidReader.Native.Enums.EReaderScope.System, cts);

