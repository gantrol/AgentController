using Xunit;

namespace CodexMicro.Desktop.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfUiCollection
{
    public const string Name = "WPF UI";
}
