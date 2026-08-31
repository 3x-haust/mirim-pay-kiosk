using System.Collections;
using System.Reflection;
using System.Text.Json;

namespace KioskProject.Tests;

internal static class OrderTestFixture
{
    private static readonly Assembly ProductAssembly = ProductAssemblyFixture.Assembly;

    internal static object CreateMenu()
    {
        var menu = Create("KioskProject.Models.MenuItem");
        Write(menu, "Id", 1);
        Write(menu, "Name", "Coffee");
        Write(menu, "Price", 2500);
        Write(menu, "Category", "Coffee");
        Write(menu, "ImagePath", "Images/coffee.png");
        Write(menu, "Stock", 20);
        return menu;
    }

    internal static object CreateItem(object menu, int quantity)
    {
        var item = Create("KioskProject.Models.CartItem");
        Write(item, "Menu", menu);
        Write(item, "Quantity", quantity);
        return item;
    }

    internal static IList CreateItems(params object?[] items)
    {
        var list = Assert.IsAssignableFrom<IList>(Activator.CreateInstance(
            typeof(List<>).MakeGenericType(RequireType("KioskProject.Models.CartItem"))));
        foreach (var item in items)
        {
            list.Add(item);
        }

        return list;
    }

    internal static object CreateOrder(string id, DateTime orderedAt, IList items)
    {
        var order = Create("KioskProject.Models.Order");
        Write(order, "Id", id);
        Write(order, "OrderedAt", orderedAt);
        Write(order, "Items", items);
        Write(order, "TotalPrice", items.Cast<object>().Sum(item => Read<int>(item, "Subtotal")));
        Write(order, "PaymentMethod", "Pay");
        return order;
    }

    internal static object CreateJsonService(string directory)
    {
        var type = RequireType("KioskProject.Services.JsonDataService");
        var constructor = Assert.IsAssignableFrom<ConstructorInfo>(type.GetConstructor(new[] { typeof(string) }));
        return constructor.Invoke(new object[] { Path.Combine(directory, "menu.json") });
    }

    internal static object CreateInjectedJsonService(
        string directory,
        Action<string, byte[]> write,
        Action<string, string> commit)
    {
        var operationsType = RequireType("KioskProject.Services.OrderFileOperations");
        var operationsConstructor = Assert.Single(operationsType.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        var operations = operationsConstructor.Invoke(new object[]
        {
            Path.Combine(directory, "orders.json"),
            write,
            commit
        });
        var serviceType = RequireType("KioskProject.Services.JsonDataService");
        var serviceConstructor = Assert.Single(serviceType.GetConstructors(
            BindingFlags.Instance | BindingFlags.NonPublic).Where(candidate =>
                candidate.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual(new[] { typeof(string), operationsType })));
        return serviceConstructor.Invoke(new[] { Path.Combine(directory, "menu.json"), operations });
    }

    internal static object Invoke(object target, string methodName, params object?[] arguments) =>
        Assert.IsAssignableFrom<MethodInfo>(target.GetType().GetMethod(methodName)).Invoke(target, arguments)
        ?? throw new Xunit.Sdk.XunitException($"{methodName} returned null.");

    internal static void InvokeVoid(object target, string methodName, params object?[] arguments) =>
        Assert.IsAssignableFrom<MethodInfo>(target.GetType().GetMethod(methodName)).Invoke(target, arguments);

    internal static Exception InvokeException(object target, string methodName, params object?[] arguments)
    {
        var exception = Assert.Throws<TargetInvocationException>(() =>
            Assert.IsAssignableFrom<MethodInfo>(target.GetType().GetMethod(methodName)).Invoke(target, arguments));
        return Assert.IsAssignableFrom<Exception>(exception.InnerException);
    }

    internal static IReadOnlyList<object> ReadItems(object owner) =>
        Assert.IsAssignableFrom<IEnumerable>(Read<object>(owner, "Items")).Cast<object>().ToArray();

    internal static T Read<T>(object target, string propertyName) =>
        Assert.IsAssignableFrom<T>(Assert.IsAssignableFrom<PropertyInfo>(
            target.GetType().GetProperty(propertyName)).GetValue(target));

    internal static void Write(object target, string propertyName, object? value) =>
        Assert.IsAssignableFrom<PropertyInfo>(target.GetType().GetProperty(propertyName)).SetValue(target, value);

    internal static Type RequireType(string fullName) =>
        Assert.IsAssignableFrom<Type>(ProductAssembly.GetType(fullName));

    internal static JsonElement[] ReadOrderElements(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        return document.RootElement.EnumerateArray().Select(element => element.Clone()).ToArray();
    }

    private static object Create(string fullName) =>
        Assert.IsAssignableFrom<object>(Activator.CreateInstance(RequireType(fullName)));
}

internal sealed class OrderDirectory : IDisposable
{
    private OrderDirectory(string path)
    {
        Path = path;
        Directory.CreateDirectory(path);
    }

    internal string Path { get; }

    internal string OrdersPath => System.IO.Path.Combine(Path, "orders.json");

    internal static OrderDirectory Create() => new(System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "KioskProject.Tests",
        Guid.NewGuid().ToString("N")));

    internal string[] TemporaryFiles() => Directory.EnumerateFiles(Path)
        .Where(path => !string.Equals(path, OrdersPath, StringComparison.Ordinal))
        .ToArray();

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
