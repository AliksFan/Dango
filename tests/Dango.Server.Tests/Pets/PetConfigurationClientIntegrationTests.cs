using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dango.Client.Sync.Pets;
using Dango.Protocol;
using Dango.Server.Tests.Accounts;

namespace Dango.Server.Tests.Pets;

[TestClass]
[TestCategory("PostgresIntegration")]
public sealed class PetConfigurationClientIntegrationTests
{
    [TestMethod]
    public async Task RealHttpsResponseLossRetriesOriginalVersionAndKeepsDurableOriginalsAndPetIdentity()
    {
        await using var fixture = await FormalTestFixture.CreateAsync(startHttp: true);
        var account = await PetTestSupport.RegisterAsync(fixture);
        var before = await PetTestSupport.GraphAsync(fixture, account);
        using var login = fixture.CreateClient(out var cookies);
        using (var response = await FormalTestFixture.SendAsync(login, HttpMethod.Post, "api/v1/auth/login",
            new { email = account.Email, password = account.Password, remembered = false }))
            Assert.IsTrue(response.IsSuccessStatusCode);
        var loss = new DropAcceptedPatch(fixture.CreatePinnedHandler(cookies));
        using var remote = PetConfigurationHttpClient.ForTesting(fixture.Endpoint, account.UserId, account.PetId, loss);
        var target = PetTestSupport.Appearance("#123456");
        var local = new LocalProbe(target);
        var directory = Path.Combine(FindRepository(), "artifacts", "backend-b2", "client-archive", Guid.NewGuid().ToString("N"));
        var journal = new PetConfigurationFileJournal(directory);
        var coordinator = new PetConfigurationChoiceCoordinator(remote, local, journal);
        var review = await coordinator.BeginAsync();
        Assert.IsFalse(Directory.Exists(directory), "Preview must not archive or overwrite either side.");
        var result = await coordinator.ChooseAsync(review, PetConfigurationChoice.Local);
        Assert.AreEqual(PetChoiceOutcome.TargetSatisfied, result.Outcome,
            "A recovered GET proves current target state, never that the discarded response was delivered.");
        Assert.AreEqual(2, loss.PatchCount);
        Assert.AreEqual(0, local.ApplyCount);
        var current = await remote.ReadAsync(default);
        Assert.AreEqual(review.Originals.Cloud.ConfigurationVersion + 1, current.ConfigurationVersion);
        Assert.IsTrue(AppearanceContract.Equivalent(target, current.Configuration));
        PetTestSupport.AssertIdentityAndRoomUnchanged(before, await PetTestSupport.GraphAsync(fixture, account));
        var reopened = new PetConfigurationFileJournal(directory);
        var originals = await reopened.LoadOriginalsAsync(result.JournalId, default);
        Assert.AreEqual(PetConfigurationChoice.Local, originals.Choice);
        Assert.AreEqual(account.UserId, originals.Cloud.UserId);
        Assert.AreEqual(account.PetId, originals.Cloud.PetId);
        Assert.AreEqual(fixture.Endpoint, originals.Origin);
        Assert.IsTrue(review.Originals.Local.SameOriginal(originals.Local));
        Assert.IsTrue(AppearanceContract.Equivalent(review.Originals.Cloud.Configuration, originals.Cloud.Configuration));
        Assert.AreEqual(PetChoiceOutcome.TargetSatisfied, (await reopened.LoadReceiptAsync(result.JournalId))!.Outcome);
        Assert.AreEqual(2, loss.PatchCount, "Reading an archive never automatically replays a mutation.");
    }

    private sealed class DropAcceptedPatch(HttpMessageHandler handler) : DelegatingHandler(handler)
    {
        public int PatchCount { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = await base.SendAsync(request, ct);
            if (request.Method == HttpMethod.Patch && ++PatchCount == 1)
            {
                Assert.IsTrue(response.IsSuccessStatusCode);
                response.Dispose();
                throw new HttpRequestException("fictional_response_loss");
            }
            return response;
        }
    }

    // The real WPF file repository has separate disk/CAS integration gates. This probe measures coordinator calls only.
    private sealed class LocalProbe(JsonElement configuration) : ILocalPetConfigurationRepository
    {
        private readonly LocalPetConfigurationSnapshot snapshot = new(true, Encoding.UTF8.GetBytes(configuration.GetRawText()), configuration);
        public int ApplyCount { get; private set; }
        public Task<LocalPetConfigurationSnapshot> CaptureAsync(CancellationToken ct) => Task.FromResult(snapshot);
        public Task ApplyAsync(LocalPetConfigurationSnapshot expected, JsonElement target, CancellationToken ct)
        { ApplyCount++; throw new InvalidOperationException("local_choice_must_not_overwrite_local"); }
    }
    private static string FindRepository()
    {
        for (var part = new DirectoryInfo(AppContext.BaseDirectory); part is not null; part = part.Parent)
            if (File.Exists(Path.Combine(part.FullName, "AGENTS.md"))) return part.FullName;
        throw new InvalidOperationException("repository_required");
    }
}
