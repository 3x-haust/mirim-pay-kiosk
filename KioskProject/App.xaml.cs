using System.Windows;
using KioskProject.Services;
using KioskProject.ViewModels;

namespace KioskProject;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        IDataService dataService = new JsonDataService();
        var viewModel = KioskComposition.Create(dataService);
        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();
    }
}

internal static class KioskComposition
{
    internal static MainViewModel Create(IDataService dataService)
    {
        ArgumentNullException.ThrowIfNull(dataService);
        return new MainViewModel(dataService);
    }
}
