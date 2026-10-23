using Somning.Bench;

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: Somning.Bench <run|report|check> [options]");
    return 2;
}

return args[0] switch
{
    "run" => await BenchRunner.RunAsync(args[1..]),
    "report" => await ReportWriter.RunAsync(args[1..]),
    "check" => await ResultsCheck.RunAsync(args[1..]),
    _ => 2
};
