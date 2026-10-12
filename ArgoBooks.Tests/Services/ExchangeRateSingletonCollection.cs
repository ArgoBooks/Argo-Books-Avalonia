using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>A write from a test running alongside renumbers the conversion queue this one is working through, so these take their turn instead.</summary>
[CollectionDefinition("ExchangeRateSingleton", DisableParallelization = true)]
public class ExchangeRateSingletonCollection;
