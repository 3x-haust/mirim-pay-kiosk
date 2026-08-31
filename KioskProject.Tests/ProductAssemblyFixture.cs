using System.Reflection;

namespace KioskProject.Tests;

internal static class ProductAssemblyFixture
{
    internal static string RepositoryRoot { get; } = FindRepositoryRoot();

    internal static Assembly Assembly { get; } = LoadProductAssembly();

    private static Assembly LoadProductAssembly()
    {
        var configuredAssembly = Environment.GetEnvironmentVariable("KIOSK_PRODUCT_ASSEMBLY");
        var assemblyPath = string.IsNullOrWhiteSpace(configuredAssembly)
            ? Path.Combine(RepositoryRoot, "KioskProject", "bin", "Release", "net8.0-windows", "KioskProject.dll")
            : Path.GetFullPath(configuredAssembly);
        return Assembly.LoadFrom(assemblyPath);
    }

    private static string FindRepositoryRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("KIOSK_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            return Path.GetFullPath(configuredRoot);
        }

        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KioskProject.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate KioskProject.sln.");
    }
}
