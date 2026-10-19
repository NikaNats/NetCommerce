// xUnit v3 discards Console output unless the assembly opts in. Every load suite
// reports its latency distribution and counters via Console.WriteLine; without
// this, a red soak run in CI shows only the failed assertion and nothing to diagnose.
[assembly: Xunit.CaptureConsole]
