using BenchmarkDotNet.Running;

namespace DirectorPrompt.Benchmarks;

public static class Program
{
    public static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
