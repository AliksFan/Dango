using System.Security.Cryptography;
using System.Text.Json;
using Dango.Desktop.Physics.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.Physics.Native;

[TestClass]
public sealed class Box2DNativeLoaderTests
{
    private static readonly Box2DNativeRequirements PinnedRequirements =
        new(13, new Box2DNativeVersion(3, 1, 1));

    [TestMethod]
    public void MissingLibraryReturnsControlledFailure()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-dango-box2d-{Guid.NewGuid():N}.dll");

        var loaded = Box2DNativeLoader.TryLoad(path, PinnedRequirements, out var session, out var failure);

        Assert.IsFalse(loaded);
        Assert.IsNull(session);
        Assert.AreEqual(Box2DNativeLoadFailureKind.MissingLibrary, failure.Kind);
    }

    [TestMethod]
    public void X86PeReturnsControlledArchitectureFailure()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dango-box2d-x86-{Guid.NewGuid():N}.dll");
        try
        {
            var image = new byte[128];
            image[0] = (byte)'M';
            image[1] = (byte)'Z';
            BitConverter.GetBytes(64).CopyTo(image, 0x3c);
            image[64] = (byte)'P';
            image[65] = (byte)'E';
            BitConverter.GetBytes((ushort)0x014c).CopyTo(image, 68);
            File.WriteAllBytes(path, image);

            var loaded = Box2DNativeLoader.TryLoad(path, PinnedRequirements, out var session, out var failure);

            Assert.IsFalse(loaded);
            Assert.IsNull(session);
            Assert.AreEqual(Box2DNativeLoadFailureKind.UnsupportedArchitecture, failure.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LibraryWithoutBridgeExportsReturnsControlledFailure()
    {
        var path = Path.Combine(Environment.SystemDirectory, "kernel32.dll");

        var loaded = Box2DNativeLoader.TryLoad(path, PinnedRequirements, out var session, out var failure);

        Assert.IsFalse(loaded);
        Assert.IsNull(session);
        Assert.AreEqual(Box2DNativeLoadFailureKind.MissingExport, failure.Kind);
    }

    [TestMethod]
    public void AbiAndVersionMismatchesReturnControlledFailures()
    {
        var path = PinnedLibraryPath();

        Assert.IsFalse(Box2DNativeLoader.TryLoad(
            path,
            PinnedRequirements with { AbiVersion = 999 },
            out var abiSession,
            out var abiFailure));
        Assert.IsNull(abiSession);
        Assert.AreEqual(Box2DNativeLoadFailureKind.AbiMismatch, abiFailure.Kind);

        Assert.IsFalse(Box2DNativeLoader.TryLoad(
            path,
            PinnedRequirements with { Box2DVersion = new Box2DNativeVersion(99, 0, 0) },
            out var versionSession,
            out var versionFailure));
        Assert.IsNull(versionSession);
        Assert.AreEqual(Box2DNativeLoadFailureKind.VersionMismatch, versionFailure.Kind);
    }

    [TestMethod]
    public void PinnedLibraryCreatesStepsAndDestroysWithoutLeakingBox2DBytes()
    {
        Assert.IsTrue(Box2DNativeLoader.TryLoad(
            PinnedLibraryPath(),
            PinnedRequirements,
            out var session,
            out var failure),
            failure.Message);
        var loadedSession = session!;
        using (loadedSession)
        {
            Assert.AreEqual(PinnedRequirements.Box2DVersion, loadedSession.Box2DVersion);
            var result = loadedSession.RunLifecycleProbe(8);
            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual(0, result.OutstandingBytes);
        }
    }

    [TestMethod]
    public void RepeatedLoadProbeAndUnloadIsStable()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            Assert.IsTrue(Box2DNativeLoader.TryLoad(
                PinnedLibraryPath(),
                PinnedRequirements,
                out var session,
                out var failure),
                $"iteration {iteration}: {failure.Message}");
            var loadedSession = session!;
            using (loadedSession)
            {
                var result = loadedSession.RunLifecycleProbe(2);
                Assert.IsTrue(result.Succeeded, $"iteration {iteration}: {result.Message}");
                Assert.AreEqual(0, result.OutstandingBytes);
            }
        }
    }

    [TestMethod]
    public void DependencyManifestPinsSourceLicenseAndBinaryHashes()
    {
        var manifestPath = Path.Combine(
            TestRepository.Root,
            "eng",
            "native",
            "box2d",
            "dependency.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;

        Assert.AreEqual("3.1.1", root.GetProperty("version").GetString());
        Assert.AreEqual("8c661469c9507d3ad6fbd2fea3f1aa71669c2fe3", root.GetProperty("commit").GetString());
        Assert.AreEqual("win-x64", root.GetProperty("native").GetProperty("rid").GetString());
        var exports = root.GetProperty("native").GetProperty("exports")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "dango_box2d_soft_body_create",
                "dango_box2d_soft_body_step",
                "dango_box2d_soft_body_destroy",
            },
            exports);
        AssertHashMatches(root.GetProperty("license"), Path.GetDirectoryName(manifestPath)!);
        AssertHashMatches(root.GetProperty("native"), TestRepository.Root);
        Assert.IsTrue(File.Exists(Path.Combine(AppContext.BaseDirectory, "licenses", "Box2D-LICENSE.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(AppContext.BaseDirectory, "licenses", "Box2D-dependency.json")));
    }

    private static void AssertHashMatches(JsonElement entry, string root)
    {
        var path = Path.Combine(root, entry.GetProperty("path").GetString()!);
        var expected = entry.GetProperty("sha256").GetString();
        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        Assert.AreEqual(expected, actual);
    }

    private static string PinnedLibraryPath() => Box2DNativeLoader.DefaultLibraryPath;
}
