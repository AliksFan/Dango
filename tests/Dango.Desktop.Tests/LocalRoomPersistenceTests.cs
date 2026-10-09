using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dango.Desktop.AppShell.Persistence;

namespace Dango.Desktop.Tests;

[TestClass]
public sealed class LocalRoomPersistenceTests
{
    [TestMethod]
    public void MissingSaveHasTwoIndependentIdentitiesAndSavesOnlyOnRequest()
    {
        var path = NewPath();
        var session = new LocalRoomSaveSession(path);

        Assert.IsFalse(File.Exists(path));
        Assert.IsTrue(session.HasPendingSave);
        Assert.IsFalse(session.IsWriteProtected);
        Assert.AreNotEqual(Guid.Empty, session.OrdinaryRoom.RoomId);
        Assert.AreNotEqual(Guid.Empty, session.DesktopPetRoom.RoomId);
        Assert.AreNotEqual(session.OrdinaryRoom.RoomId, session.DesktopPetRoom.RoomId);
        Assert.AreEqual("ordinary", session.OrdinaryRoom.RoomType);
        Assert.AreEqual("desktopPet", session.DesktopPetRoom.RoomType);
        Assert.AreEqual(new LocalRoomCameraSaveData("follow", "standard", 100, 0, 0), session.OrdinaryRoom.Camera);

        Assert.IsTrue(session.TrySave());
        Assert.IsFalse(session.HasPendingSave);
        var reloaded = new LocalRoomSaveSession(path);
        Assert.AreEqual(session.OrdinaryRoom, reloaded.OrdinaryRoom);
        Assert.AreEqual(session.DesktopPetRoom, reloaded.DesktopPetRoom);
        Assert.IsFalse(reloaded.HasPendingSave);
    }

    [TestMethod]
    public void ColdReloadPreservesBothRoomStatesAndOnlyTheDeclaredFields()
    {
        var path = NewPath();
        var session = new LocalRoomSaveSession(path);
        var ordinary = session.OrdinaryRoom with
        {
            ResumeCenterX = -765.25,
            Camera = new("manual", "custom", 42.5, -100.125, 201.75),
        };
        var desktopPet = session.DesktopPetRoom with
        {
            ResumeCenterX = 1234.5,
            Camera = new("edgeFollow", "whole", 130, 0, 0),
        };
        session.UpdateRoom(ordinary);
        session.UpdateRoom(desktopPet);
        Assert.IsTrue(session.TrySave());

        var reloaded = new LocalRoomSaveSession(path);
        Assert.AreEqual(ordinary, reloaded.OrdinaryRoom);
        Assert.AreEqual(desktopPet, reloaded.DesktopPetRoom);
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        CollectionAssert.AreEquivalent(new[] { "schemaVersion", "ordinaryRoom", "desktopPetRoom" }, root.Select(x => x.Key).ToArray());
        CollectionAssert.AreEquivalent(new[] { "roomId", "roomType", "templateId", "templateVersion", "resumeCenterX", "camera" }, root["ordinaryRoom"]!.AsObject().Select(x => x.Key).ToArray());
        CollectionAssert.AreEquivalent(new[] { "mode", "preset", "requestedZoomPercent", "manualX", "manualY" }, root["ordinaryRoom"]!["camera"]!.AsObject().Select(x => x.Key).ToArray());
    }

    [TestMethod]
    [DynamicData(nameof(InvalidSaves))]
    public void CorruptUnknownOrInvalidSavesProtectExactOriginalBytes(string content)
    {
        var path = NewPath();
        Write(path, content);
        var original = File.ReadAllBytes(path);

        var session = new LocalRoomSaveSession(path);
        Assert.IsTrue(session.IsWriteProtected);
        StringAssert.Contains(session.StatusMessage, "保护");
        session.UpdateRoom(session.OrdinaryRoom with { ResumeCenterX = 123 });
        Assert.IsTrue(session.HasPendingSave);
        Assert.IsFalse(session.TrySave());
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
    }

    public static IEnumerable<object[]> InvalidSaves()
    {
        var state = LocalRoomSaveState.CreateDefault();
        var valid = JsonSerializer.Serialize(state, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        foreach (var content in new[] { "", "[]", "{", valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":2"), valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"), valid.Replace("\"mode\":\"follow\"", "\"mode\":\"follow\",\"mode\":\"manual\""), new string(' ', 65537) })
            yield return [content];
        foreach (var mutation in new Action<JsonObject>[]
        {
            json => json["extra"] = true,
            json => json.Remove("desktopPetRoom"),
            json => json["ordinaryRoom"] = null,
            json => json["desktopPetRoom"]!["roomId"] = json["ordinaryRoom"]!["roomId"]!.GetValue<string>(),
            json => json["ordinaryRoom"]!["roomId"] = Guid.Empty.ToString(),
            json => json["ordinaryRoom"]!["roomType"] = "desktopPet",
            json => json["ordinaryRoom"]!["templateId"] = "future-template",
            json => json["ordinaryRoom"]!["templateVersion"] = 2,
            json => json["ordinaryRoom"]!["resumeCenterX"] = 1000001,
            json => json["ordinaryRoom"]!["resumeCenterX"] = "0",
            json => json["ordinaryRoom"]!["velocity"] = 5,
            json => json["ordinaryRoom"]!["camera"]!["mode"] = "unknown",
            json => json["ordinaryRoom"]!["camera"]!["preset"] = "unknown",
            json => json["ordinaryRoom"]!["camera"]!["requestedZoomPercent"] = 0,
            json => json["ordinaryRoom"]!["camera"]!["requestedZoomPercent"] = 130.001,
            json => json["ordinaryRoom"]!["camera"]!["manualX"] = -1000001,
            json => json["ordinaryRoom"]!["camera"]!.AsObject().Remove("manualY"),
            json => json["ordinaryRoom"]!["camera"]!["extra"] = true,
        })
        {
            var json = JsonNode.Parse(valid)!.AsObject();
            mutation(json);
            yield return [json.ToJsonString()];
        }
    }

    [TestMethod]
    public void LockedWriteKeepsOldBytesAndPendingStateUntilRetrySucceeds()
    {
        var path = NewPath();
        var session = new LocalRoomSaveSession(path);
        Assert.IsTrue(session.TrySave());
        var original = File.ReadAllBytes(path);
        var updated = session.OrdinaryRoom with { ResumeCenterX = 321.125 };
        session.UpdateRoom(updated);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.IsFalse(session.TrySave());
            Assert.IsTrue(session.HasPendingSave);
            Assert.IsFalse(session.IsWriteProtected);
            Assert.AreEqual(updated, session.OrdinaryRoom);
            StringAssert.Contains(session.StatusMessage, "重试");
        }
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        Assert.IsTrue(session.TrySave());
        Assert.IsFalse(session.HasPendingSave);
        Assert.AreEqual(updated, new LocalRoomSaveSession(path).OrdinaryRoom);
    }

    [TestMethod]
    public void ChangeAfterLoadIsProtectedWithoutSilentlyOverwritingAnotherSave()
    {
        var path = NewPath();
        var initial = new LocalRoomSaveSession(path);
        Assert.IsTrue(initial.TrySave());
        var stale = new LocalRoomSaveSession(path);
        var newer = new LocalRoomSaveSession(path);
        newer.UpdateRoom(newer.DesktopPetRoom with { ResumeCenterX = 900 });
        Assert.IsTrue(newer.TrySave());
        var original = File.ReadAllBytes(path);

        stale.UpdateRoom(stale.OrdinaryRoom with { ResumeCenterX = 800 });
        Assert.IsFalse(stale.TrySave());
        Assert.IsTrue(stale.IsWriteProtected);
        Assert.IsTrue(stale.HasPendingSave);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
    }

    [TestMethod]
    public void FileCreatedSinceMissingLoadIsAlsoProtected()
    {
        var path = NewPath();
        var session = new LocalRoomSaveSession(path);
        Write(path, "external fictional bytes");
        Assert.IsFalse(session.TrySave());
        Assert.IsTrue(session.IsWriteProtected);
        Assert.AreEqual("external fictional bytes", File.ReadAllText(path));
    }

    [TestMethod]
    public void OversizedReplacementAfterLoadIsProtectedWithoutUnboundedReading()
    {
        var path = NewPath();
        var session = new LocalRoomSaveSession(path);
        Assert.IsTrue(session.TrySave());
        session.UpdateRoom(session.OrdinaryRoom with { ResumeCenterX = 10 });
        var original = new string('x', 65537);
        Write(path, original);
        Assert.IsFalse(session.TrySave());
        Assert.IsTrue(session.IsWriteProtected);
        Assert.AreEqual(original, File.ReadAllText(path));
    }

    [TestMethod]
    public void UpdatesRejectChangingIdentityOrInvalidValuesWithoutChangingCurrentState()
    {
        var session = new LocalRoomSaveSession(NewPath());
        var original = session.OrdinaryRoom;
        Assert.ThrowsExactly<InvalidDataException>(() => session.UpdateRoom(original with { RoomId = Guid.NewGuid() }));
        Assert.ThrowsExactly<InvalidDataException>(() => session.UpdateRoom(original with { ResumeCenterX = double.NaN }));
        Assert.ThrowsExactly<InvalidDataException>(() => session.UpdateRoom(original with { Camera = original.Camera with { RequestedZoomPercent = double.PositiveInfinity } }));
        Assert.AreEqual(original, session.OrdinaryRoom);
    }

    [TestMethod]
    public void CoordinateClosedBoundsAndPositiveSmallZoomRoundTrip()
    {
        var path = NewPath();
        var session = new LocalRoomSaveSession(path);
        var expected = session.OrdinaryRoom with { ResumeCenterX = -1000000, Camera = new("manual", "custom", 0.01, 1000000, -1000000) };
        session.UpdateRoom(expected);
        Assert.IsTrue(session.TrySave());
        Assert.AreEqual(expected, new LocalRoomSaveSession(path).OrdinaryRoom);
    }

    [TestMethod]
    public void ExplicitSettingsPathKeepsAllFilesInItsOwnDirectory()
    {
        var settingsPath = NewPath();
        var paths = new DangoUserDataPaths(settingsPath);
        Assert.AreEqual(Path.GetFullPath(settingsPath), paths.SettingsPath);
        Assert.AreEqual(Path.GetDirectoryName(Path.GetFullPath(settingsPath)), paths.RootDirectory);
        Assert.AreEqual(Path.Combine(paths.RootDirectory, "appearance.json"), paths.AppearancePath);
        Assert.AreEqual(Path.Combine(paths.RootDirectory, "local-rooms.json"), paths.LocalRoomSavePath);
        Assert.IsFalse(Directory.Exists(paths.RootDirectory));
    }

    [TestMethod]
    public void AccountDirectoryBindsCanonicalOriginAndBothStableIdentitiesWithoutCreatingIt()
    {
        var paths = new DangoUserDataPaths(NewPath());
        var userId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var directory = paths.GetAccountDirectory(new Uri("https://EXAMPLE.invalid:443/api/v1/"), userId, petId);
        Assert.AreEqual(directory, paths.GetAccountDirectory(new Uri("https://example.invalid/"), userId, petId));
        Assert.AreNotEqual(directory, paths.GetAccountDirectory(new Uri("https://example.invalid:8443/"), userId, petId));
        Assert.AreNotEqual(directory, paths.GetAccountDirectory(new Uri("https://other.invalid/"), userId, petId));
        Assert.AreNotEqual(directory, paths.GetAccountDirectory(new Uri("https://example.invalid/"), Guid.NewGuid(), petId));
        Assert.AreNotEqual(directory, paths.GetAccountDirectory(new Uri("https://example.invalid/"), userId, Guid.NewGuid()));
        Assert.IsTrue(directory.StartsWith(paths.RootDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        Assert.IsFalse(directory.Contains("example.invalid", StringComparison.Ordinal));
        Assert.AreEqual(petId.ToString("N"), Path.GetFileName(directory));
        Assert.IsFalse(Directory.Exists(paths.RootDirectory));
    }

    [TestMethod]
    [DataRow("http://example.invalid/")]
    [DataRow("https://fictional-user:fictional-password@example.invalid/")]
    [DataRow("https://example.invalid/?fictional-token=none")]
    [DataRow("https://example.invalid/#fragment")]
    [DataRow("https://example.invalid:0/")]
    [DataRow("/relative")]
    public void AccountDirectoryRejectsUntrustedOrCredentialBearingEndpoints(string endpoint)
    {
        var paths = new DangoUserDataPaths(NewPath());
        Assert.ThrowsExactly<ArgumentException>(() => paths.GetAccountDirectory(new Uri(endpoint, UriKind.RelativeOrAbsolute), Guid.NewGuid(), Guid.NewGuid()));
        Assert.IsFalse(Directory.Exists(paths.RootDirectory));
    }

    [TestMethod]
    public void AccountDirectoryRejectsMissingIdentities()
    {
        var paths = new DangoUserDataPaths(NewPath());
        var endpoint = new Uri("https://example.invalid/");
        Assert.ThrowsExactly<ArgumentException>(() => paths.GetAccountDirectory(endpoint, Guid.Empty, Guid.NewGuid()));
        Assert.ThrowsExactly<ArgumentException>(() => paths.GetAccountDirectory(endpoint, Guid.NewGuid(), Guid.Empty));
    }

    private static string NewPath() => Path.Combine(TestRepository.Root, "artifacts", "test-runs", "local-room-persistence", Guid.NewGuid().ToString("N"), "local-rooms.json");

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }
}
