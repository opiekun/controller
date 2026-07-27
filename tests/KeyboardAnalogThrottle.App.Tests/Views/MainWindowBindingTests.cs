using System.Xml.Linq;

namespace KeyboardAnalogThrottle.App.Tests.Views;

public sealed class MainWindowBindingTests
{
    [Fact]
    public void Shortcuts_tab_binds_every_editor_field_and_save_command()
    {
        var document = XDocument.Load(FindMainWindowXaml());
        var shortcutsTab = document.Descendants().Single(element =>
            element.Name.LocalName == "TabItem" &&
            element.Attribute("Header")?.Value == "Shortcuts");

        foreach (var propertyName in new[]
        {
            "ThrottlePrimaryBinding",
            "BrakePrimaryBinding",
            "ThrottleCutBinding",
            "EmergencyDisableBinding",
            "RatchetIncreaseBinding",
            "RatchetDecreaseBinding",
            "RatchetResetBinding"
        })
        {
            Assert.Contains(shortcutsTab.Descendants(), element =>
                element.Name.LocalName == "TextBox" &&
                element.Attribute("Text")?.Value.Contains($"ShortcutEditor.{propertyName}", StringComparison.Ordinal) == true);
        }

        Assert.Contains(shortcutsTab.Descendants(), element =>
            element.Name.LocalName == "Button" &&
            element.Attribute("Command")?.Value.Contains("SaveShortcutsCommand", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Shortcuts_tab_uses_dynamic_fixed_level_collections_not_static_rows()
    {
        var document = XDocument.Load(FindMainWindowXaml());
        var shortcutsTab = document.Descendants().Single(element =>
            element.Name.LocalName == "TabItem" &&
            element.Attribute("Header")?.Value == "Shortcuts");
        var fixedLevelLists = shortcutsTab.Descendants()
            .Where(element => element.Name.LocalName == "ItemsControl")
            .ToArray();

        Assert.Equal(2, fixedLevelLists.Length);
        Assert.Contains(fixedLevelLists, element =>
            element.Attribute("ItemsSource")?.Value.Contains("ShortcutEditor.ThrottleFixedLevels", StringComparison.Ordinal) == true);
        Assert.Contains(fixedLevelLists, element =>
            element.Attribute("ItemsSource")?.Value.Contains("ShortcutEditor.BrakeFixedLevels", StringComparison.Ordinal) == true);
        Assert.All(fixedLevelLists, element =>
        {
            Assert.Contains(element.Descendants(), child =>
                child.Name.LocalName == "TextBox" &&
                child.Attribute("Text")?.Value.Contains("Binding", StringComparison.Ordinal) == true);
            Assert.Contains(element.Descendants(), child =>
                child.Name.LocalName == "TextBlock" &&
                child.Attribute("Text")?.Value.Contains("Level", StringComparison.Ordinal) == true);
        });
        Assert.DoesNotContain("ThrottleFixed25Binding", shortcutsTab.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("BrakeFixed25Binding", shortcutsTab.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("RawThrottlePercentage")]
    [InlineData("ThrottlePercentage")]
    [InlineData("RawBrakePercentage")]
    [InlineData("BrakePercentage")]
    public void Read_only_trigger_display_bindings_are_one_way(string propertyName)
    {
        var document = XDocument.Load(FindMainWindowXaml());
        var progressBar = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ProgressBar" &&
                element.Attribute("Value")?.Value.StartsWith($"{{Binding {propertyName}", StringComparison.Ordinal) == true);

        Assert.Contains("Mode=OneWay", progressBar.Attribute("Value")!.Value, StringComparison.Ordinal);
    }

    private static string FindMainWindowXaml()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "KeyboardAnalogThrottle.App", "MainWindow.xaml");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Could not locate MainWindow.xaml from the test output directory.");
    }
}
