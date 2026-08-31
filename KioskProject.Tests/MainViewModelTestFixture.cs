using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Input;

namespace KioskProject.Tests;

internal sealed class MainViewModelFixture
{
    private static readonly Assembly ProductAssembly = ProductAssemblyFixture.Assembly;

    private MainViewModelFixture(object viewModel, MainDataServiceProxy dataService, object menuItem)
    {
        ViewModel = viewModel;
        DataService = dataService;
        MenuItem = menuItem;
    }

    internal object ViewModel { get; }

    internal MainDataServiceProxy DataService { get; }

    internal object MenuItem { get; }

    internal object Menu => Read<object>(ViewModel, "Menu");

    internal object Cart => Read<object>(ViewModel, "Cart");

    internal static MainViewModelFixture Create(int stock = 20)
    {
        var item = CreateProduct("KioskProject.Models.MenuItem");
        Write(item, "Id", 1);
        Write(item, "Name", "Americano");
        Write(item, "Price", 2500);
        Write(item, "Category", "Coffee");
        Write(item, "ImagePath", "Images/americano.png");
        Write(item, "Stock", stock);

        var proxy = DispatchProxy.Create(
            RequireType("KioskProject.Services.IDataService"),
            typeof(MainDataServiceProxy));
        var service = Assert.IsAssignableFrom<MainDataServiceProxy>(proxy);
        service.Configure(new[] { item });
        var type = RequireType("KioskProject.ViewModels.MainViewModel");
        var constructor = Assert.IsAssignableFrom<ConstructorInfo>(type.GetConstructor(new[]
        {
            RequireType("KioskProject.Services.IDataService")
        }));
        var viewModel = Assert.IsAssignableFrom<object>(constructor.Invoke(new[] { service.Proxy }));
        return new MainViewModelFixture(viewModel, service, item);
    }

    internal void Add(int quantity = 1) => Invoke(Menu, "AddToCart", MenuItem, quantity);

    internal void Execute(string commandName, object? parameter = null) =>
        Command(commandName).Execute(parameter);

    internal ICommand Command(string commandName) => Read<ICommand>(ViewModel, commandName);

    internal string State(string propertyName) => Read<object>(ViewModel, propertyName).ToString()
        ?? throw new Xunit.Sdk.XunitException($"{propertyName} has no string representation.");

    internal IReadOnlyList<object> CartItems() =>
        Assert.IsAssignableFrom<IEnumerable>(Read<object>(Cart, "CartItems")).Cast<object>().ToArray();

    internal static IReadOnlyList<object> OrderItems(object order) =>
        Assert.IsAssignableFrom<IEnumerable>(Read<object>(order, "Items")).Cast<object>().ToArray();

    internal static List<string?> Observe(object target)
    {
        var notifications = new List<string?>();
        Assert.IsAssignableFrom<INotifyPropertyChanged>(target).PropertyChanged +=
            (_, args) => notifications.Add(args.PropertyName);
        return notifications;
    }

    internal static T Read<T>(object target, string propertyName) =>
        Assert.IsAssignableFrom<T>(Assert.IsAssignableFrom<PropertyInfo>(
            target.GetType().GetProperty(propertyName)).GetValue(target));

    internal static object? ReadNullable(object target, string propertyName) =>
        Assert.IsAssignableFrom<PropertyInfo>(target.GetType().GetProperty(propertyName)).GetValue(target);

    internal static void Write(object target, string propertyName, object? value) =>
        Assert.IsAssignableFrom<PropertyInfo>(target.GetType().GetProperty(propertyName)).SetValue(target, value);

    internal static void Invoke(object target, string methodName, params object?[] arguments) =>
        Assert.IsAssignableFrom<MethodInfo>(target.GetType().GetMethod(methodName)).Invoke(target, arguments);

    internal static Type RequireType(string fullName) =>
        Assert.IsAssignableFrom<Type>(ProductAssembly.GetType(fullName));

    private static object CreateProduct(string fullName) =>
        Assert.IsAssignableFrom<object>(Activator.CreateInstance(RequireType(fullName)));
}

public class MainDataServiceProxy : DispatchProxy
{
    private IReadOnlyList<object> _menus = Array.Empty<object>();

    internal object Proxy => this;

    internal int SaveAttempts { get; private set; }

    internal List<object> SavedOrders { get; } = new();

    internal Action<object>? OnSave { get; set; }

    internal Exception? SaveException { get; set; }

    internal void Configure(IReadOnlyList<object> menus) => _menus = menus;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? arguments)
    {
        Assert.NotNull(targetMethod);
        switch (targetMethod.Name)
        {
            case "LoadMenus":
                var list = Assert.IsAssignableFrom<IList>(Activator.CreateInstance(targetMethod.ReturnType));
                foreach (var menu in _menus)
                {
                    list.Add(menu);
                }

                return list;
            case "SaveOrder":
                var order = Assert.IsAssignableFrom<object>(Assert.Single(arguments ?? Array.Empty<object>()));
                SaveAttempts++;
                OnSave?.Invoke(order);
                if (SaveException is not null)
                {
                    throw SaveException;
                }

                SavedOrders.Add(order);
                return null;
            default:
                throw new MissingMethodException(targetMethod.Name);
        }
    }
}
