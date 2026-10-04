using Dango.Desktop.AppShell.Rooms;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class RoomNavigationCoordinatorTests
{
    private static readonly AuthorityRoomSnapshot Original = new("fictional-room-a", 10);
    private static readonly AuthorityRoomSnapshot Target = new("fictional-room-b", 11);

    [TestMethod]
    public void NoRoomRestoresHomeAndNeverInventsAnAutomaticRoom()
    {
        var navigation = new RoomNavigationCoordinator();
        Assert.AreEqual(RoomNavigationView.Home, navigation.View);
        Assert.IsNull(navigation.Authority.RoomId);
        Assert.IsFalse(navigation.CanResumeRoom);
        Assert.IsFalse(navigation.CanHideControlCenter);
        navigation.ShowPorch();
        navigation.ShowHome();
        Assert.IsNull(navigation.Authority.RoomId);
        Assert.IsNull(navigation.BeginEntry());
        Assert.IsFalse(navigation.HideRoom());
    }

    [TestMethod]
    public void BrowsingAndSelectingDoorsDoesNotChangeAuthorityOrSendOriginalRoomInput()
    {
        var navigation = new RoomNavigationCoordinator(Original);
        navigation.ShowPorch();
        Assert.IsTrue(navigation.SelectDoor(Target.RoomId!));
        Assert.AreEqual(Target.RoomId, navigation.SelectedDoorId);
        Assert.AreEqual(Original, navigation.Authority);
        Assert.IsTrue(navigation.CanResumeRoom);
        Assert.IsFalse(navigation.CanSendRoomInput,
            "Moving in the porch must never control the still-authoritative original room.");
        navigation.ShowHome();
        Assert.AreEqual(Original, navigation.Authority);
        Assert.IsNull(navigation.SelectedDoorId);
    }

    [TestMethod]
    public void UnselectedOrRepeatedEntryCannotCreateTwoAttempts()
    {
        var navigation = new RoomNavigationCoordinator(Original);
        Assert.IsFalse(navigation.SelectDoor(Target.RoomId!));
        navigation.ShowPorch();
        Assert.IsNull(navigation.BeginEntry());
        Assert.IsTrue(navigation.SelectDoor(Target.RoomId!));
        var attempt = navigation.BeginEntry();
        Assert.IsNotNull(attempt);
        Assert.AreEqual(RoomNavigationView.Entering, navigation.View);
        Assert.IsNull(navigation.BeginEntry());
        Assert.IsFalse(navigation.SelectDoor("fictional-room-c"));
        Assert.AreEqual(Original, navigation.Authority);
        Assert.IsFalse(navigation.CanHideControlCenter);
    }

    [TestMethod]
    public void AuthorityAndCurrentGenerationHostReadyAreBothRequiredToRevealRoom()
    {
        var navigation = new RoomNavigationCoordinator(Original);
        var attempt = Begin(navigation);
        Assert.IsFalse(navigation.ConfirmHostReady(attempt));
        Assert.AreEqual(Original, navigation.Authority);
        Assert.IsFalse(navigation.CanHideControlCenter);

        Assert.IsTrue(navigation.ConfirmAdmission(attempt, Target));
        Assert.AreEqual(Target, navigation.Authority);
        Assert.AreEqual(RoomNavigationView.Entering, navigation.View);
        Assert.IsFalse(navigation.CanSendRoomInput);
        Assert.IsFalse(navigation.CanHideControlCenter,
            "A successful admission must not hide the control center before a usable frame.");

        Assert.IsTrue(navigation.ConfirmHostReady(attempt));
        Assert.AreEqual(RoomNavigationView.RoomVisible, navigation.View);
        Assert.IsTrue(navigation.CanSendRoomInput);
        Assert.IsTrue(navigation.CanHideControlCenter);
        Assert.IsFalse(navigation.ConfirmHostReady(attempt), "A ready completion is consumed once.");
    }

    [TestMethod]
    public void RejectedAdmissionPreservesTheLatestAuthorityAndReturnsToPorch()
    {
        var navigation = new RoomNavigationCoordinator(Original);
        var attempt = Begin(navigation);
        Assert.IsTrue(navigation.RejectAdmission(attempt));
        Assert.AreEqual(Original, navigation.Authority);
        Assert.AreEqual(RoomNavigationView.Porch, navigation.View);
        Assert.IsFalse(navigation.ConfirmAdmission(attempt, Target));
        Assert.IsFalse(navigation.ConfirmHostReady(attempt));
    }

    [TestMethod]
    public void ReturnedPorchAndNewAttemptsRejectOlderAdmissionAndReadyCompletions()
    {
        var navigation = new RoomNavigationCoordinator(Original);
        var old = Begin(navigation);
        navigation.ShowPorch();
        navigation.SelectDoor(Target.RoomId!);
        var latest = navigation.BeginEntry()!.Value;
        Assert.AreNotEqual(old.Generation, latest.Generation);
        Assert.IsFalse(navigation.ConfirmAdmission(old, Target));
        Assert.IsFalse(navigation.ConfirmHostReady(old));
        Assert.AreEqual(Original, navigation.Authority);
        Assert.IsTrue(navigation.ConfirmAdmission(latest, Target));
        Assert.IsFalse(navigation.ConfirmHostReady(old));
        Assert.IsTrue(navigation.ConfirmHostReady(latest));
    }

    [TestMethod]
    public void ForcedNoRoomDuringEntryInvalidatesLateAdmissionAndHostReady()
    {
        var navigation = new RoomNavigationCoordinator(Original);
        var attempt = Begin(navigation);
        Assert.IsTrue(navigation.ConfirmAdmission(attempt, Target));
        var noRoom = new AuthorityRoomSnapshot(null, 12);
        Assert.IsTrue(navigation.ApplyAuthority(noRoom));
        Assert.AreEqual(noRoom, navigation.Authority);
        Assert.AreEqual(RoomNavigationView.Home, navigation.View);
        Assert.IsFalse(navigation.CanResumeRoom);
        Assert.IsFalse(navigation.ConfirmAdmission(attempt, Target));
        Assert.IsFalse(navigation.ConfirmHostReady(attempt));
        Assert.IsFalse(navigation.CanHideControlCenter);
    }

    [TestMethod]
    public void OlderOrConflictingSnapshotsCannotReviveAnInvalidatedRoom()
    {
        var navigation = new RoomNavigationCoordinator(new AuthorityRoomSnapshot(null, 12));
        Assert.IsFalse(navigation.ApplyAuthority(Target));
        Assert.IsFalse(navigation.ApplyAuthority(new AuthorityRoomSnapshot("fictional-room-b", 12)));
        Assert.IsTrue(navigation.ApplyAuthority(new AuthorityRoomSnapshot(null, 12)));
        Assert.IsNull(navigation.Authority.RoomId);
        Assert.AreEqual(RoomNavigationView.Home, navigation.View);
    }

    [TestMethod]
    public void WrongRoomOrUnadvancedAuthorityDoesNotCountAsAdmission()
    {
        var navigation = new RoomNavigationCoordinator(Original);
        var attempt = Begin(navigation);
        Assert.IsFalse(navigation.ConfirmAdmission(attempt, new AuthorityRoomSnapshot("fictional-room-c", 11)));
        Assert.IsFalse(navigation.ConfirmAdmission(attempt, new AuthorityRoomSnapshot(Target.RoomId, 10)));
        Assert.IsFalse(navigation.ConfirmAdmission(attempt, new AuthorityRoomSnapshot(Target.RoomId, 9)));
        Assert.AreEqual(Original, navigation.Authority);
        Assert.IsFalse(navigation.ConfirmHostReady(attempt));
        Assert.IsFalse(navigation.CanHideControlCenter);
    }

    [TestMethod]
    public void PresentationFailureAfterCommitKeepsTheCommittedRoomNotTheOriginal()
    {
        var navigation = new RoomNavigationCoordinator(Original);
        var attempt = Begin(navigation);
        Assert.IsTrue(navigation.ConfirmAdmission(attempt, Target));
        Assert.IsTrue(navigation.FailPresentation(attempt));
        Assert.AreEqual(Target, navigation.Authority);
        Assert.AreEqual(RoomNavigationView.PresentationRecovery, navigation.View);
        Assert.IsFalse(navigation.CanHideControlCenter);
        Assert.IsFalse(navigation.ConfirmHostReady(attempt));
        navigation.ShowPorch();
        Assert.AreEqual(Target, navigation.Authority);
    }

    [TestMethod]
    public void HideAndReturnOnlyChangePresentationNotAuthority()
    {
        var navigation = new RoomNavigationCoordinator(Original);
        var attempt = Begin(navigation);
        navigation.ConfirmAdmission(attempt, Target);
        navigation.ConfirmHostReady(attempt);
        Assert.IsTrue(navigation.HideRoom());
        Assert.AreEqual(RoomNavigationView.RoomHidden, navigation.View);
        Assert.AreEqual(Target, navigation.Authority);
        Assert.IsFalse(navigation.CanSendRoomInput);
        Assert.IsFalse(navigation.HideRoom());
        navigation.ShowPorch();
        Assert.AreEqual(RoomNavigationView.Porch, navigation.View);
        Assert.AreEqual(Target, navigation.Authority);
        Assert.IsTrue(navigation.CanResumeRoom);
        Assert.IsFalse(navigation.CanSendRoomInput);
    }

    [TestMethod]
    public void NewAuthorityNeverSilentlyReusesTheOldPresentation()
    {
        var navigation = new RoomNavigationCoordinator(Original);
        var attempt = Begin(navigation);
        navigation.ConfirmAdmission(attempt, Target);
        navigation.ConfirmHostReady(attempt);
        Assert.IsTrue(navigation.ApplyAuthority(new AuthorityRoomSnapshot("fictional-room-c", 12)));
        Assert.AreEqual(RoomNavigationView.PresentationRecovery, navigation.View);
        Assert.IsFalse(navigation.CanHideControlCenter);
        Assert.IsFalse(navigation.CanSendRoomInput);
        navigation.ApplyAuthority(new AuthorityRoomSnapshot(null, 13));
        Assert.AreEqual(RoomNavigationView.Home, navigation.View);
        Assert.IsFalse(navigation.CanResumeRoom);
    }

    [TestMethod]
    public void DuplicateMatchingSnapshotDoesNotInvalidateCurrentReadyHandshake()
    {
        var navigation = new RoomNavigationCoordinator(Original);
        var attempt = Begin(navigation);
        navigation.ConfirmAdmission(attempt, Target);
        Assert.IsTrue(navigation.ApplyAuthority(Target));
        Assert.IsTrue(navigation.ConfirmHostReady(attempt));
    }

    [TestMethod]
    public void InvalidAuthorityAndBlankDoorIdentifiersAreRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new RoomNavigationCoordinator(new AuthorityRoomSnapshot(null, -1)));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new RoomNavigationCoordinator(new AuthorityRoomSnapshot(" ", 1)));
        var navigation = new RoomNavigationCoordinator();
        navigation.ShowPorch();
        Assert.ThrowsExactly<ArgumentException>(() => navigation.SelectDoor(" "));
        Assert.IsNull(navigation.SelectedDoorId);
    }

    private static RoomEntryAttempt Begin(RoomNavigationCoordinator navigation)
    {
        navigation.ShowPorch();
        Assert.IsTrue(navigation.SelectDoor(Target.RoomId!));
        return navigation.BeginEntry()!.Value;
    }
}
