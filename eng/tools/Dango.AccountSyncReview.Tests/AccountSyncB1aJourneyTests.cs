using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Client.Sync.Accounts;
using Dango.Client.Sync.Pets;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Persistence;
using Dango.Desktop.Rendering.Visuals;
using Dango.Protocol;
using Dango.Server.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.AccountSyncReview.Tests;

[TestClass]
[DoNotParallelize]
[TestCategory("ApprovedAccountSyncIntegration")]
public sealed class AccountSyncB1aJourneyTests
{
    [STATestMethod]
    public void FullApplicationHostRegistersNamesComparesEveryLoginAndSynchronizesRealSavedAppearance()
    {
        // This is the first operation: an earlier stage or a normal test run cannot access resources.
        var owner = AccountSyncTestOwner.Require();
        var elapsed = Stopwatch.StartNew();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        AccountSyncHttpsFixture? fixture = null; Rig? rig = null;
        using var secrets = new MemorySecrets(); using var secondarySecrets = new MemorySecrets();
        try
        {
            fixture = Complete(AccountSyncHttpsFixture.CreateAsync(owner));
            var paths = new DangoUserDataPaths(Path.Combine(owner.RunDirectory, "desktop", "settings.json"));
            Directory.CreateDirectory(paths.RootDirectory);
            var initial = new AppearanceSettingsSession(paths.AppearancePath);
            var eyes = initial.SavedAppearance.EyeLayout;
            initial.StageEyes(new(103, eyes.OvalPercent, eyes.HeightControlPercent, eyes.SpacingControlPercent));
            Assert.IsTrue(initial.TrySave());
            var baseline = new AppearanceSettingsStore().LoadOrDefault(paths.AppearancePath).Settings.Configuration;
            rig = new(fixture, owner, paths, secrets, 1);
            rig.Register();
            Assert.IsNotNull(fixture.Identity);
            rig.Login();
            rig.Wait(() => rig.Controller.IsLoggedIn && rig.PrimaryName == "保存昵称" && rig.PrimaryEnabled);
            Assert.IsFalse(rig.Controller.ConfigurationReady); Assert.IsNull(rig.Controller.RoomClient);
            rig.Uia(u => { u.Value("AccountNickname", " "); u.InvokeId("AccountPrimary"); });
            rig.Wait(() => rig.PrimaryEnabled && rig.Message.Contains("1–24", StringComparison.Ordinal));
            Assert.AreEqual(0, owner.Budget.Snapshot().Operations[AccountSyncOperation.Profile]);
            rig.SetNickname("同步团子");
            rig.Wait(() => rig.Controller.ConfigurationReady && rig.PrimaryEnabled);
            Assert.IsFalse(rig.HasChoice); Assert.AreEqual(0, ConfigAttempts(owner));
            Assert.AreEqual(baseline, rig.Saved); rig.CloseAccount();
            rig.Uia(u => Assert.AreEqual("同步团子", u.Name("AccountButton")));
            rig.SaveScreenshot("first-login.png");
            using var secondary = fixture.CreateClient(secondarySecrets);
            var secondaryIdentity = Complete(secondary.LoginAsync(fixture.Email, fixture.Password, false));
            Assert.AreEqual(fixture.Identity, secondaryIdentity);
            var remote = secondary.CreatePetClient(secondaryIdentity);
            var cloudBaseline = Complete(remote.ReadAsync(CancellationToken.None));
            Assert.AreEqual(1L, cloudBaseline.ConfigurationVersion); Assert.AreEqual(103d, Size(cloudBaseline));
            rig.SaveEyeSize(111); var uploadOriginal = CaptureLocal(paths);
            rig.Wait(() => ConfigAttempts(owner) == 1 && SyncStatus(rig.Controller) == "捏脸已同步。");
            var uploaded = Complete(remote.ReadAsync(CancellationToken.None));
            Assert.AreEqual(2L, uploaded.ConfigurationVersion); Assert.AreEqual(111d, Size(uploaded));
            Assert.AreEqual(111d, DiskSize(paths));

            // The real UI draft must survive a remote commit, and must cause zero writes.
            rig.PreviewEyeSize(118); var draft = rig.Home.BoundAppearance!.DraftAppearance;
            var whileDraft = Complete(remote.UpdateAsync(uploaded.ConfigurationVersion, WithSize(uploaded.Configuration, 116), CancellationToken.None));
            rig.Wait(() => SyncStatus(rig.Controller) == "捏脸草稿保留；应用或取消后继续同步。");
            Assert.AreEqual(draft, rig.Home.BoundAppearance!.DraftAppearance);
            Assert.AreEqual(111d, DiskSize(paths)); Assert.AreEqual(2, ConfigAttempts(owner));
            rig.CancelPreview();
            rig.Wait(() => rig.Saved.EyeLayout.SizePercent == 116 && SyncStatus(rig.Controller) == "捏脸已同步。");
            rig.CloseEditor(); Assert.AreEqual(2, ConfigAttempts(owner));
            var restoredLocal = CaptureLocal(paths);

            rig.Dispose(); rig = null;
            var absent = Complete(remote.UpdateAsync(whileDraft.ConfigurationVersion, WithSize(whileDraft.Configuration, 123), CancellationToken.None));
            rig = new(fixture, owner, paths, secrets, 2);
            rig.Wait(() => rig.HasChoice && rig.PrimaryEnabled == false);
            Assert.AreEqual(3, ConfigAttempts(owner)); Assert.AreEqual(116d, DiskSize(paths));
            rig.SaveScreenshot("remembered-difference.png");
            rig.Choose("AccountCloudChoice"); Assert.AreEqual(3, ConfigAttempts(owner));
            Assert.AreEqual(123d, DiskSize(paths));

            rig.Logout(); rig.CloseAccount(); rig.SaveEyeSize(132);
            Assert.AreEqual(3, ConfigAttempts(owner));
            rig.Login(); rig.Wait(() => rig.HasChoice);
            Assert.AreEqual(3, ConfigAttempts(owner)); Assert.AreEqual(132d, DiskSize(paths));
            var chooseLocalOriginal = CaptureLocal(paths);
            rig.Choose("AccountLocalChoice");
            var chosen = Complete(remote.ReadAsync(CancellationToken.None));
            Assert.AreEqual(5L, chosen.ConfigurationVersion); Assert.AreEqual(132d, Size(chosen));
            Assert.AreEqual(fixture.Identity!.PetId, chosen.PetId);

            rig.Logout(); rig.CloseAccount(); rig.SaveEyeSize(138);
            var chooseCloudOriginal = CaptureLocal(paths);
            Assert.AreEqual(4, ConfigAttempts(owner)); rig.Login(); rig.Wait(() => rig.HasChoice);
            rig.Choose("AccountCloudChoice"); Assert.AreEqual(4, ConfigAttempts(owner)); Assert.AreEqual(132d, DiskSize(paths));
            rig.CloseAccount();
            var followLocal = CaptureLocal(paths);
            var latest = Complete(remote.UpdateAsync(chosen.ConfigurationVersion, WithSize(chosen.Configuration, 128), CancellationToken.None));
            rig.Wait(() => rig.Saved.EyeLayout.SizePercent == 128 && SyncStatus(rig.Controller) == "捏脸已同步。");
            Assert.IsFalse(rig.Home.IsAccountPanelOpen); Assert.AreEqual(5, ConfigAttempts(owner));
            rig.OpenAccount(); rig.Uia(u => u.InvokeName("修改昵称"));
            rig.Wait(() => rig.PrimaryName == "保存昵称" && rig.PrimaryEnabled);
            rig.SetNickname("新同步团子"); rig.Wait(() => rig.Controller.Nickname == "新同步团子" && rig.PrimaryEnabled);
            rig.CloseAccount(); rig.Uia(u => Assert.AreEqual("新同步团子", u.Name("AccountButton")));
            rig.Dispose(); rig = null;
            rig = new(fixture, owner, paths, secrets, 3);
            rig.Wait(() => rig.Controller.ConfigurationReady);
            Assert.IsFalse(rig.HasChoice); Assert.IsFalse(rig.Home.IsAccountPanelOpen);
            rig.Uia(u => Assert.AreEqual("新同步团子", u.Name("AccountButton")));
            Assert.AreEqual(5, ConfigAttempts(owner)); Assert.AreEqual(128d, DiskSize(paths));
            Assert.AreEqual(4, owner.Budget.Snapshot().Operations[AccountSyncOperation.Login]);
            rig.SaveScreenshot("remembered-equal.png");
            rig.AssertHealthyFrame();
            var choices = new[]
            {
                new AccountSyncArchiveExpectation("remembered-cloud", fixture.Endpoint, restoredLocal, absent, PetConfigurationChoice.Cloud, PetChoiceOutcome.LocalApplied, 4),
                new AccountSyncArchiveExpectation("explicit-local", fixture.Endpoint, chooseLocalOriginal, absent, PetConfigurationChoice.Local, PetChoiceOutcome.CloudUpdated, 5),
                new AccountSyncArchiveExpectation("explicit-cloud", fixture.Endpoint, chooseCloudOriginal, chosen, PetConfigurationChoice.Cloud, PetChoiceOutcome.LocalApplied, 5)
            };
            var sync = new[]
            {
                new AccountSyncArchiveExpectation("saved-upload", fixture.Endpoint, uploadOriginal, cloudBaseline, PetConfigurationChoice.Local, PetChoiceOutcome.CloudUpdated, 2),
                new AccountSyncArchiveExpectation("draft-cancel-follow", fixture.Endpoint, uploadOriginal, whileDraft, PetConfigurationChoice.Cloud, PetChoiceOutcome.LocalApplied, 3),
                new AccountSyncArchiveExpectation("online-follow", fixture.Endpoint, followLocal, latest, PetConfigurationChoice.Cloud, PetChoiceOutcome.LocalApplied, 6)
            };
            ValidateArchives(owner, paths, fixture.Identity, fixture.Endpoint, choices, sync);
            rig.Logout(); Complete(secondary.LogoutAsync());
            var final = Complete(fixture.InScopeAsync(async services =>
            {
                var db = services.GetRequiredService<DangoDbContext>();
                var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == fixture.Identity.UserId);
                var pet = await db.Pets.AsNoTracking().SingleAsync(p => p.UserId == user.Id);
                var sessions = await db.AuthSessions.AsNoTracking().Where(s => s.UserId == user.Id).ToListAsync();
                var mail = await (from job in db.EmailJobs.AsNoTracking()
                                  join challenge in db.EmailChallenges.AsNoTracking() on job.ChallengeId equals challenge.Id
                                  where challenge.NormalizedEmail == user.NormalizedEmail select job).SingleAsync();
                return new { UserId = user.Id, PetId = pet.Id, Nickname = user.Nickname, user.ProfileVersion,
                    pet.ConfigurationVersion, Sessions = sessions.Count, Revoked = sessions.Count(s => s.RevokedAt != null),
                    mail.Attempts, MailState = mail.State.ToString(), DefaultRoomId = fixture.DefaultRoomId };
            }));
            Assert.AreEqual(fixture.Identity.UserId, final.UserId); Assert.AreEqual(fixture.Identity.PetId, final.PetId);
            Assert.AreEqual("新同步团子", final.Nickname); Assert.AreEqual(3L, final.ProfileVersion);
            Assert.AreEqual(6L, final.ConfigurationVersion); Assert.AreEqual(4, final.Sessions); Assert.AreEqual(4, final.Revoked);
            Assert.AreEqual(0, final.Attempts); Assert.AreEqual("Pending", final.MailState);
            Assert.AreEqual(6L, latest.ConfigurationVersion);
            CollectionAssert.AreEquivalent(AccountSyncAttemptBudget.Limits.Select(p => (p.Key, p.Value)).ToArray(),
                owner.Budget.Snapshot().Operations.Select(p => (p.Key, p.Value)).ToArray());
            owner.Budget.CheckAlive();
            owner.WriteEvidence("journey-result.json", new { Version = 1, owner.OwnerId, Passed = true, Final = final, ConfigurationWrites = 5,
                LoginWrites = 4, ProfileWrites = 2, LogoutWrites = 4, DesktopHosts = 3, HttpsHosts = 1,
                HttpRequests = owner.Budget.HttpRequests, DurationSeconds = elapsed.Elapsed.TotalSeconds,
                UsesSystemTrust = false, UsesWindowsCredentials = false, ActualFormalHost = true, ActualApplicationHost = true,
                HealthyNativeFrames = true, ExactChoiceArchives = 3, ExactSyncArchives = 3 });
        }
        catch
        {
            // A failed journey immediately closes business access before cleanup pumps the dispatcher.
            owner.Abort();
            throw;
        }
        finally
        {
            try { rig?.Dispose(); }
            finally
            {
                try { if (fixture is not null) Complete(fixture.DisposeAsync().AsTask()); }
                finally { owner.Dispose(); SynchronizationContext.SetSynchronizationContext(previous); }
            }
        }
    }

    private static int ConfigAttempts(AccountSyncTestOwner owner) => owner.Budget.Snapshot().Operations[AccountSyncOperation.Configuration];
    private static string? SyncStatus(DesktopAccountController controller) => Field<string?>(controller, "configurationStatus");
    private static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj)!;
    private static double Size(PetConfigurationSnapshot snapshot) => snapshot.Configuration.GetProperty("eyeSizePercent").GetDouble();
    private static double DiskSize(DangoUserDataPaths paths) => new AppearanceSettingsStore().LoadOrDefault(paths.AppearancePath).Settings.EyeLayout.SizePercent;
    private static JsonElement WithSize(JsonElement original, double size)
    {
        var fields = original.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        fields["eyeSizePercent"] = JsonSerializer.SerializeToElement(size); return JsonSerializer.SerializeToElement(fields);
    }

    private static LocalPetConfigurationSnapshot CaptureLocal(DangoUserDataPaths paths)
    {
        var session = new AppearanceSettingsSession(paths.AppearancePath);
        return Complete(new LocalPetConfigurationRepository(paths.AppearancePath, session, Dispatcher.CurrentDispatcher).CaptureAsync(CancellationToken.None));
    }

    private static void ValidateArchives(AccountSyncTestOwner owner, DangoUserDataPaths paths, AccountIdentity identity, Uri origin,
        AccountSyncArchiveExpectation[] choices, AccountSyncArchiveExpectation[] sync)
    {
        var root = paths.GetAccountDirectory(origin, identity.UserId, identity.PetId);
        foreach (var (name, expectations) in new[] { ("configuration-choices", choices), ("configuration-sync", sync) })
        {
            var directory = Path.Combine(root, name); var journal = new PetConfigurationFileJournal(directory);
            var originals = Directory.GetFiles(directory, "*.originals.json"); Assert.AreEqual(3, originals.Length);
            Assert.AreEqual(3, Directory.GetFiles(directory, "*.receipt.json").Length);
            Assert.AreEqual(6, Directory.GetFiles(directory).Length);
            var remaining = expectations.ToList(); var evidence = new List<object>();
            foreach (var path in originals)
            {
                var id = Guid.ParseExact(Path.GetFileName(path).Split('.')[0], "N");
                var saved = Complete(journal.LoadOriginalsAsync(id, CancellationToken.None));
                var receipt = Complete(journal.LoadReceiptAsync(id)); Assert.IsNotNull(receipt);
                var expected = remaining.Single(e => e.Cloud.ConfigurationVersion == saved.Cloud.ConfigurationVersion && e.Choice == saved.Choice);
                AccountSyncArchiveContract.Verify(saved, receipt, expected); remaining.Remove(expected);
                evidence.Add(new { expected.Step, JournalId = id,
                    LocalOriginalSha256 = Convert.ToHexString(SHA256.HashData(saved.Local.OriginalBytes.Span)),
                    saved.Cloud.ConfigurationVersion, receipt.Choice, receipt.Outcome, receipt.ObservedCloudVersion });
            }
            Assert.AreEqual(0, remaining.Count);
            owner.WriteEvidence(name + "-evidence.json", new { Count = originals.Length, ExactOriginalsAndReceipts = true, UserId = identity.UserId, PetId = identity.PetId, Archives = evidence });
        }
    }

    private static T Complete<T>(Task<T> task) { Wait(() => task.IsCompleted); return task.GetAwaiter().GetResult(); }
    private static void Complete(Task task) { Wait(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Wait(Func<bool> condition, int seconds = 30)
    {
        if (condition()) return;
        var frame = new DispatcherFrame(); var elapsed = Stopwatch.StartNew(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => { if (condition() || elapsed.Elapsed > TimeSpan.FromSeconds(seconds)) frame.Continue = false; };
        timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
        Assert.IsTrue(condition(), "The bounded approved application transition did not finish.");
    }

    private sealed class Rig : IDisposable
    {
        private readonly AccountSyncHttpsFixture fixture;
        private readonly AccountSyncTestOwner owner;
        private readonly DangoApplicationHost host;
        private readonly ControlCenterWindow window;
        private readonly nint handle;
        private readonly int index;
        private readonly AccountSyncAppearanceNavigator appearanceNavigator;
        private int navigationSequence;
        private Task? pendingUia;
        private bool disposed;
        internal ProductHomeView Home => window.ProductHome;
        internal DesktopAccountController Controller => Field<DesktopAccountController>(host, "accountController");
        internal DangoAppearanceConfiguration Saved => Home.BoundAppearance!.SavedAppearance;
        internal bool PrimaryEnabled => ((Button)Home.AccountPanel.FindName("AccountPrimary")).IsEnabled;
        internal string PrimaryName => AutomationProperties.GetName((Button)Home.AccountPanel.FindName("AccountPrimary"));
        internal string Message => ((TextBlock)Home.AccountPanel.FindName("AccountMessage")).Text;
        internal bool HasChoice => Home.AccountPanel.FindName("AccountCloudChoice") is RadioButton;

        internal Rig(AccountSyncHttpsFixture fixture, AccountSyncTestOwner owner, DangoUserDataPaths paths, MemorySecrets secrets, int index)
        {
            this.fixture = fixture; this.owner = owner; this.index = index;
            Assert.IsTrue(index is >= 1 and <= 3);
            var native = Path.Combine(RepositoryRoot(), "artifacts", "local-room", "account-profile-sync-20261010", "bin", "Dango.Desktop", "release", "dango_physics_module.dll");
            Assert.AreEqual("BFDD6CD68835540569A7D96EFCDE9B4580846983BDD717111D6BC68811150507", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(native))));
            host = DangoApplicationHost.CreateForAccountIntegration(paths.SettingsPath, native,
                new(fixture.Endpoint, secrets, (origin, store) => { Assert.AreEqual(fixture.Endpoint, origin); return fixture.CreateClient(store); }));
            try
            {
                fixture.Resource("DesktopStarted", new { Index = index, Attempt = true });
                host.Start(); window = Field<ControlCenterWindow>(host, "controlCenterWindow");
                window.Left = 80; window.Top = 80; window.ShowInTaskbar = false;
                handle = new WindowInteropHelper(window).Handle; Assert.AreNotEqual(nint.Zero, handle);
                Assert.AreEqual(Process.GetCurrentProcess().Id, AutomationElement.FromHandle(handle).Current.ProcessId);
                appearanceNavigator = new(id => Uia(u => u.InvokeId(id)),
                    (id, value) => Uia(u => u.Range(id, value)), Wait, ReadNavigationState, TraceNavigation);
                Wait(() => Controller is not null && PrimaryEnabled);
                AssertHealthyFrame();
                TraceNavigation("host-ready", ReadNavigationState());
            }
            catch (Exception startupError)
            {
                owner.Abort();
                try { DisposeCore(checkHealth: false); }
                catch (Exception cleanupError)
                {
                    throw new AggregateException("Application startup and owned cleanup both failed.", startupError, cleanupError);
                }
                throw;
            }
        }

        internal void Register()
        {
            OpenAccount(); Uia(u => u.InvokeName("受邀注册")); Wait(() => PrimaryName == "发送验证邮件" && PrimaryEnabled);
            Uia(u => { u.Value("AccountEmail", fixture.Email); u.Value("AccountInvitationId", fixture.InvitationId.ToString("D")); });
            // PasswordBox exposes no ValuePattern; only this synthetic fixture secret is supplied in-process.
            ((PasswordBox)Home.AccountPanel.FindName("AccountInvitationProof")).Password = fixture.InvitationProof;
            Uia(u => u.InvokeId("AccountPrimary")); Wait(() => PrimaryName == "刷新验证状态" && PrimaryEnabled);
            Complete(fixture.ConfirmAsync()); Uia(u => u.InvokeId("AccountPrimary"));
            Wait(() => PrimaryName == "完成注册" && PrimaryEnabled);
            ((PasswordBox)Home.AccountPanel.FindName("AccountPassword")).Password = fixture.Password;
            ((PasswordBox)Home.AccountPanel.FindName("AccountConfirmPassword")).Password = fixture.Password;
            Uia(u => u.InvokeId("AccountPrimary")); Wait(() => PrimaryName == "登录" && PrimaryEnabled);
        }

        internal void Login()
        {
            if (!Home.IsAccountPanelOpen) OpenAccount(); Wait(() => PrimaryName == "登录" && PrimaryEnabled);
            Uia(u => { u.Value("AccountEmail", fixture.Email); u.Check("AccountRememberLogin"); });
            ((PasswordBox)Home.AccountPanel.FindName("AccountPassword")).Password = fixture.Password;
            Uia(u => u.InvokeId("AccountPrimary"));
        }
        internal void SetNickname(string value) => Uia(u => { u.Value("AccountNickname", value); u.InvokeId("AccountPrimary"); });
        internal void Choose(string source) { Uia(u => { u.Select(source); u.InvokeId("AccountPrimary"); }); Wait(() => Controller.ConfigurationReady && PrimaryEnabled); }
        internal void OpenAccount() { Uia(u => u.InvokeId("AccountButton")); Wait(() => Home.IsAccountPanelOpen && PrimaryEnabled); }
        internal void CloseAccount()
        {
            TraceNavigation("before-close-account", ReadNavigationState());
            Uia(u => u.InvokeName("继续本地游玩")); Wait(() => !Home.IsAccountPanelOpen);
            TraceNavigation("account-closed", ReadNavigationState());
        }
        internal void Logout()
        {
            if (!Home.IsAccountPanelOpen) OpenAccount(); Uia(u => u.InvokeName("退出登录"));
            Wait(() => !Controller.IsLoggedIn && PrimaryName == "登录" && PrimaryEnabled);
        }
        internal void PreviewEyeSize(double value) => appearanceNavigator.PreviewEyeSize(value);
        internal void SaveEyeSize(double value) => appearanceNavigator.SaveEyeSize(value);
        internal void CancelPreview() => appearanceNavigator.CancelPreview();
        internal void CloseEditor() => appearanceNavigator.CloseEditor();
        private AccountSyncAppearanceNavigationState ReadNavigationState()
        {
            bool Ready(string id) => Home.FindName(id) is FrameworkElement element
                && element.IsVisible && element.IsEnabled && element.IsHitTestVisible
                && element.ActualWidth > 0 && element.ActualHeight > 0;
            var editor = !Home.IsEditing ? AccountSyncAppearanceEditor.None
                : Home.SelectedEditor == HomeEditor.Face ? AccountSyncAppearanceEditor.Face
                : AccountSyncAppearanceEditor.Wardrobe;
            var appearance = Home.BoundAppearance!;
            return new(Home.IsAccountPanelOpen, Home.IsSettingsOpen, Home.IsInPorch, Home.IsDangoAway,
                editor, Ready("FaceButton"), Ready("AppearanceEyesTab"),
                Ready("AppearanceEyesPanel") && Ready("EyeSizeSlider"), Ready("ApplyButton"),
                Ready("CancelAppearanceButton"), Ready(editor == AccountSyncAppearanceEditor.Face
                    ? "FaceCloseEditorButton" : "CloseEditorButton"), appearance.HasPendingSave,
                appearance.DraftAppearance.EyeLayout.SizePercent, appearance.SavedAppearance.EyeLayout.SizePercent);
        }
        private void TraceNavigation(string phase, AccountSyncAppearanceNavigationState state)
        {
            // Only this owned synthetic window's page and appearance state; never capture input or secrets.
            Assert.IsTrue(navigationSequence < 128, "The bounded navigation evidence limit was exceeded.");
            var sequence = ++navigationSequence;
            owner.WriteEvidence($"navigation-{index}-{sequence:D3}.json",
                new { Version = 1, owner.OwnerId, DesktopIndex = index, Sequence = sequence, Phase = phase, State = state });
        }
        internal void Wait(Func<bool> condition) => AccountSyncB1aJourneyTests.Wait(condition);
        internal void Uia(Action<UiaClient> action)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Assert.IsTrue(pendingUia is null, "The previous owned UIA worker has not settled.");
            pendingUia = Task.Run(() =>
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed), this);
                action(new(handle, () => !Volatile.Read(ref disposed)));
            });
            try { Complete(pendingUia); }
            catch { owner.Abort(); throw; }
            finally
            {
                if (pendingUia.IsCompleted)
                {
                    if (pendingUia.IsFaulted) _ = pendingUia.Exception;
                    pendingUia = null;
                }
            }
        }
        internal void SaveScreenshot(string name)
        {
            AssertHealthyFrame();
            window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = new FileStream(Path.Combine(owner.RunDirectory, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            encoder.Save(file); file.Flush(true);
        }
        internal void AssertHealthyFrame()
        {
            var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
            Wait(() => runtime.InitializationState == SceneInitializationState.Ready && runtime.LastPhysicsFrame is { Succeeded: true }
                && runtime.LastFrame is { Bodies.Count: > 0 } && Home.OwnsLiveFrameDriver);
            Assert.IsNull(runtime.LastError); Assert.IsNull(runtime.AttachmentError);
            Assert.AreSame(runtime, Home.BoundRuntime);
            Assert.IsTrue(runtime.EntityIds.Count > 0);
            var previous = runtime.LastPhysicsFrame;
            Wait(() => !ReferenceEquals(previous, runtime.LastPhysicsFrame));
            Assert.IsTrue(runtime.LastPhysicsFrame!.Succeeded); Assert.IsNull(runtime.LastError);
        }
        public void Dispose() => DisposeCore(checkHealth: true);
        private void DisposeCore(bool checkHealth)
        {
            if (disposed) return; disposed = true;
            Exception? failure = null;
            var hostDisposed = false; var pumpDrained = false; var hwndClosed = false; var uiaWorkerSettled = false;
            void RecordFailure(Exception error) { owner.Abort(); failure ??= error; }
            DesktopAccountController? controller = null;
            try { controller = Controller; } catch (Exception ex) { RecordFailure(ex); }
            try { if (checkHealth) AssertHealthyFrame(); } catch (Exception ex) { RecordFailure(ex); }
            try { host.Dispose(); hostDisposed = true; } catch (Exception ex) { RecordFailure(ex); }
            try
            {
                Assert.IsNotNull(controller, "The owned account controller was not captured.");
                Complete(Field<Task>(controller, "configurationPump")); pumpDrained = true;
            }
            catch (Exception ex) { RecordFailure(ex); }
            try
            {
                Assert.AreNotEqual(nint.Zero, handle, "An uncaptured HWND cannot prove window cleanup.");
                Wait(() => !IsWindow(handle)); hwndClosed = true;
            }
            catch (Exception ex) { RecordFailure(ex); }
            try
            {
                Wait(() => pendingUia is null || pendingUia.IsCompleted);
                if (pendingUia?.IsFaulted == true) _ = pendingUia.Exception;
                pendingUia = null; uiaWorkerSettled = true;
            }
            catch (Exception ex) { RecordFailure(ex); }
            fixture.Resource("DesktopDisposed", new { Index = index, Disposed = hostDisposed, HwndClosed = hwndClosed,
                ConfigurationPumpDrained = pumpDrained, UiaWorkerSettled = uiaWorkerSettled });
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed class UiaClient(nint handle, Func<bool> mayOperate)
    {
        private AutomationElement Root
        {
            get
            {
                Assert.IsTrue(mayOperate() && IsWindow(handle), "The owned UIA window has closed.");
                var root = AutomationElement.FromHandle(handle);
                Assert.AreEqual(Process.GetCurrentProcess().Id, root.Current.ProcessId);
                return root;
            }
        }
        private AutomationElement Id(string id) => Root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id))
            ?? throw new AssertFailedException("Approved HWND control missing: " + id);
        internal string Name(string id) => Id(id).Current.Name;
        internal void Value(string id, string value) => ((ValuePattern)Ready(id).GetCurrentPattern(ValuePattern.Pattern)).SetValue(value);
        internal void Check(string id) { var pattern = (TogglePattern)Ready(id).GetCurrentPattern(TogglePattern.Pattern); if (pattern.Current.ToggleState != ToggleState.On) pattern.Toggle(); }
        internal void Select(string id) => ((SelectionItemPattern)Ready(id).GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        private AutomationElement Ready(string id)
        {
            var element = Id(id);
            Assert.IsTrue(element.Current.IsEnabled && !element.Current.IsOffscreen, "Approved HWND control unavailable: " + id);
            return element;
        }
        internal void Range(string id, double value) => ((RangeValuePattern)Ready(id).GetCurrentPattern(RangeValuePattern.Pattern)).SetValue(value);
        internal void InvokeId(string id) => ((InvokePattern)Ready(id).GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        internal void InvokeName(string name)
        {
            var button = Root.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.NameProperty, name),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))) ?? throw new AssertFailedException("Approved HWND button missing: " + name);
            Assert.IsTrue(mayOperate() && button.Current.IsEnabled && !button.Current.IsOffscreen,
                "Approved HWND button unavailable: " + name);
            ((InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        }
    }

    private sealed class MemorySecrets : IAccountSecretStore, IDisposable
    {
        private readonly Dictionary<(Uri, string), byte[]> values = [];
        public Task<byte[]?> ReadAsync(Uri origin, string slot, CancellationToken ct) { ct.ThrowIfCancellationRequested(); lock (values) return Task.FromResult(values.TryGetValue((origin, slot), out var bytes) ? bytes.ToArray() : null); }
        public Task WriteAsync(Uri origin, string slot, byte[] value, CancellationToken ct) { ct.ThrowIfCancellationRequested(); lock (values) { Delete(origin, slot); values[(origin, slot)] = value.ToArray(); } return Task.CompletedTask; }
        public Task DeleteAsync(Uri origin, string slot, CancellationToken ct) { ct.ThrowIfCancellationRequested(); lock (values) Delete(origin, slot); return Task.CompletedTask; }
        private void Delete(Uri origin, string slot) { if (values.Remove((origin, slot), out var bytes)) CryptographicOperations.ZeroMemory(bytes); }
        public void Dispose() { lock (values) { foreach (var bytes in values.Values) CryptographicOperations.ZeroMemory(bytes); values.Clear(); } }
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? p = new(AppContext.BaseDirectory); p is not null; p = p.Parent)
            if (File.Exists(Path.Combine(p.FullName, "global.json")) && File.Exists(Path.Combine(p.FullName, "AGENTS.md"))) return p.FullName;
        throw new AssertFailedException("Approved candidate is outside the selected worktree.");
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint handle);
}
