using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OAE.App.ViewModels;
using OAE.App.Views;
using OAE.Core.Config;
using OAE.Core.Store;

namespace OAE.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var configPath = OaeConfig.DefaultPath;
            var config = OaeConfig.Load(configPath);
            var store = new HotSwapStore(new StubStore());

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(store, config, configPath),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
