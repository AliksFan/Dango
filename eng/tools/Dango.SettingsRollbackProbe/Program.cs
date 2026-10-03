using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.DesktopHost;

// Run the actual previous-version reader and writer without launching that app or its desktop host.
if (args.Length != 2) throw new ArgumentException("Usage: Dango.SettingsRollbackProbe <old-package-directory> <new-evidence-directory>");
var oldPackage = Path.GetFullPath(args[0]);
var evidenceRoot = Path.GetFullPath(args[1]);
if (Directory.Exists(evidenceRoot) || File.Exists(evidenceRoot)) throw new IOException("Evidence directory must be new.");
var assemblyPath = Path.Combine(oldPackage, "Dango.Desktop.dll");
var context = new PreviousVersionContext(oldPackage);
var assembly = context.LoadFromAssemblyPath(assemblyPath);
var storeType = assembly.GetType("Dango.Desktop.AppShell.ControlCenter.ControlCenterWindowSettingsStore", throwOnError: true)!;
var oldStore = Activator.CreateInstance(storeType, [null])!;
var load = storeType.GetMethod("LoadOrDefault")!;
var save = storeType.GetMethod("Save")!;
Directory.CreateDirectory(evidenceRoot);
var results = new List<object>();
foreach (var preset in Enum.GetValues<ControlCenterWindowPreset>())
{
    var path = Path.Combine(evidenceRoot, preset.ToString(), "settings.json");
    var store = new ControlCenterWindowSettingsStore();
    store.Save(path, new(1010, 690, selectedPreset: preset, displayMode: DesktopDisplayMode.Window,
        interactionEnabled: false, hatPresetId: "bowler", keyboardControl: new(true, KeyboardControlScheme.Wasd)));
    var presetBytes = File.ReadAllBytes(ControlCenterWindowSettingsStore.GetPresetPath(path));
    var result = load.Invoke(oldStore, [path])!;
    if ((bool)result.GetType().GetProperty("PreserveOriginal")!.GetValue(result)! ||
        result.GetType().GetProperty("Warning")!.GetValue(result) is not null)
        throw new InvalidDataException("The actual previous reader rejected the new app's saved settings.");
    var oldSettings = result.GetType().GetProperty("Settings")!.GetValue(result)!;
    var oldSettingsType = oldSettings.GetType();
    object ReadProperty(string name) => oldSettingsType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)!
        .GetValue(oldSettings)!;
    if ((int)ReadProperty("SchemaVersion") != 4 || (double)ReadProperty("Width") != 1010 ||
        (double)ReadProperty("Height") != 690 || (string)ReadProperty("HatPresetId") != "bowler" ||
        (bool)ReadProperty("InteractionEnabled"))
        throw new InvalidDataException("The actual previous reader discarded valid preferences.");
    var constructor = oldSettingsType.GetConstructors().Single();
    var constructorArguments = constructor.GetParameters().Select(parameter =>
        parameter.Name == "hatPresetId" ? "top-hat" : ReadProperty(parameter.Name!)).ToArray();
    var changedByOldVersion = constructor.Invoke(constructorArguments);
    save.Invoke(oldStore, [path, changedByOldVersion]);
    var returnedToNewVersion = store.LoadOrDefault(path);
    if (returnedToNewVersion.PreserveOriginal || returnedToNewVersion.Settings.HatPresetId != "top-hat" ||
        returnedToNewVersion.Settings.SelectedPreset != preset ||
        !presetBytes.SequenceEqual(File.ReadAllBytes(ControlCenterWindowSettingsStore.GetPresetPath(path))))
        throw new InvalidDataException("Returning from the actual old writer lost the new preset or old preferences.");
    results.Add(new { preset = preset.ToString(), actualOldReaderAccepted = true, actualOldWriterSaved = true,
        newReaderRecoveredPreset = true });
}
var summary = new { passed = true, actualOldAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath))), results };
File.WriteAllText(Path.Combine(evidenceRoot, "verification.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PASS actual previous-version reader/writer: {results.Count}/3 presets; fictional settings only.");
context.Unload();

sealed class PreviousVersionContext(string package) : AssemblyLoadContext(isCollectible: true)
{
    protected override Assembly? Load(AssemblyName name)
    {
        if (name.Name?.StartsWith("Dango.", StringComparison.Ordinal) != true) return null;
        var path = Path.Combine(package, name.Name + ".dll");
        return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
    }
}
