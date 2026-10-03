using Xunit;

namespace Balancia.Storage.Tests;

// Serilog's application logger is process-wide; isolate tests that temporarily replace it.
[CollectionDefinition("Logging", DisableParallelization = true)]
public sealed class LoggingCollection
{
}
