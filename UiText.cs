using System.Globalization;
using System.IO;
using System.Text.Json;
using Avalonia;

namespace ProjectLibraryDesk;

internal static class UiText
{
    private static readonly string PreferencePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ProjectLibraryDesk", "language.txt");

    private static readonly IReadOnlyDictionary<string, string> English = ReadCatalog("en");
    private static readonly IReadOnlyDictionary<string, string> Polish = ReadCatalog("pl");
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo PolishCulture = CultureInfo.GetCultureInfo("pl");
    internal static string Language { get; } = ReadPreference();
    private static IReadOnlyDictionary<string, string> Catalog => Language == "pl" ? Polish : English;

    private static Dictionary<string, string> ReadCatalog(string language)
    {
        using Stream stream = typeof(UiText).Assembly.GetManifestResourceStream($"UiStrings.{language}.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }

    private static string ReadPreference()
    {
        try { return File.ReadAllText(PreferencePath).Trim() == "pl" ? "pl" : "en"; }
        catch (IOException) { return "en"; }
        catch (UnauthorizedAccessException) { return "en"; }
    }

    internal static void SavePreference(string language)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)!);
        File.WriteAllText(PreferencePath, language == "pl" ? "pl" : "en");
    }

    internal static void AddResources(Application app)
    {
        foreach (var (key, value) in Catalog)
            app.Resources[$"Ui.{key}"] = value;
    }

    internal static string T(string key, params object?[] args)
    {
        string value = Catalog[key];
        return args.Length == 0 ? value : string.Format(Language == "pl" ? PolishCulture : EnglishCulture, value, args);
    }

    internal static string SqlError(string message)
    {
        foreach (var (key, english) in English)
            if (key.StartsWith("Sql", StringComparison.Ordinal) &&
                (message == english || message == Polish[key]))
                return Catalog[key];
        return message;
    }
}
