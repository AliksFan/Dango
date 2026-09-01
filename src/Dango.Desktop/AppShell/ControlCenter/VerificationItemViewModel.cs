using System.ComponentModel;
using System.Runtime.CompilerServices;
using Dango.Desktop.Verification;

namespace Dango.Desktop.AppShell.ControlCenter;

public sealed record VerificationOutcomeOption(
    ManualVerificationOutcome Outcome,
    string DisplayName);

public sealed class VerificationItemViewModel : INotifyPropertyChanged
{
    private static readonly IReadOnlyList<VerificationOutcomeOption> Options =
    [
        new(ManualVerificationOutcome.Unverified, "未验证"),
        new(ManualVerificationOutcome.Passed, "人工通过"),
        new(ManualVerificationOutcome.Failed, "人工失败"),
        new(ManualVerificationOutcome.UnableToRun, "无法执行"),
    ];

    private readonly VerificationItemResult result;

    public VerificationItemViewModel(
        VerificationItemDefinition definition,
        VerificationItemResult result)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        this.result = result ?? throw new ArgumentNullException(nameof(result));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public VerificationItemDefinition Definition { get; }

    public IReadOnlyList<VerificationOutcomeOption> OutcomeOptions => Options;

    public ManualVerificationOutcome Outcome
    {
        get => result.Outcome;
        set
        {
            if (result.Outcome == value)
            {
                return;
            }

            result.Outcome = value;
            OnPropertyChanged();
        }
    }

    public string Notes
    {
        get => result.Notes;
        set
        {
            value ??= string.Empty;
            if (string.Equals(result.Notes, value, StringComparison.Ordinal))
            {
                return;
            }

            result.Notes = value;
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
