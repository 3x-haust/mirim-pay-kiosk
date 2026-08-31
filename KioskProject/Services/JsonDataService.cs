using System.IO;
using System.Text.Json;
using KioskProject.Models;

namespace KioskProject.Services;

public class JsonDataService : IDataService
{
    private const int RequiredPropertyCount = 6;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false
    };

    private readonly string _menuPath;
    private readonly OrderFileOperations _orderFiles;

    public JsonDataService()
        : this(Path.Combine(AppContext.BaseDirectory, "Data", "menu.json"))
    {
    }

    public JsonDataService(string menuPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(menuPath);
        _menuPath = Path.GetFullPath(menuPath);
        var directory = Path.GetDirectoryName(_menuPath)
            ?? throw new ArgumentException("The menu path must include a directory.", nameof(menuPath));
        _orderFiles = OrderFileOperations.Create(Path.Combine(directory, "orders.json"));
    }

    internal JsonDataService(string menuPath, OrderFileOperations orderFiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(menuPath);
        ArgumentNullException.ThrowIfNull(orderFiles);
        _menuPath = Path.GetFullPath(menuPath);
        _orderFiles = orderFiles;
    }

    public List<MenuItem> LoadMenus()
    {
        var json = File.ReadAllText(_menuPath);
        try
        {
            using var document = JsonDocument.Parse(json);
            ValidateDocument(document.RootElement);
            return JsonSerializer.Deserialize<List<MenuItem>>(json, SerializerOptions)
                ?? throw new InvalidDataException("Menu JSON must contain an array.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Menu JSON is malformed.", exception);
        }
    }

    public void SaveOrder(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        var orders = new List<Order>();
        if (File.Exists(_orderFiles.Path))
        {
            var existingBytes = File.ReadAllBytes(_orderFiles.Path);
            try
            {
                using var document = JsonDocument.Parse(existingBytes);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidDataException("Order JSON root must be an array.");
                }

                var existingOrders = JsonSerializer.Deserialize<List<Order?>>(existingBytes, SerializerOptions)
                    ?? throw new InvalidDataException("Order JSON root must be an array.");
                foreach (var existingOrder in existingOrders)
                {
                    orders.Add(existingOrder
                        ?? throw new InvalidDataException("Order JSON cannot contain null entries."));
                }
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Order JSON is malformed.", exception);
            }
        }

        orders.Add(order);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(orders, SerializerOptions);
        var directory = Path.GetDirectoryName(_orderFiles.Path)
            ?? throw new InvalidOperationException("The order path must include a directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_orderFiles.Path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            _orderFiles.Write(temporaryPath, bytes);
            _orderFiles.Commit(temporaryPath, _orderFiles.Path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void ValidateDocument(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Menu JSON root must be an array.");
        }

        var ids = new HashSet<int>();
        var index = 0;
        foreach (var item in root.EnumerateArray())
        {
            ValidateMenu(item, index, ids);
            index++;
        }
    }

    private static void ValidateMenu(JsonElement item, int index, HashSet<int> ids)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            throw InvalidMenu(index, "must be an object");
        }

        var properties = item.EnumerateObject().ToArray();
        if (properties.Length != RequiredPropertyCount)
        {
            throw InvalidMenu(index, "must contain exactly six properties");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            var expectedKind = property.Name switch
            {
                "id" => JsonValueKind.Number,
                "name" => JsonValueKind.String,
                "price" => JsonValueKind.Number,
                "category" => JsonValueKind.String,
                "imagePath" => JsonValueKind.String,
                "stock" => JsonValueKind.Number,
                _ => JsonValueKind.Undefined
            };

            if (expectedKind == JsonValueKind.Undefined ||
                property.Value.ValueKind != expectedKind ||
                !names.Add(property.Name))
            {
                throw InvalidMenu(index, $"property '{property.Name}' is not valid");
            }
        }

        if (!item.GetProperty("id").TryGetInt32(out var id) || id <= 0)
        {
            throw InvalidMenu(index, "id must be a positive 32-bit integer");
        }

        if (!ids.Add(id))
        {
            throw InvalidMenu(index, $"id {id} is duplicated");
        }

        RequireNonBlankString(item, "name", index);
        RequireNonNegativeInteger(item, "price", index);
        RequireNonBlankString(item, "category", index);
        RequireNonBlankString(item, "imagePath", index);
        var imagePath = item.GetProperty("imagePath").GetString()!;
        var imageSegments = imagePath.Split('/');
        var imagesDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Images"));
        var imageFilePath = Path.GetFullPath(Path.Combine(imagesDirectory, imagePath));
        if (Uri.TryCreate(imagePath, UriKind.Absolute, out _) ||
            Path.IsPathRooted(imagePath) ||
            imagePath.Contains('\\') ||
            imageSegments.Any(segment => segment is "" or "." or "..") ||
            !imagePath.StartsWith("Images/", StringComparison.Ordinal) ||
            !imageFilePath.StartsWith(imagesDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidMenu(index, "imagePath must be a canonical packaged asset path");
        }

        RequireNonNegativeInteger(item, "stock", index);
    }

    private static void RequireNonBlankString(JsonElement item, string propertyName, int index)
    {
        if (string.IsNullOrWhiteSpace(item.GetProperty(propertyName).GetString()))
        {
            throw InvalidMenu(index, $"{propertyName} must be nonblank");
        }
    }

    private static void RequireNonNegativeInteger(JsonElement item, string propertyName, int index)
    {
        if (!item.GetProperty(propertyName).TryGetInt32(out var value) || value < 0)
        {
            throw InvalidMenu(index, $"{propertyName} must be a nonnegative 32-bit integer");
        }
    }

    private static InvalidDataException InvalidMenu(int index, string reason) =>
        new($"Menu item at index {index} {reason}.");
}

internal sealed class OrderFileOperations
{
    internal OrderFileOperations(
        string path,
        Action<string, byte[]> write,
        Action<string, string> commit)
    {
        Path = System.IO.Path.GetFullPath(path);
        Write = write;
        Commit = commit;
    }

    internal string Path { get; }

    internal Action<string, byte[]> Write { get; }

    internal Action<string, string> Commit { get; }

    internal static OrderFileOperations Create(string path) => new(
        path,
        static (temporaryPath, bytes) =>
        {
            using var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        },
        static (temporaryPath, destinationPath) =>
            File.Move(temporaryPath, destinationPath, overwrite: true));
}
