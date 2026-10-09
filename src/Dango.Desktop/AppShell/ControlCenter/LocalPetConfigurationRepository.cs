using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Dango.Client.Sync.Pets;
using Dango.Desktop.AppShell.Persistence;
using Dango.Desktop.Rendering.Visuals;
using Dango.Protocol;

namespace Dango.Desktop.AppShell.ControlCenter;

/// <summary>Explicit B2 association boundary. Caller supplies the isolated/local appearance path; no implicit network or UI.</summary>
public sealed class LocalPetConfigurationRepository(string path, AppearanceSettingsSession session, Dispatcher dispatcher,
    Action<DangoAppearanceConfiguration>? applied = null) : ILocalPetConfigurationRepository
{
    private readonly AppearanceSettingsStore parser = new();

    public Task<LocalPetConfigurationSnapshot> CaptureAsync(CancellationToken ct) =>
        dispatcher.InvokeAsync(() => Capture(ct), DispatcherPriority.Normal, ct).Task;

    public Task ApplyAsync(LocalPetConfigurationSnapshot expected, JsonElement configuration, CancellationToken ct) =>
        dispatcher.InvokeAsync(() =>
        {
            ct.ThrowIfCancellationRequested();
            var current = Capture(ct);
            if (current.Exists != expected.Exists || !current.OriginalBytes.Span.SequenceEqual(expected.OriginalBytes.Span)
                || !AppearanceContract.Equivalent(current.NormalizedConfiguration, expected.NormalizedConfiguration))
                throw new PetConfigurationChoiceException("local_configuration_conflict");
            configuration = AppearanceContract.ValidateAndClone(configuration);
            // The shared contract accepts hex case semantically; the existing desktop color domain uses uppercase.
            var canonical = JsonSerializer.SerializeToElement(new
            {
                appearanceSchemaVersion = 3,
                bodyColorHex = configuration.GetProperty("bodyColorHex").GetString()!.ToUpperInvariant(),
                eyeColorHex = configuration.GetProperty("eyeColorHex").GetString()!.ToUpperInvariant(),
                eyeSizePercent = configuration.GetProperty("eyeSizePercent").GetDouble(),
                eyeOvalityPercent = configuration.GetProperty("eyeOvalityPercent").GetDouble(),
                eyeHeightControlPercent = configuration.GetProperty("eyeHeightControlPercent").GetDouble(),
                eyeSpacingControlPercent = configuration.GetProperty("eyeSpacingControlPercent").GetDouble()
            });
            var content = canonical.GetRawText();
            var parsed = parser.Parse(System.Text.Encoding.UTF8.GetBytes(content));
            if (parsed.PreserveOriginal) throw new PetConfigurationChoiceException("local_configuration_invalid");
            var original = expected.Exists ? ExactFileSnapshot.FromBytes(expected.OriginalBytes.ToArray()) : ExactFileSnapshot.Missing;
            try
            {
                if (!new DurableAtomicTextWriter().TryReplace(path, original, content))
                    throw new PetConfigurationChoiceException("local_configuration_conflict");
            }
            catch (InvalidDataException) { throw new PetConfigurationChoiceException("local_configuration_conflict"); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { throw new PetConfigurationChoiceException("local_configuration_write_failed"); }
            // Both disk and memory change on the same UI turn; a live draft cannot be silently lost.
            session.AcceptExternalSave(parsed.Settings.Configuration);
            applied?.Invoke(parsed.Settings.Configuration);
        }, DispatcherPriority.Normal, ct).Task;

    private LocalPetConfigurationSnapshot Capture(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (session.IsDirty) throw new PetConfigurationChoiceException("local_unsaved_changes");
        if (session.IsWriteProtected) throw new PetConfigurationChoiceException("local_configuration_invalid");
        ExactFileSnapshot original;
        try { original = DurableAtomicTextWriter.Capture(path); }
        catch (InvalidDataException) { throw new PetConfigurationChoiceException("local_configuration_invalid"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new PetConfigurationChoiceException("local_configuration_unavailable"); }
        var parsed = original.Exists ? parser.Parse(original.Bytes) : new(AppearanceSettings.CreateDefault(), null);
        if (parsed.PreserveOriginal) throw new PetConfigurationChoiceException("local_configuration_invalid");
        if (parsed.Settings.Configuration != session.SavedAppearance)
            throw new PetConfigurationChoiceException("local_configuration_changed");
        var saved = parsed.Settings;
        var eyes = saved.EyeLayout;
        var normalized = JsonSerializer.SerializeToElement(new
        {
            appearanceSchemaVersion = 3,
            bodyColorHex = saved.BodyColor.ToHex(),
            eyeColorHex = saved.EyeColor.ToHex(),
            eyeSizePercent = eyes.SizePercent,
            eyeOvalityPercent = eyes.OvalPercent,
            eyeHeightControlPercent = eyes.HeightControlPercent,
            eyeSpacingControlPercent = eyes.SpacingControlPercent
        });
        return new(original.Exists, original.Bytes, AppearanceContract.ValidateAndClone(normalized));
    }
}
