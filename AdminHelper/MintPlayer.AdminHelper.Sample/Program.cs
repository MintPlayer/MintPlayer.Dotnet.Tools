using MintPlayer.AdminHelper;

AdminHelper.EnsureRunningAsAdmin();


Console.WriteLine("Running with administrator privileges!");
foreach (var arg in Environment.GetCommandLineArgs())
    Console.WriteLine($"Arg: {arg}");