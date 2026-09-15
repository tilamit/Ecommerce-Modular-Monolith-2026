using Xunit;

// Integration tests spin up real hosts. Serilog's Log.Logger is process-global and is
// frozen when a host builds, so two hosts starting concurrently race on it ("The logger is
// already frozen"). Running collections sequentially also keeps LocalDB contention and
// per-run database lifetimes predictable.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
