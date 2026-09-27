using System.Windows;

namespace CodexMicro.Storybook;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var application = new Application();
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/AgentController.MicroSurface.Wpf;component/MicroSurfaceResources.xaml"),
        });
        application.Run(new StorybookWindow());
    }
}
