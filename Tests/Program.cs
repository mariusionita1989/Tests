using System;
using System.Diagnostics;
using System.Text;
using BenchmarkDotNet.Running;
using Tests;
using Tests.Benchmarks;

public class Program
{
    public static void Main()
    {
        BenchmarkRunner.Run<TestsDemo>();
    }
}