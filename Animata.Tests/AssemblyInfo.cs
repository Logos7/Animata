using Xunit;

// Testy fizyki są deterministyczne tylko bez współbieżności klas.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
