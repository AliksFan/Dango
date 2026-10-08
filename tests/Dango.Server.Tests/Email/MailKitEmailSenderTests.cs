using System.Net.Sockets;
using Dango.Server.Email;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MimeKit;

namespace Dango.Server.Tests.Email;

[TestClass]
public sealed class MailKitEmailSenderTests
{
    private static readonly EmailDelivery Delivery = new("recipient@example.invalid", "Fictional verification", "Fictional body");

    [TestMethod]
    public async Task ProductionConnectsWithTlsBeforeAuthenticationAndReturnsAccepted()
    {
        var client = new FakeClient();
        var sender = Sender(client);
        var result = await sender.SendAsync(Delivery, CancellationToken.None);
        Assert.AreEqual(EmailSendStatus.Accepted, result.Status);
        Assert.IsNull(result.Code);
        CollectionAssert.AreEqual(new[] { "connect", "authenticate", "send", "disconnect", "dispose" }, client.Calls);
        Assert.AreEqual("smtp.example.invalid", client.Host);
        Assert.AreEqual(587, client.Port);
        Assert.AreEqual(SecureSocketOptions.StartTls, client.Security);
        Assert.AreEqual(15_000, client.Timeout);
        Assert.AreEqual("fictional_user", client.AuthUser);
        Assert.AreEqual("fictional_password", client.AuthPassword);
        Assert.IsNotNull(client.Message);
        Assert.AreEqual("sender@example.invalid", ((MailboxAddress)client.Message.From.Single()).Address);
        Assert.AreEqual(Delivery.Recipient, ((MailboxAddress)client.Message.To.Single()).Address);
        Assert.AreEqual(Delivery.Subject, client.Message.Subject);
        Assert.AreEqual(Delivery.TextBody, client.Message.TextBody);
        Assert.IsTrue(client.SawCancelableToken);
    }

    [TestMethod]
    public async Task ProductionNeverAuthenticatesWhenConnectionIsNotEncrypted()
    {
        var client = new FakeClient { IsSecure = false };
        var result = await Sender(client).SendAsync(Delivery, CancellationToken.None);
        Assert.AreEqual(EmailSendStatus.PermanentFailure, result.Status);
        Assert.AreEqual("smtp_tls_required", result.Code);
        Assert.DoesNotContain("authenticate", client.Calls);
        Assert.DoesNotContain("send", client.Calls);
        Assert.Contains("dispose", client.Calls);
    }

    [TestMethod]
    public async Task LoopbackTestFactoryOmitsAuthenticationAndKeepsDestinationLocal()
    {
        var client = new FakeClient { IsSecure = false };
        var sender = new MailKitEmailSender(SmtpOptions.ForLoopbackTest(2525), () => client);
        var result = await sender.SendAsync(Delivery, CancellationToken.None);
        Assert.AreEqual(EmailSendStatus.Accepted, result.Status);
        Assert.AreEqual("127.0.0.1", client.Host);
        Assert.AreEqual(SecureSocketOptions.None, client.Security);
        Assert.DoesNotContain("authenticate", client.Calls);
    }

    [TestMethod]
    public async Task DisconnectFailureCannotTurnAcceptedMessageIntoRetry()
    {
        foreach (var error in new Exception[] { new IOException("fictional_secret_in_disconnect"),
                     new TimeoutException("fictional_secret_in_disconnect"), new InvalidOperationException("fictional_secret_in_disconnect") })
        {
            var client = new FakeClient { DisconnectError = error };
            var result = await Sender(client).SendAsync(Delivery, CancellationToken.None);
            Assert.AreEqual(EmailSendStatus.Accepted, result.Status);
            Assert.IsNull(result.Code);
            Assert.Contains("dispose", client.Calls);
        }
    }

    [TestMethod]
    public async Task UnexpectedClientFailureHasOneSanitizedPermanentResult()
    {
        var result = await Sender(new FakeClient { ConnectError = new Exception("fictional_secret_in_unexpected_error") })
            .SendAsync(Delivery, CancellationToken.None);
        Assert.AreEqual(EmailSendStatus.PermanentFailure, result.Status);
        Assert.AreEqual("smtp_send_failed", result.Code);
        Assert.DoesNotContain("fictional_secret", result.ToString());
    }

    [TestMethod]
    public async Task ServerRejectionIsClassifiedWithoutPublishingServerText()
    {
        foreach (var transient in new[] { true, false })
        {
            var client = new FakeClient
            {
                SendError = new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted,
                    transient ? SmtpStatusCode.MailboxBusy : SmtpStatusCode.MailboxUnavailable, "fictional_secret_in_response")
            };
            var result = await Sender(client).SendAsync(Delivery, CancellationToken.None);
            Assert.AreEqual(transient ? EmailSendStatus.TransientFailure : EmailSendStatus.PermanentFailure, result.Status);
            Assert.AreEqual(transient ? "smtp_temporary_rejection" : "smtp_rejected", result.Code);
            Assert.DoesNotContain("fictional_secret", result.ToString());
        }
    }

    [TestMethod]
    public async Task AuthenticationAndTlsErrorsAreStablePermanentFailures()
    {
        var authClient = new FakeClient { AuthenticationError = new MailKit.Security.AuthenticationException("fictional_secret") };
        var auth = await Sender(authClient).SendAsync(Delivery, CancellationToken.None);
        Assert.AreEqual(EmailSendStatus.PermanentFailure, auth.Status);
        Assert.AreEqual("smtp_authentication_failed", auth.Code);
        Assert.DoesNotContain("send", authClient.Calls);

        var tlsClient = new FakeClient { ConnectError = new NotSupportedException("fictional_secret") };
        var tls = await Sender(tlsClient).SendAsync(Delivery, CancellationToken.None);
        Assert.AreEqual(EmailSendStatus.PermanentFailure, tls.Status);
        Assert.AreEqual("smtp_tls_required", tls.Code);
        Assert.DoesNotContain("authenticate", tlsClient.Calls);
        Assert.DoesNotContain("fictional_secret", tls.ToString());
    }

    [TestMethod]
    public async Task SocketAndProtocolFailuresAreTransientAndSecretFree()
    {
        foreach (var error in new Exception[] { new SocketException((int)SocketError.ConnectionRefused),
                     new IOException("fictional_secret"), new SmtpProtocolException("fictional_secret") })
        {
            var result = await Sender(new FakeClient { ConnectError = error }).SendAsync(Delivery, CancellationToken.None);
            Assert.AreEqual(EmailSendStatus.TransientFailure, result.Status);
            Assert.DoesNotContain("fictional_secret", result.ToString());
        }
    }

    [TestMethod]
    public async Task WholeAttemptDeadlineCancelsAsyncWorkWithoutNetwork()
    {
        var client = new FakeClient { WaitForConnectCancellation = true };
        var options = SmtpOptions.ForLoopbackTest(2525, timeoutMilliseconds: 100, attemptTimeoutMilliseconds: 100);
        var result = await new MailKitEmailSender(options, () => client).SendAsync(Delivery, CancellationToken.None);
        Assert.AreEqual(EmailSendStatus.TransientFailure, result.Status);
        Assert.AreEqual("smtp_timeout", result.Code);
        Assert.Contains("dispose", client.Calls);
    }

    [TestMethod]
    public async Task CallerCancellationIsPropagatedAndDoesNotBecomeDeliveryFailure()
    {
        var client = new FakeClient { WaitForConnectCancellation = true };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Sender(client).SendAsync(Delivery, cancellation.Token));
        Assert.AreEqual(0, client.Calls.Count);
    }

    [TestMethod]
    public async Task InFlightCallerCancellationDisposesClientAndPropagatesCancellation()
    {
        var client = new FakeClient { WaitForConnectCancellation = true };
        using var cancellation = new CancellationTokenSource(50);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Sender(client).SendAsync(Delivery, cancellation.Token));
        CollectionAssert.AreEqual(new[] { "connect", "dispose" }, client.Calls);
    }

    [TestMethod]
    public async Task InvalidDeliveryIsRejectedBeforeClientCreation()
    {
        var created = 0;
        var sender = new MailKitEmailSender(ProductionOptions(), () => { created++; return new FakeClient(); });
        foreach (var delivery in new[]
                 {
                     Delivery with { Recipient = "victim@example.invalid,other@example.invalid" },
                     Delivery with { Recipient = "victim@example.invalid\r\nBcc:other@example.invalid" },
                     Delivery with { Subject = "test\r\nBcc:other@example.invalid" },
                     Delivery with { Subject = new string('s', 201) },
                     Delivery with { TextBody = new string('b', 65_537) },
                     Delivery with { TextBody = new string('团', 21_846) },
                     Delivery with { TextBody = "\ud800" }
                 })
        {
            var result = await sender.SendAsync(delivery, CancellationToken.None);
            Assert.AreEqual(EmailSendStatus.PermanentFailure, result.Status);
            Assert.AreEqual("smtp_delivery_invalid", result.Code);
        }
        Assert.AreEqual(0, created);
    }

    private static MailKitEmailSender Sender(FakeClient client) => new(ProductionOptions(), () => client);

    private static SmtpOptions ProductionOptions()
    {
        Assert.IsTrue(SmtpOptions.TryCreate("smtp.example.invalid", 587, SecureSocketOptions.StartTls,
            "sender@example.invalid", "fictional_user", "fictional_password", out var options, out _));
        return options!;
    }

    private sealed class FakeClient : ISmtpEmailClient
    {
        public List<string> Calls { get; } = [];
        public bool IsSecure { get; set; } = true;
        public bool IsConnected { get; private set; }
        public int Timeout { get; set; }
        public string? Host { get; private set; }
        public int Port { get; private set; }
        public SecureSocketOptions Security { get; private set; }
        public string? AuthUser { get; private set; }
        public string? AuthPassword { get; private set; }
        public MimeMessage? Message { get; private set; }
        public bool SawCancelableToken { get; private set; }
        public bool WaitForConnectCancellation { get; init; }
        public Exception? ConnectError { get; init; }
        public Exception? AuthenticationError { get; init; }
        public Exception? SendError { get; init; }
        public Exception? DisconnectError { get; init; }

        public async Task ConnectAsync(string host, int port, SecureSocketOptions security, CancellationToken ct)
        {
            Calls.Add("connect");
            Host = host;
            Port = port;
            Security = security;
            SawCancelableToken |= ct.CanBeCanceled;
            if (ConnectError is not null) throw ConnectError;
            if (WaitForConnectCancellation) await Task.Delay(System.Threading.Timeout.Infinite, ct);
            IsConnected = true;
        }

        public Task AuthenticateAsync(string userName, string password, CancellationToken ct)
        {
            Calls.Add("authenticate");
            AuthUser = userName;
            AuthPassword = password;
            SawCancelableToken &= ct.CanBeCanceled;
            if (AuthenticationError is not null) throw AuthenticationError;
            return Task.CompletedTask;
        }

        public Task SendAsync(MimeMessage message, CancellationToken ct)
        {
            Calls.Add("send");
            Message = message;
            SawCancelableToken &= ct.CanBeCanceled;
            if (SendError is not null) throw SendError;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(bool quit, CancellationToken ct)
        {
            Calls.Add("disconnect");
            if (DisconnectError is not null) throw DisconnectError;
            IsConnected = false;
            return Task.CompletedTask;
        }

        public void Dispose() => Calls.Add("dispose");
    }
}
