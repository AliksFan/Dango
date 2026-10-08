namespace Dango.Server.Email;

public sealed record EmailDelivery(string Recipient, string Subject, string TextBody)
{
    public override string ToString() => "EmailDelivery [redacted]";
}
public enum EmailSendStatus { Accepted, TransientFailure, PermanentFailure, NotConfigured }
public sealed record EmailSendResult(EmailSendStatus Status, string? Code = null);

public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(EmailDelivery delivery, CancellationToken ct);
}

public sealed class UnconfiguredEmailSender : IEmailSender
{
    public Task<EmailSendResult> SendAsync(EmailDelivery delivery, CancellationToken ct) =>
        Task.FromResult(new EmailSendResult(EmailSendStatus.NotConfigured, "smtp_not_configured"));
}
