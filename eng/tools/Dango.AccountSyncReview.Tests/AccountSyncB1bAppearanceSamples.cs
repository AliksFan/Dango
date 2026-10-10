namespace Dango.AccountSyncReview.Tests;

internal static class AccountSyncB1bAppearanceSamples
{
    internal const double Baseline = 103;
    internal const double CasPrimary = 111;
    internal const double CasSecondary = 114;
    internal const double LostAccepted = 118;
    internal const double Superseded = 125;
    internal const double Newer = 128;
    internal const double Retry = 137;
    internal const double RecoveryIntermediate = 138;
    internal const double RecoveryNewest = 139;

    internal static IReadOnlyList<double> All { get; } = Array.AsReadOnly<double>(
        [Baseline, CasPrimary, CasSecondary, LostAccepted, Superseded, Newer, Retry, RecoveryIntermediate, RecoveryNewest]);
}
