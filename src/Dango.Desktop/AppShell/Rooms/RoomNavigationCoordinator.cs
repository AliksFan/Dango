namespace Dango.Desktop.AppShell.Rooms;

public enum RoomNavigationView
{
    Home,
    Porch,
    Entering,
    RoomVisible,
    RoomHidden,
    PresentationRecovery
}

/// <summary>
/// Opaque confirmed authority supplied by a future service adapter, not a locally
/// selected door. Null is a legal room assignment. This is not a protocol DTO.
/// </summary>
public sealed record AuthorityRoomSnapshot(string? RoomId, long Version);

public readonly record struct RoomEntryAttempt(long Generation, string RoomId, long SourceAuthorityVersion);

/// <summary>
/// Local presentation contracts only: no permission decisions, connections or
/// persistence. E1 uses only home/porch/selection. Entry callbacks are unconnected
/// seams for failure-capable tests, not simulated admission in the product.
/// </summary>
public sealed class RoomNavigationCoordinator
{
    private long generation;
    private RoomEntryAttempt? pending;
    private AuthorityRoomSnapshot? confirmedAdmission;
    private AuthorityRoomSnapshot? presentedAuthority;

    public RoomNavigationCoordinator(AuthorityRoomSnapshot? initialAuthority = null)
    {
        Authority = initialAuthority ?? new AuthorityRoomSnapshot(null, 0);
        Validate(Authority);
    }

    public RoomNavigationView View { get; private set; } = RoomNavigationView.Home;
    public AuthorityRoomSnapshot Authority { get; private set; }
    public string? SelectedDoorId { get; private set; }
    public bool CanResumeRoom => Authority.RoomId is not null;
    public bool CanSendRoomInput => View == RoomNavigationView.RoomVisible && presentedAuthority == Authority;
    public bool CanHideControlCenter => CanSendRoomInput;

    public void ShowHome()
    {
        CancelAttempt();
        SelectedDoorId = null;
        presentedAuthority = null;
        View = RoomNavigationView.Home;
    }

    public void ShowPorch()
    {
        CancelAttempt();
        presentedAuthority = null;
        View = RoomNavigationView.Porch;
    }

    public bool SelectDoor(string roomId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roomId);
        if (View != RoomNavigationView.Porch) return false;
        SelectedDoorId = roomId;
        return true;
    }

    public RoomEntryAttempt? BeginEntry()
    {
        if (View != RoomNavigationView.Porch || SelectedDoorId is null || pending is not null) return null;
        pending = new RoomEntryAttempt(++generation, SelectedDoorId, Authority.Version);
        confirmedAdmission = null;
        View = RoomNavigationView.Entering;
        return pending;
    }

    /// <summary>
    /// Called only with externally confirmed admission. A selected door, a host
    /// being available or an unknown result must never be passed as confirmation.
    /// </summary>
    public bool ConfirmAdmission(RoomEntryAttempt attempt, AuthorityRoomSnapshot authority)
    {
        Validate(authority);
        if (!IsPending(attempt) || confirmedAdmission is not null ||
            authority.RoomId != attempt.RoomId || !CanAccept(authority)) return false;
        Authority = authority;
        confirmedAdmission = authority;
        return true;
    }

    public bool ConfirmHostReady(RoomEntryAttempt attempt)
    {
        if (!IsPending(attempt) || confirmedAdmission is null || confirmedAdmission != Authority) return false;
        presentedAuthority = Authority;
        CancelAttempt();
        View = RoomNavigationView.RoomVisible;
        return true;
    }

    public bool RejectAdmission(RoomEntryAttempt attempt)
    {
        if (!IsPending(attempt) || confirmedAdmission is not null) return false;
        CancelAttempt();
        View = RoomNavigationView.Porch;
        return true;
    }

    public bool FailPresentation(RoomEntryAttempt attempt)
    {
        if (!IsPending(attempt)) return false;
        var wasCommitted = confirmedAdmission is not null;
        CancelAttempt();
        presentedAuthority = null;
        View = wasCommitted ? RoomNavigationView.PresentationRecovery : RoomNavigationView.Porch;
        return true;
    }

    public bool HideRoom()
    {
        if (View != RoomNavigationView.RoomVisible) return false;
        View = RoomNavigationView.RoomHidden;
        return true;
    }

    /// <summary>
    /// New confirmed authority invalidates in-flight callbacks and old room
    /// presentation. It never restores the room from a superseded local request.
    /// </summary>
    public bool ApplyAuthority(AuthorityRoomSnapshot authority)
    {
        Validate(authority);
        if (!CanAccept(authority)) return false;
        if (authority == Authority) return true;

        Authority = authority;
        CancelAttempt();
        presentedAuthority = null;
        if (Authority.RoomId is null) View = RoomNavigationView.Home;
        else if (View == RoomNavigationView.Entering) View = RoomNavigationView.Porch;
        else if (View is RoomNavigationView.RoomVisible or RoomNavigationView.RoomHidden or RoomNavigationView.PresentationRecovery)
            View = RoomNavigationView.PresentationRecovery;
        return true;
    }

    private bool IsPending(RoomEntryAttempt attempt) => View == RoomNavigationView.Entering && pending == attempt;

    private bool CanAccept(AuthorityRoomSnapshot authority) =>
        authority.Version > Authority.Version || authority == Authority;

    private void CancelAttempt()
    {
        generation++;
        pending = null;
        confirmedAdmission = null;
    }

    private static void Validate(AuthorityRoomSnapshot authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (authority.Version < 0) throw new ArgumentOutOfRangeException(nameof(authority));
        if (authority.RoomId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(authority.RoomId);
    }
}
