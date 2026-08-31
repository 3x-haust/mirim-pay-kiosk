using System.Collections;
using System.Reflection;
using System.Windows.Input;

namespace KioskProject.Tests;

public sealed partial class MenuViewModelTests
{
    private static readonly Assembly ProductAssembly = ProductAssemblyFixture.Assembly;

    [Fact]
    public void Constructor_loads_catalog_once_and_exposes_ready_state_when_data_exists()
    {
        // Given
        var first = CreateMenu(1, "Drinks", 20);
        var second = CreateMenu(2, "Food", 5);
        var service = CreateDataService(new[] { first, second });
        var cart = CreateCart();

        // When
        var menu = CreateMenuViewModel(service.Proxy, cart);

        // Then
        Assert.Equal(1, service.LoadCount);
        Assert.Equal(new[] { first, second }, ReadItems(menu, "Menus"));
        Assert.Equal(new[] { first, second }, ReadItems(menu, "FilteredMenus"));
        Assert.False(Read<bool>(menu, "IsEmpty"));
        Assert.False(Read<bool>(menu, "HasLoadError"));
        Assert.True(Read<bool>(menu, "CanPurchase"));
        Assert.Equal(string.Empty, Read<string>(menu, "Status"));
    }

    [Fact]
    public void Constructor_exposes_valid_empty_state_when_catalog_is_empty()
    {
        // Given
        var service = CreateDataService(Array.Empty<object>());

        // When
        var menu = CreateMenuViewModel(service.Proxy, CreateCart());

        // Then
        Assert.Equal(1, service.LoadCount);
        Assert.Empty(ReadItems(menu, "Menus"));
        Assert.Empty(ReadItems(menu, "FilteredMenus"));
        Assert.Equal(new[] { "All" }, ReadStrings(menu, "Categories"));
        Assert.True(Read<bool>(menu, "IsEmpty"));
        Assert.False(Read<bool>(menu, "HasLoadError"));
        Assert.False(Read<bool>(menu, "CanPurchase"));
        Assert.Equal(string.Empty, Read<string>(menu, "Status"));
    }

    [Fact]
    public void Constructor_exposes_fatal_error_without_partial_catalog_when_load_throws()
    {
        // Given
        var service = CreateDataService(
            new[] { CreateMenu(1, "Ignored", 1) },
            new InvalidDataException("broken catalog"));
        var cart = CreateCart();

        // When
        var menu = CreateMenuViewModel(service.Proxy, cart);

        // Then
        Assert.Equal(1, service.LoadCount);
        Assert.Empty(ReadItems(menu, "Menus"));
        Assert.Empty(ReadItems(menu, "FilteredMenus"));
        Assert.Empty(ReadStrings(menu, "Categories"));
        Assert.False(Read<bool>(menu, "IsEmpty"));
        Assert.True(Read<bool>(menu, "HasLoadError"));
        Assert.False(Read<bool>(menu, "CanPurchase"));
        Assert.Equal("load-error", Read<string>(menu, "Status"));
        Assert.Equal("broken catalog", Read<string>(menu, "LoadErrorMessage"));
    }

    [Fact]
    public void Categories_are_stable_unique_and_preserve_first_seen_order()
    {
        // Given
        var menus = new[]
        {
            CreateMenu(1, "Coffee", 1),
            CreateMenu(1, "Tea", 1),
            CreateMenu(3, "Coffee", 1),
            CreateMenu(4, "Food", 1),
            CreateMenu(5, "Tea", 1)
        };

        // When
        var menu = CreateMenuViewModel(CreateDataService(menus).Proxy, CreateCart());

        // Then
        Assert.Equal(new[] { "All", "Coffee", "Tea", "Food" }, ReadStrings(menu, "Categories"));
    }

    [Fact]
    public void Filter_and_reset_change_filtered_view_without_mutating_source_or_collection_identity()
    {
        // Given
        var coffee = CreateMenu(1, "Drinks", 20);
        var cake = CreateMenu(2, "Food", 5);
        var tea = CreateMenu(3, "Drinks", 10);
        var menu = CreateMenuViewModel(CreateDataService(new[] { coffee, cake, tea }).Proxy, CreateCart());
        var source = Read<object>(menu, "Menus");
        var filtered = Read<object>(menu, "FilteredMenus");

        // When
        Invoke(menu, "FilterByCategory", "Drinks");

        // Then
        Assert.Same(source, Read<object>(menu, "Menus"));
        Assert.Same(filtered, Read<object>(menu, "FilteredMenus"));
        Assert.Equal(new[] { coffee, cake, tea }, ReadItems(menu, "Menus"));
        Assert.Equal(new[] { coffee, tea }, ReadItems(menu, "FilteredMenus"));
        Assert.Equal("Drinks", Read<string>(menu, "SelectedCategory"));

        // When
        Invoke(menu, "ResetFilter");

        // Then
        Assert.Same(source, Read<object>(menu, "Menus"));
        Assert.Same(filtered, Read<object>(menu, "FilteredMenus"));
        Assert.Equal(new[] { coffee, cake, tea }, ReadItems(menu, "Menus"));
        Assert.Equal(new[] { coffee, cake, tea }, ReadItems(menu, "FilteredMenus"));
        Assert.Equal("All", Read<string>(menu, "SelectedCategory"));
    }

    [Fact]
    public void All_category_resets_an_active_filter()
    {
        // Given
        var menus = new[]
        {
            CreateMenu(1, "Drinks", 20),
            CreateMenu(2, "Food", 5)
        };
        var menu = CreateMenuViewModel(CreateDataService(menus).Proxy, CreateCart());
        Invoke(menu, "FilterByCategory", "Food");

        // When
        Invoke(menu, "FilterByCategory", "All");

        // Then
        Assert.Equal(menus, ReadItems(menu, "FilteredMenus"));
        Assert.Equal("All", Read<string>(menu, "SelectedCategory"));
    }

    [Fact]
    public void Add_command_uses_injected_shared_cart_and_exact_add_api()
    {
        // Given
        var item = CreateMenu(1, "Drinks", 20);
        var cart = CreateCart();
        var menu = CreateMenuViewModel(CreateDataService(new[] { item }).Proxy, cart);
        var command = Read<ICommand>(menu, "AddCommand");

        // When
        command.Execute(item);

        // Then
        Assert.Same(cart, Read<object>(menu, "Cart"));
        var cartItem = Assert.Single(ReadItems(cart, "CartItems"));
        Assert.Same(item, Read<object>(cartItem, "Menu"));
        Assert.Equal(1, Read<int>(cartItem, "Quantity"));
        Assert.Equal(2500, Read<int>(cart, "TotalPrice"));
    }

    [Fact]
    public void Purchasing_is_disabled_and_preserves_shared_cart_after_load_failure()
    {
        // Given
        var cart = CreateCart();
        var existing = CreateMenu(7, "Old", 2);
        Write(existing, "Price", 100);
        Invoke(cart, "AddToCart", existing, 1);
        var failed = CreateDataService(Array.Empty<object>(), new IOException("offline"));
        var menu = CreateMenuViewModel(failed.Proxy, cart);
        var command = Read<ICommand>(menu, "AddCommand");

        // When
        command.Execute(existing);

        // Then
        Assert.False(command.CanExecute(existing));
        Assert.Single(ReadItems(cart, "CartItems"));
        Assert.Equal(100, Read<int>(cart, "TotalPrice"));
        Assert.Equal("load-error", Read<string>(menu, "Status"));
    }

    private static object CreateMenuViewModel(object service, object cart)
    {
        var type = RequireType("KioskProject.ViewModels.MenuViewModel");
        var constructor = Assert.IsAssignableFrom<ConstructorInfo>(type.GetConstructor(new[]
        {
            RequireType("KioskProject.Services.IDataService"),
            RequireType("KioskProject.ViewModels.CartViewModel")
        }));
        return constructor.Invoke(new[] { service, cart });
    }

    private static MenuDataServiceProxy CreateDataService(
        IReadOnlyList<object> menus,
        Exception? loadException = null)
    {
        var proxy = DispatchProxy.Create(
            RequireType("KioskProject.Services.IDataService"),
            typeof(MenuDataServiceProxy));
        var configured = Assert.IsAssignableFrom<MenuDataServiceProxy>(proxy);
        configured.Configure(menus, loadException);
        return configured;
    }

    private static object CreateCart() =>
        Assert.IsAssignableFrom<object>(Activator.CreateInstance(RequireType("KioskProject.ViewModels.CartViewModel")));

    private static object CreateMenu(int id, string category, int stock)
    {
        var menu = Assert.IsAssignableFrom<object>(Activator.CreateInstance(RequireType("KioskProject.Models.MenuItem")));
        Write(menu, "Id", id);
        Write(menu, "Name", $"Menu {id}");
        Write(menu, "Price", 2500);
        Write(menu, "Category", category);
        Write(menu, "Stock", stock);
        return menu;
    }

    private static IReadOnlyList<object> ReadItems(object target, string property) =>
        Assert.IsAssignableFrom<IEnumerable>(Read<object>(target, property)).Cast<object>().ToArray();

    private static IReadOnlyList<string> ReadStrings(object target, string property) =>
        Assert.IsAssignableFrom<IEnumerable>(Read<object>(target, property)).Cast<string>().ToArray();

    private static void Invoke(object target, string method, params object?[] arguments) =>
        Assert.IsAssignableFrom<MethodInfo>(target.GetType().GetMethod(method)).Invoke(target, arguments);

    private static T Read<T>(object target, string property) =>
        Assert.IsAssignableFrom<T>(Assert.IsAssignableFrom<PropertyInfo>(target.GetType().GetProperty(property)).GetValue(target));

    private static void Write(object target, string property, object? value) =>
        Assert.IsAssignableFrom<PropertyInfo>(target.GetType().GetProperty(property)).SetValue(target, value);

    private static Type RequireType(string fullName) =>
        Assert.IsAssignableFrom<Type>(ProductAssembly.GetType(fullName));
}

public class MenuDataServiceProxy : DispatchProxy
{
    private IReadOnlyList<object> _menus = Array.Empty<object>();
    private Exception? _loadException;

    internal int LoadCount { get; private set; }

    internal object Proxy => this;

    internal void Configure(IReadOnlyList<object> menus, Exception? loadException)
    {
        _menus = menus;
        _loadException = loadException;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? arguments)
    {
        Assert.NotNull(targetMethod);
        if (targetMethod.Name == "LoadMenus")
        {
            LoadCount++;
            if (_loadException is not null)
            {
                throw _loadException;
            }

            var list = Assert.IsAssignableFrom<IList>(Activator.CreateInstance(targetMethod.ReturnType));
            foreach (var menu in _menus)
            {
                list.Add(menu);
            }

            return list;
        }

        if (targetMethod.Name == "SaveOrder")
        {
            return null;
        }

        throw new MissingMethodException(targetMethod.Name);
    }
}
