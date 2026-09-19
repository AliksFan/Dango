using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dango.Desktop.Verification;

public interface IAtomicTextWriter
{
    void Write(string path, string content);
}

public sealed class AtomicTextFileWriter : IAtomicTextWriter
{
    public void Write(string path, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ??
            throw new InvalidOperationException("目标文件缺少目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(temporaryPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

public sealed class VerificationSessionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private readonly VerificationDefinition definition;
    private readonly IAtomicTextWriter writer;

    public VerificationSessionStore(
        VerificationDefinition definition,
        IAtomicTextWriter? writer = null)
    {
        this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
        this.writer = writer ?? new AtomicTextFileWriter();
    }

    public void Save(string path, VerificationSession session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        VerificationSessionValidator.Validate(definition, session);
        writer.Write(path, JsonSerializer.Serialize(session, SerializerOptions));
    }

    public VerificationSession Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var session = JsonSerializer.Deserialize<VerificationSession>(
            File.ReadAllText(path),
            SerializerOptions) ?? throw new InvalidDataException("验收记录为空。");
        VerificationSessionValidator.Validate(definition, session);
        return session;
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
