// No Handwired source. The phase 5 carried item "the integer evaluator CPU declaration checks cost time"
// (task 3 of the phase 6 plan). The source checks of GpuEvaluator run in parallel and before the device
// lock. They give the message of the first bad table, as the checks in order give. The test output gives
// the check time in order (the phase 5 form) and in parallel. Also the constant tables of the storage
// buffer on the device.

using System.Diagnostics;
using Sharpwired.Core;

namespace Sharpwired.Gpu.Tests;

public sealed partial class BatchEvaluatorTests
{
    [Fact]
    public void The_parallel_source_checks_give_the_message_of_the_checks_in_order_and_the_output_gives_both_times()
    {
        const int TableCount = 2_000_000;
        var (circuit, program) = BatchPrograms.Create();
        var sources = BatchPrograms.Tables(TableCount, 0xc4ec);

        // Warm up both paths, then measure each one three times and keep the fastest time.
        double Measure(bool parallel)
        {
            BatchArguments.CheckTables(circuit, program, sources, TableCount, declarations: true, parallel);
            var best = double.MaxValue;
            for (var round = 0; round < 3; round++)
            {
                var watch = Stopwatch.StartNew();
                BatchArguments.CheckTables(circuit, program, sources, TableCount, declarations: true, parallel);
                best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
            }
            return best;
        }
        var inOrder = Measure(parallel: false);
        var inParallel = Measure(parallel: true);
        output.WriteLine($"[checks] {TableCount} tables of {program.SourceSize} cells, ranges and declarations: in order {inOrder:F1} ms, in parallel {inParallel:F1} ms on {Environment.ProcessorCount} processors");

        // A declaration fault at table 10 and a range fault at table 1,500,000. The checks in order check
        // the ranges of each table first, so both paths give the range fault.
        var bad = (float[])sources.Clone();
        bad[(10 * 6) + 3] = 0.5f;
        bad[(1_500_000 * 6) + 2] = 1.5f;
        string Message(float[] cells, bool parallel, bool declarations = true) =>
            Assert.Throws<ArgumentException>(() => BatchArguments.CheckTables(circuit, program, cells, TableCount, declarations, parallel)).Message;
        Assert.Equal(Message(bad, parallel: false), Message(bad, parallel: true));
        Assert.Equal("source wire 2 is outside its assumed range", Message(bad, parallel: true));

        // Two declaration faults: both paths give the first one.
        bad[(1_500_000 * 6) + 2] = 0.5f;
        bad[(900_000 * 6) + 3] = 2.5f;
        Assert.Equal("source wire 3 is declared on the grid 1 and must be a multiple of it, got 0.5", Message(bad, parallel: true));
        Assert.Equal(Message(bad, parallel: false), Message(bad, parallel: true));

        // With no declaration checks (the f32 writer), the declaration faults pass.
        BatchArguments.CheckTables(circuit, program, bad, TableCount, declarations: false, parallel: true);
    }

    // A thread holds the lock of the device. A call with a bad table still throws: its checks run before
    // the lock.
    [Theory]
    [MemberData(nameof(Writers))]
    public void A_bad_table_throws_while_another_thread_holds_the_device_lock(GpuWriter writer)
    {
        var (circuit, program) = BatchPrograms.Create();
        var bad = BatchPrograms.Tables(100_000, 0x10c4);
        bad[(70_000 * 6) + 2] = 1.5f;
        fixture.ForEachDevice(output, device =>
        {
            using var evaluator = GpuEvaluator.Create(device, circuit, program, writer);
            using var held = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var holder = Task.Factory.StartNew(() =>
            {
                lock (device.Sync)
                {
                    held.Set();
                    release.Wait();
                }
            }, TaskCreationOptions.LongRunning);
            held.Wait();
            try
            {
                var call = Task.Run(() => evaluator.Evaluate(bad, 100_000));
                Assert.True(Task.WaitAny([call], TimeSpan.FromSeconds(30)) == 0, "the call waited for the device lock");
                var error = Assert.Throws<AggregateException>(() => call.Wait());
                var inner = Assert.IsType<ArgumentException>(error.InnerException);
                Assert.Equal("source wire 2 is outside its assumed range", inner.Message);
            }
            finally
            {
                release.Set();
                holder.Wait();
            }
        });
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public void An_index_read_of_a_constant_table_on_the_device_equals_the_reference(GpuWriter writer)
    {
        var (circuit, program, _, _, _) = WriterTests.ConstantTableProgram();
        var sources = Enumerable.Range(-12, 25).Select(value => (float)value).ToArray();
        fixture.ForEachDevice(output, device =>
        {
            using var evaluator = GpuEvaluator.Create(device, circuit, program, writer);
            Assert.Equal(5, evaluator.Module.ConstantTable.Length);
            var outputs = evaluator.Evaluate(sources, sources.Length);
            var (compared, _) = GpuChecks.Compare(device, circuit, program, sources, sources.Length, outputs, SameNumericValue(writer));
            Assert.Equal(sources.Length, compared);
        });
    }
}
