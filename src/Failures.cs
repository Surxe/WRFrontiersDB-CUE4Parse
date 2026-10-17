using System.Collections.Concurrent;

namespace BatchExport
{
    /// <summary>
    /// Every export failure of the run, so none is only logged. The run exits non-zero when any
    /// were recorded, and an out-of-memory error aborts it at once.
    /// </summary>
    public static class Failures
    {
        public const int ExitClean = 0;
        public const int ExitFailures = 1;
        public const int ExitOutOfMemory = 3;

        /// <summary>How many failures the summary lists before it only counts them.</summary>
        private const int MaxListed = 20;

        public sealed record Failure(string Phase, string Path, string Message);

        private static readonly ConcurrentQueue<Failure> _all = new();
        private static int _aborting;

        /// <summary>The phase failures are recorded under. Phases run one after another.</summary>
        public static string Phase { get; set; } = "startup";

        public static int Count => _all.Count;

        public static void Record(string path, string message)
        {
            _all.Enqueue(new Failure(Phase, path, message));
        }

        public static void Record(string path, Exception ex)
        {
            Record(path, ex.InnerException == null ? ex.Message : $"{ex.Message} ({ex.InnerException.Message})");
        }

        /// <summary>
        /// Aborts with ExitOutOfMemory on the first OutOfMemoryException anywhere, before any catch
        /// (ours or CUE4Parse's) can swallow it. Under a GC heap limit an OOM means the budget is too
        /// small; carrying on would only leave files missing.
        /// </summary>
        public static void AbortOnOutOfMemory()
        {
            AppDomain.CurrentDomain.FirstChanceException += (_, args) =>
            {
                if (args.Exception is not OutOfMemoryException)
                    return;
                if (Interlocked.Exchange(ref _aborting, 1) == 1)
                    return;
                // The message may itself fail to allocate; the exit must happen regardless.
                try { Console.Error.WriteLine($"Out of memory during phase '{Phase}': aborting (exit {ExitOutOfMemory})."); }
                catch { }
                Environment.Exit(ExitOutOfMemory);
            };
        }

        /// <summary>Prints the failure counts per phase and the first failures, and returns the exit code.</summary>
        public static int Summarize()
        {
            if (_all.IsEmpty)
            {
                Console.WriteLine("Export finished with no failures.");
                return ExitClean;
            }

            var failures = _all.ToArray();
            Console.WriteLine($"Export finished with {failures.Length} failure(s):");
            foreach (var phase in failures.GroupBy(f => f.Phase))
                Console.WriteLine($"  {phase.Key}: {phase.Count()}");

            Console.WriteLine($"First {Math.Min(MaxListed, failures.Length)}:");
            foreach (var failure in failures.Take(MaxListed))
                Console.WriteLine($"  [{failure.Phase}] {failure.Path}: {failure.Message}");

            return ExitFailures;
        }
    }
}
