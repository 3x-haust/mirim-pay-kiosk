using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace KioskProject.Tests;

public sealed class KioskVisualResourceTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string ProjectRoot = Path.Combine(ProductAssemblyFixture.RepositoryRoot, "KioskProject");
    private static readonly string ResourcesRoot = Path.Combine(ProjectRoot, "Resources");
    private static readonly string[] ResourceFiles =
    {
        "KioskFoundations.xaml",
        "KioskControls.xaml",
        "KioskPortrait.xaml",
        "KioskAssets.xaml"
    };

    [Fact]
    public void Application_merges_the_complete_resource_layer_in_dependency_order()
    {
        var app = Load(Path.Combine(ProjectRoot, "App.xaml"));

        var sources = app.Descendants(Presentation + "ResourceDictionary")
            .Select(element => (string?)element.Attribute("Source"))
            .Where(source => source is not null)
            .ToArray();

        Assert.Equal(ResourceFiles.Select(file => $"Resources/{file}"), sources);
    }

    [Fact]
    public void Figma_palette_and_portrait_metrics_keep_the_extracted_machine_values()
    {
        var foundations = Load(Path.Combine(ResourcesRoot, "KioskFoundations.xaml"));
        var keyedValues = foundations.Root!.Elements()
            .Where(element => element.Attribute(Xaml + "Key") is not null)
            .ToDictionary(element => (string)element.Attribute(Xaml + "Key")!, element => element.Value.Trim());

        Assert.Equal("#FF249D57", keyedValues["KioskPrimaryColor"]);
        Assert.Equal("#FF208D4E", keyedValues["KioskPrimaryStrongColor"]);
        Assert.Equal("#FF0F9444", keyedValues["KioskSuccessColor"]);
        Assert.Equal("#FFFFFFFF", keyedValues["KioskWhiteColor"]);
        Assert.Equal("#FF808080", keyedValues["KioskTextSecondaryColor"]);
        Assert.Equal("#FFD7D7D7", keyedValues["KioskBorderColor"]);
        Assert.Equal("1080", keyedValues["KioskLogicalWidth"]);
        Assert.Equal("1920", keyedValues["KioskLogicalHeight"]);
        Assert.Equal("96", keyedValues["KioskTouchTargetMin"]);
    }

    [Fact]
    public void Secondary_action_uses_the_Figma_118_DIP_height_instead_of_inheriting_108()
    {
        var foundations = Load(Path.Combine(ResourcesRoot, "KioskFoundations.xaml"));
        var controls = Load(Path.Combine(ResourcesRoot, "KioskControls.xaml"));
        var keyedValues = foundations.Root!.Elements()
            .Where(element => element.Attribute(Xaml + "Key") is not null)
            .ToDictionary(element => (string)element.Attribute(Xaml + "Key")!, element => element.Value.Trim());

        Assert.Equal("KioskSecondaryActionHeight", EffectiveSetterResource(controls, "KioskSecondaryButtonStyle", "MinHeight"));
        Assert.Equal("118", keyedValues["KioskSecondaryActionHeight"]);
    }

    [Fact]
    public void Start_CTA_uses_the_Figma_45_DIP_type_instead_of_inheriting_40()
    {
        var foundations = Load(Path.Combine(ResourcesRoot, "KioskFoundations.xaml"));
        var controls = Load(Path.Combine(ResourcesRoot, "KioskControls.xaml"));
        var keyedValues = foundations.Root!.Elements()
            .Where(element => element.Attribute(Xaml + "Key") is not null)
            .ToDictionary(element => (string)element.Attribute(Xaml + "Key")!, element => element.Value.Trim());

        Assert.Equal("KioskStartActionType", EffectiveSetterResource(controls, "KioskOutlineButtonStyle", "FontSize"));
        Assert.Equal("45", keyedValues["KioskStartActionType"]);
    }

    [Fact]
    public void Persisted_Figma_metadata_is_well_formed_xml_without_control_bytes()
    {
        var metadataPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Figma", "metadata-1-3.xml");
        var bytes = File.ReadAllBytes(metadataPath);

        Assert.DoesNotContain((byte)0x08, bytes);
        Assert.Equal("canvas", XDocument.Load(metadataPath).Root?.Name.LocalName);
    }

    [Fact]
    public void Every_custom_static_resource_reference_resolves_to_a_declared_key()
    {
        var documents = ResourceFiles.Select(file => Load(Path.Combine(ResourcesRoot, file))).ToArray();
        var declaredKeys = documents.SelectMany(document => document.Descendants())
            .Select(element => (string?)element.Attribute(Xaml + "Key"))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.Ordinal);
        var references = documents.SelectMany(document => document.Descendants().Attributes())
            .SelectMany(attribute => Regex.Matches(attribute.Value, @"\{StaticResource\s+([^}\s]+)\}"))
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var unresolved = references.Where(reference => !declaredKeys.Contains(reference)).Order().ToArray();

        Assert.True(unresolved.Length == 0, $"Unresolved StaticResource keys: {string.Join(", ", unresolved)}");
    }

    [Fact]
    public void Grid_dimensions_do_not_consume_double_static_resources()
    {
        // Given
        var paths = Directory.GetFiles(ProjectRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();
        var documents = paths.Select(path => (Path: path, Document: Load(path))).ToArray();
        var doubleKeys = documents.SelectMany(source => source.Document.Descendants())
            .Where(element => element.Name.LocalName == "Double" &&
                              element.Name.NamespaceName == "clr-namespace:System;assembly=System.Runtime")
            .Select(element => (string?)element.Attribute(Xaml + "Key"))
            .Where(key => key is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        // When
        var violations = documents.SelectMany(source => source.Document.Descendants()
                .Where(element => element.Name == Presentation + "RowDefinition" ||
                                  element.Name == Presentation + "ColumnDefinition")
                .Select(element => new
                {
                    source.Path,
                    Element = element.Name.LocalName,
                    Dimension = element.Name == Presentation + "RowDefinition" ? "Height" : "Width",
                    Value = (string?)element.Attribute(
                        element.Name == Presentation + "RowDefinition" ? "Height" : "Width")
                }))
            .Where(candidate => candidate.Value is not null)
            .Select(candidate => new
            {
                candidate.Path,
                candidate.Element,
                candidate.Dimension,
                Match = Regex.Match(candidate.Value!, @"^\{StaticResource\s+([^}\s,]+)\}$")
            })
            .Where(candidate => candidate.Match.Success && doubleKeys.Contains(candidate.Match.Groups[1].Value))
            .Select(candidate =>
                $"{Path.GetRelativePath(ProjectRoot, candidate.Path)}:{candidate.Element}.{candidate.Dimension} -> {candidate.Match.Groups[1].Value}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Then
        Assert.True(
            violations.Length == 0,
            $"Grid dimensions cannot consume sys:Double StaticResources: {string.Join(", ", violations)}");
    }

    [Fact]
    public void CornerRadius_consumers_only_use_compatible_static_resources()
    {
        // Given
        var paths = Directory.GetFiles(ProjectRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();
        var resourceTypes = paths.SelectMany(path => Load(path).Descendants())
            .Where(element => element.Attribute(Xaml + "Key") is not null)
            .ToDictionary(element => (string)element.Attribute(Xaml + "Key")!, element => element.Name.LocalName, StringComparer.Ordinal);

        // When
        var violations = paths.SelectMany(path => Load(path).Descendants()
                .Where(element => element.Attribute("CornerRadius") is not null)
                .Select(element => new { path, Value = (string)element.Attribute("CornerRadius")! }))
            .Select(candidate => new { candidate.path, Match = Regex.Match(candidate.Value, @"^\{StaticResource\s+([^}\s,]+)\}$") })
            .Where(candidate => candidate.Match.Success && resourceTypes.TryGetValue(candidate.Match.Groups[1].Value, out var type) && type != "CornerRadius")
            .Select(candidate => $"{Path.GetRelativePath(ProjectRoot, candidate.path)}:CornerRadius -> {candidate.Match.Groups[1].Value} ({resourceTypes[candidate.Match.Groups[1].Value]})")
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Then
        Assert.True(violations.Length == 0, $"CornerRadius StaticResources must declare CornerRadius objects: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Image_sources_only_consume_image_source_static_resources()
    {
        // Given
        var paths = Directory.GetFiles(ProjectRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();
        var documents = paths.Select(path => (Path: path, Document: Load(path))).ToArray();
        var resourceTypes = documents.SelectMany(source => source.Document.Descendants())
            .Where(element => element.Attribute(Xaml + "Key") is not null)
            .ToDictionary(
                element => (string)element.Attribute(Xaml + "Key")!,
                element => element.Name.LocalName,
                StringComparer.Ordinal);
        var imageSourceTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "BitmapImage", "BitmapFrame", "CachedBitmap", "ColorConvertedBitmap", "CroppedBitmap",
            "DrawingImage", "FormatConvertedBitmap", "RenderTargetBitmap", "TransformedBitmap", "WriteableBitmap"
        };

        // When
        var violations = documents.SelectMany(source => source.Document.Descendants(Presentation + "Image")
                .SelectMany(image => Regex.Matches((string?)image.Attribute("Source") ?? string.Empty, @"\{StaticResource\s+([^}\s,]+)")
                    .Select(match => new { source.Path, Key = match.Groups[1].Value })))
            .Where(candidate => resourceTypes.TryGetValue(candidate.Key, out var type) && !imageSourceTypes.Contains(type))
            .Select(candidate =>
                $"{Path.GetRelativePath(ProjectRoot, candidate.Path)}:Image.Source -> {candidate.Key} ({resourceTypes[candidate.Key]})")
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Then
        Assert.True(
            violations.Length == 0,
            $"Image.Source StaticResources must declare ImageSource objects: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Controls_define_touch_focus_pressed_hover_and_disabled_states()
    {
        var controls = Load(Path.Combine(ResourcesRoot, "KioskControls.xaml"));
        var primaryStyle = controls.Descendants(Presentation + "Style")
            .Single(style => (string?)style.Attribute(Xaml + "Key") == "KioskPrimaryButtonStyle");
        var triggerProperties = primaryStyle.Descendants(Presentation + "Trigger")
            .Select(trigger => (string?)trigger.Attribute("Property"))
            .ToHashSet(StringComparer.Ordinal);
        var setterProperties = primaryStyle.Elements(Presentation + "Setter")
            .Select(setter => (string?)setter.Attribute("Property"))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("MinWidth", setterProperties);
        Assert.Contains("MinHeight", setterProperties);
        Assert.Contains("FocusVisualStyle", setterProperties);
        Assert.Contains("IsMouseOver", triggerProperties);
        Assert.Contains("IsPressed", triggerProperties);
        Assert.Contains("IsEnabled", triggerProperties);
    }

    [Fact]
    public void Portrait_host_uses_uniform_scaling_over_the_fixed_logical_canvas()
    {
        var portrait = Load(Path.Combine(ResourcesRoot, "KioskPortrait.xaml"));
        var viewboxStyle = portrait.Descendants(Presentation + "Style")
            .Single(style => (string?)style.Attribute(Xaml + "Key") == "KioskPortraitViewboxStyle");
        var setters = viewboxStyle.Elements(Presentation + "Setter")
            .ToDictionary(setter => (string)setter.Attribute("Property")!, setter => (string)setter.Attribute("Value")!);
        var logicalGrid = portrait.Descendants(Presentation + "Grid").Single();

        Assert.Equal("Uniform", setters["Stretch"]);
        Assert.Equal("Both", setters["StretchDirection"]);
        Assert.Equal("{StaticResource KioskLogicalWidth}", (string?)logicalGrid.Attribute("Width"));
        Assert.Equal("{StaticResource KioskLogicalHeight}", (string?)logicalGrid.Attribute("Height"));
    }

    [Fact]
    public void Local_logo_and_placeholder_assets_are_valid_png_files_and_are_deployed()
    {
        var expected = new[] { "mirim-logo.png", "menu-placeholder.png", "americano.png" };
        var outputImages = Path.Combine(ProjectRoot, "bin", "Release", "net8.0-windows", "Images");

        foreach (var fileName in expected)
        {
            AssertPng(Path.Combine(ProjectRoot, "Images", fileName));
            AssertPng(Path.Combine(outputImages, fileName));
        }
    }

    private static XDocument Load(string path) => XDocument.Load(path, LoadOptions.SetLineInfo);

    private static string EffectiveSetterResource(XDocument controls, string styleKey, string property)
    {
        var styles = controls.Descendants(Presentation + "Style")
            .Where(style => style.Attribute(Xaml + "Key") is not null)
            .ToDictionary(style => (string)style.Attribute(Xaml + "Key")!);
        for (var style = styles[styleKey]; style is not null;)
        {
            var value = style.Elements(Presentation + "Setter")
                .SingleOrDefault(setter => (string?)setter.Attribute("Property") == property)?
                .Attribute("Value")?.Value;
            if (value is not null)
            {
                return Regex.Match(value, @"^\{StaticResource\s+([^}\s]+)\}$").Groups[1].Value;
            }

            var basedOn = style.Attribute("BasedOn")?.Value;
            var baseKey = basedOn is null ? string.Empty : Regex.Match(basedOn, @"^\{StaticResource\s+([^}\s]+)\}$").Groups[1].Value;
            style = styles.GetValueOrDefault(baseKey);
        }

        return string.Empty;
    }

    private static void AssertPng(string path)
    {
        Assert.True(File.Exists(path), $"Missing local image asset: {path}");
        var signature = File.ReadAllBytes(path).Take(8).ToArray();
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, signature);
    }
}
