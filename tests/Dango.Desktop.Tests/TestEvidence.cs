namespace Dango.Desktop.Tests;

internal static class TestEvidence
{
    private static readonly string DefaultRoot = Path.Combine(TestRepository.Root, "artifacts", "test-runs", Guid.NewGuid().ToString("N"));

    internal static FileStream Create(string category, string name)
    {
        var root = Environment.GetEnvironmentVariable("DANGO_TEST_EVIDENCE_ROOT") ?? DefaultRoot;
        var directory = Path.Combine(root, category);
        Directory.CreateDirectory(directory);
        return new FileStream(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.None);
    }
}
