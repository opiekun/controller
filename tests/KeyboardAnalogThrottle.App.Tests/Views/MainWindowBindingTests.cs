using System.Xml.Linq;

namespace KeyboardAnalogThrottle.App.Tests.Views;

public sealed class MainWindowBindingTests
{
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
