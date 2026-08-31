namespace KioskProject.Tests;

public sealed partial class SaveOrderTests
{
    [Fact]
    public void SaveOrder_preserves_old_file_and_removes_temp_when_injected_write_fails()
    {
        // Given
        using var directory = OrderDirectory.Create();
        var originalBytes = "[]"u8.ToArray();
        File.WriteAllBytes(directory.OrdersPath, originalBytes);
        string? observedTempPath = null;
        var service = OrderTestFixture.CreateInjectedJsonService(
            directory.Path,
            (tempPath, bytes) =>
            {
                observedTempPath = tempPath;
                File.WriteAllBytes(tempPath, bytes[..Math.Min(bytes.Length, 8)]);
                throw new IOException("Injected write failure.");
            },
            (_, _) => throw new Xunit.Sdk.XunitException("Commit must not run after a write failure."));

        // When
        var exception = OrderTestFixture.InvokeException(
            service,
            "SaveOrder",
            CreateOrder("write-failure", FirstTime, 1));

        // Then
        Assert.IsType<IOException>(exception);
        Assert.NotNull(observedTempPath);
        Assert.Equal(directory.Path, Path.GetDirectoryName(observedTempPath));
        Assert.Equal(originalBytes, File.ReadAllBytes(directory.OrdersPath));
        Assert.Empty(directory.TemporaryFiles());
    }

    [Fact]
    public void SaveOrder_preserves_old_file_and_removes_temp_when_injected_commit_fails()
    {
        // Given
        using var directory = OrderDirectory.Create();
        var originalBytes = "[]"u8.ToArray();
        File.WriteAllBytes(directory.OrdersPath, originalBytes);
        string? observedTempPath = null;
        var service = OrderTestFixture.CreateInjectedJsonService(
            directory.Path,
            (tempPath, bytes) =>
            {
                observedTempPath = tempPath;
                File.WriteAllBytes(tempPath, bytes);
            },
            (_, _) => throw new IOException("Injected commit failure."));

        // When
        var exception = OrderTestFixture.InvokeException(
            service,
            "SaveOrder",
            CreateOrder("commit-failure", FirstTime, 1));

        // Then
        Assert.IsType<IOException>(exception);
        Assert.NotNull(observedTempPath);
        Assert.Equal(directory.Path, Path.GetDirectoryName(observedTempPath));
        Assert.Equal(originalBytes, File.ReadAllBytes(directory.OrdersPath));
        Assert.Empty(directory.TemporaryFiles());
    }
}
