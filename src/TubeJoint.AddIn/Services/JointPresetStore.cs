using System.Text.Json;
using System.Text.Json.Serialization;
using TubeJoint.AddIn.Models;

namespace TubeJoint.AddIn.Services;

/// <summary>Persists user presets outside IPT/IAM files.</summary>
internal sealed class JointPresetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string CatalogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Autodesk", "Inventor 2027", "TubeJoint", "Presets", "joint-presets.json");

    public JointPresetCatalog Load()
    {
        try
        {
            if (!File.Exists(CatalogPath))
                return new JointPresetCatalog();
            var json = File.ReadAllText(CatalogPath);
            var catalog = JsonSerializer.Deserialize<JointPresetCatalog>(json, JsonOptions)
                          ?? new JointPresetCatalog();
            catalog.Presets ??= new List<JointPreset>();
            return catalog;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Не удалось прочитать пресеты '{CatalogPath}'. " +
                "Файл оставлен без изменений; исправьте или переименуйте его.", exception);
        }
    }

    public void Save(JointPresetCatalog catalog)
    {
        var directory = Path.GetDirectoryName(CatalogPath)
                        ?? throw new InvalidOperationException("Не удалось определить папку пресетов.");
        Directory.CreateDirectory(directory);
        catalog.Schema = JointPresetCatalog.CurrentSchema;
        var temporaryPath = CatalogPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(catalog, JsonOptions));
        File.Move(temporaryPath, CatalogPath, true);
    }

    public static IReadOnlyList<JointPreset> Sort(JointPresetCatalog catalog) =>
        catalog.SortOrder switch
        {
            JointPresetSortOrder.Created => catalog.Presets
                .OrderByDescending(item => item.CreatedUtc).ToList(),
            JointPresetSortOrder.Alphabetical => catalog.Presets
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
            JointPresetSortOrder.Modified => catalog.Presets
                .OrderByDescending(item => item.ModifiedUtc).ToList(),
            _ => catalog.Presets.OrderByDescending(item => item.LastUsedUtc).ToList()
        };

    public static JointPreset? StartupPreset(JointPresetCatalog catalog)
    {
        var id = catalog.StartupMode switch
        {
            JointPresetStartupMode.LastUsed => catalog.LastUsedPresetId,
            JointPresetStartupMode.SpecificPreset => catalog.DefaultPresetId,
            _ => null
        };
        return string.IsNullOrWhiteSpace(id)
            ? null
            : catalog.Presets.FirstOrDefault(item => item.Id == id);
    }
}
