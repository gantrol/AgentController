using System.Reflection;
using CodexController.Composition;

namespace CodexController.Tests;

public sealed class CompositionBoundaryTests
{
    [Fact]
    public void MainWindowConsumesOnlyItsPresentationDependencyBundle()
    {
        var constructor = Assert.Single(
            typeof(MainWindow).GetConstructors(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic));
        var parameter = Assert.Single(constructor.GetParameters());

        Assert.Equal(
            typeof(MainWindowDependencies),
            parameter.ParameterType);
        Assert.Null(typeof(MainWindow).Assembly.GetType(
            "CodexController.Services.AppServices"));
    }
}
