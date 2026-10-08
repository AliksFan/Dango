using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

[assembly: InternalsVisibleTo("Dango.Server.Tests")]

namespace Dango.Server.Email;

public sealed class MailKitEmailSender : IEmailSender
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly SmtpOptions options;
    private readonly Func<ISmtpEmailClient> createClient;

    public MailKitEmailSender(SmtpOptions options) : this(options, () => new MailKitSmtpEmailClient()) { }

    internal MailKitEmailSender(SmtpOptions options, Func<ISmtpEmailClient> createClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(createClient);
        this.options = options;
        this.createClient = createClient;
    }

    public async Task<EmailSendResult> SendAsync(EmailDelivery delivery, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!TryMessage(delivery, out var message)) return new(EmailSendStatus.PermanentFailure, "smtp_delivery_invalid");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(options.AttemptTimeoutMilliseconds);
        ISmtpEmailClient? client = null;
        var accepted = false;
        var connecting = true;
        try
        {
            client = createClient();
            client.Timeout = options.TimeoutMilliseconds;
            await client.ConnectAsync(options.Host, options.Port, options.Security, deadline.Token);
            if (!options.IsLoopbackTest && !client.IsSecure)
                return new(EmailSendStatus.PermanentFailure, "smtp_tls_required");
            connecting = false;
            if (!options.IsLoopbackTest)
                await client.AuthenticateAsync(options.UserName!, options.Password!, deadline.Token);
            await client.SendAsync(message!, deadline.Token);
            // SMTP acceptance is established by SendAsync, independently of QUIT or inbox delivery.
            accepted = true;
            return new(EmailSendStatus.Accepted);
        }
        catch (OperationCanceledException)
        {
            ct.ThrowIfCancellationRequested();
            return new(EmailSendStatus.TransientFailure, "smtp_timeout");
        }
        catch (SslHandshakeException)
        { return new(EmailSendStatus.PermanentFailure, "smtp_tls_failed"); }
        catch (MailKit.Security.AuthenticationException)
        { return new(EmailSendStatus.PermanentFailure, "smtp_authentication_failed"); }
        catch (SmtpCommandException ex)
        {
            return (int)ex.StatusCode is >= 400 and <= 499
                ? new(EmailSendStatus.TransientFailure, "smtp_temporary_rejection")
                : new(EmailSendStatus.PermanentFailure, "smtp_rejected");
        }
        catch (SmtpProtocolException)
        { return new(EmailSendStatus.TransientFailure, "smtp_protocol_error"); }
        catch (Exception ex) when (ex is IOException or SocketException or TimeoutException)
        { return new(EmailSendStatus.TransientFailure, "smtp_unavailable"); }
        catch (NotSupportedException)
        { return new(EmailSendStatus.PermanentFailure, connecting ? "smtp_tls_required" : "smtp_feature_unsupported"); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { return new(EmailSendStatus.PermanentFailure, "smtp_send_failed"); }
        finally
        {
            if (client is not null)
            {
                // A separate bounded cleanup budget still permits connection disposal when the attempt expired.
                using var cleanup = new CancellationTokenSource(Math.Min(3_000, options.AttemptTimeoutMilliseconds));
                try
                {
                    if (client.IsConnected) await client.DisconnectAsync(accepted, cleanup.Token);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException) { }
                finally
                {
                    try { client.Dispose(); }
                    catch (Exception ex) when (ex is not OutOfMemoryException) { }
                }
            }
        }
    }

    private bool TryMessage(EmailDelivery? delivery, out MimeMessage? message)
    {
        message = null;
        if (delivery is null || !SmtpOptions.TryMailbox(delivery.Recipient, out var recipient)
            || string.IsNullOrEmpty(delivery.Subject) || delivery.Subject.Length > 200 || delivery.Subject.Any(char.IsControl)
            || delivery.TextBody is null || delivery.TextBody.Length > 65_536 || delivery.TextBody.Contains('\0')) return false;
        try
        {
            if (StrictUtf8.GetByteCount(delivery.TextBody) > 65_536) return false;
        }
        catch (EncoderFallbackException) { return false; }
        message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(options.FromAddress));
        message.To.Add(recipient!);
        message.Subject = delivery.Subject;
        message.Body = new TextPart("plain") { Text = delivery.TextBody };
        return true;
    }
}

internal interface ISmtpEmailClient : IDisposable
{
    int Timeout { get; set; }
    bool IsSecure { get; }
    bool IsConnected { get; }
    Task ConnectAsync(string host, int port, SecureSocketOptions security, CancellationToken ct);
    Task AuthenticateAsync(string userName, string password, CancellationToken ct);
    Task SendAsync(MimeMessage message, CancellationToken ct);
    Task DisconnectAsync(bool quit, CancellationToken ct);
}

internal sealed class MailKitSmtpEmailClient : ISmtpEmailClient
{
    // Parameterless construction uses NullProtocolLogger and the framework certificate validator.
    private readonly SmtpClient client = new();
    public int Timeout { get => client.Timeout; set => client.Timeout = value; }
    public bool IsSecure => client.IsSecure;
    public bool IsConnected => client.IsConnected;
    public Task ConnectAsync(string host, int port, SecureSocketOptions security, CancellationToken ct) =>
        client.ConnectAsync(host, port, security, ct);
    public Task AuthenticateAsync(string userName, string password, CancellationToken ct) =>
        client.AuthenticateAsync(userName, password, ct);
    public async Task SendAsync(MimeMessage message, CancellationToken ct) => await client.SendAsync(message, ct);
    public Task DisconnectAsync(bool quit, CancellationToken ct) => client.DisconnectAsync(quit, ct);
    public void Dispose() => client.Dispose();
}
