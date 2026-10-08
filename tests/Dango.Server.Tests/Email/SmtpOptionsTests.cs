using System.Text.Json;
using Dango.Server.Email;
using MailKit.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.Email;

[TestClass]
public sealed class SmtpOptionsTests
{
    private const string UserName = "fictional_smtp_user_marker";
    private const string Password = "fictional_smtp_secret_marker";

    [TestMethod]
    [DataRow(SecureSocketOptions.StartTls)]
    [DataRow(SecureSocketOptions.SslOnConnect)]
    public void ProductionRequiresExplicitTlsAndRedactsCredentials(SecureSocketOptions security)
    {
        Assert.IsTrue(SmtpOptions.TryCreate("smtp.example.invalid", 587, security, "sender@example.invalid",
            UserName, Password, out var options, out var error));
        Assert.IsNotNull(options);
        Assert.AreEqual(string.Empty, error);
        Assert.AreEqual(security, options.Security);
        Assert.AreEqual(15_000, options.TimeoutMilliseconds);
        Assert.AreEqual(30_000, options.AttemptTimeoutMilliseconds);
        Assert.IsFalse(options.IsLoopbackTest);
        Assert.AreEqual("SmtpOptions [redacted]", options.ToString());
        Assert.DoesNotContain(UserName, JsonSerializer.Serialize(options));
        Assert.DoesNotContain(Password, JsonSerializer.Serialize(options));
    }

    [TestMethod]
    [DataRow(SecureSocketOptions.Auto)]
    [DataRow(SecureSocketOptions.None)]
    [DataRow(SecureSocketOptions.StartTlsWhenAvailable)]
    [DataRow((SecureSocketOptions)99)]
    public void ProductionRejectsPlaintextOpportunisticOrUnknownTls(SecureSocketOptions security) =>
        Reject("smtp.example.invalid", 587, security, "sender@example.invalid", UserName, Password,
            "smtp_tls_configuration_invalid");

    [TestMethod]
    [DataRow(0)]
    [DataRow(65536)]
    public void PortMustBeExplicitAndBounded(int port) =>
        Reject("smtp.example.invalid", port, SecureSocketOptions.StartTls, "sender@example.invalid", UserName, Password,
            "smtp_port_invalid");

    [TestMethod]
    [DataRow("")]
    [DataRow(" smtp.example.invalid")]
    [DataRow("smtp://example.invalid")]
    [DataRow("example.invalid/path")]
    [DataRow("example.invalid\r\nfictional_smtp_secret_marker")]
    [DataRow("-example.invalid")]
    [DataRow("example..invalid")]
    public void HostCannotContainUrlsWhitespaceOrMalformedDns(string host) =>
        Reject(host, 587, SecureSocketOptions.StartTls, "sender@example.invalid", UserName, Password, "smtp_host_invalid");

    [TestMethod]
    public void HostLabelsAndCredentialSizesAreBounded()
    {
        Reject(new string('a', 64) + ".invalid", 587, SecureSocketOptions.StartTls, "sender@example.invalid",
            UserName, Password, "smtp_host_invalid");
        Reject("smtp.example.invalid", 587, SecureSocketOptions.StartTls, "sender@example.invalid",
            new string('u', 321), Password, "smtp_credentials_invalid");
        Reject("smtp.example.invalid", 587, SecureSocketOptions.StartTls, "sender@example.invalid",
            UserName, new string('p', 1025), "smtp_credentials_invalid");
        Reject("smtp.example.invalid", 587, SecureSocketOptions.StartTls, "sender@example.invalid",
            UserName, "", "smtp_credentials_invalid");
        Reject("smtp.example.invalid", 587, SecureSocketOptions.StartTls, "sender@example.invalid",
            "user\r\n" + Password, Password, "smtp_credentials_invalid");
    }

    [TestMethod]
    [DataRow("sender")]
    [DataRow("Display Name <sender@example.invalid>")]
    [DataRow("sender@example.invalid,other@example.invalid")]
    [DataRow("sender@example.invalid\r\nBcc:other@example.invalid")]
    public void FromIsOnePlainMailbox(string from) =>
        Reject("smtp.example.invalid", 587, SecureSocketOptions.StartTls, from, UserName, Password, "smtp_from_invalid");

    [TestMethod]
    public void TimeoutRangesRejectUnboundedOrContradictoryBudgets()
    {
        Assert.IsFalse(SmtpOptions.TryCreate("smtp.example.invalid", 587, SecureSocketOptions.StartTls,
            "sender@example.invalid", UserName, Password, out _, out var error, timeoutMilliseconds: 0));
        Assert.AreEqual("smtp_timeout_invalid", error);
        Assert.IsFalse(SmtpOptions.TryCreate("smtp.example.invalid", 587, SecureSocketOptions.StartTls,
            "sender@example.invalid", UserName, Password, out _, out error,
            timeoutMilliseconds: 60_001, attemptTimeoutMilliseconds: 120_000));
        Assert.AreEqual("smtp_timeout_invalid", error);
        Assert.IsFalse(SmtpOptions.TryCreate("smtp.example.invalid", 587, SecureSocketOptions.StartTls,
            "sender@example.invalid", UserName, Password, out _, out error,
            timeoutMilliseconds: 15_000, attemptTimeoutMilliseconds: 14_999));
        Assert.AreEqual("smtp_timeout_invalid", error);
        Assert.IsFalse(SmtpOptions.TryCreate("smtp.example.invalid", 587, SecureSocketOptions.StartTls,
            "sender@example.invalid", UserName, Password, out _, out error, attemptTimeoutMilliseconds: 120_001));
        Assert.AreEqual("smtp_timeout_invalid", error);
    }

    [TestMethod]
    public void ExplicitLoopbackFactoryAloneAllowsPlaintextWithoutCredentials()
    {
        var options = SmtpOptions.ForLoopbackTest(2525);
        Assert.AreEqual("127.0.0.1", options.Host);
        Assert.AreEqual(SecureSocketOptions.None, options.Security);
        Assert.IsTrue(options.IsLoopbackTest);
        Assert.IsNull(options.UserName);
        Assert.IsNull(options.Password);
        Assert.ThrowsExactly<ArgumentException>(() => SmtpOptions.ForLoopbackTest(0));
    }

    [TestMethod]
    public void HostConfigurationFactoryIsStrictAndDoesNotPutValuesInExceptions()
    {
        var options = SmtpOptions.Create("smtp.example.invalid", 465, "sender@example.invalid", UserName, Password, "SslOnConnect");
        Assert.AreEqual(SecureSocketOptions.SslOnConnect, options.Security);
        var tlsError = Assert.ThrowsExactly<ArgumentException>(() =>
            SmtpOptions.Create("smtp.example.invalid", 587, "sender@example.invalid", UserName, Password, Password));
        Assert.Contains("smtp_tls_configuration_invalid", tlsError.Message);
        Assert.DoesNotContain(Password, tlsError.Message);
        var credentialError = Assert.ThrowsExactly<ArgumentException>(() =>
            SmtpOptions.Create("smtp.example.invalid", 587, "sender@example.invalid", UserName, "", "StartTls"));
        Assert.AreEqual("smtp_credentials_invalid", credentialError.Message);
    }

    private static void Reject(string host, int port, SecureSocketOptions security, string from,
        string userName, string password, string expectedCode)
    {
        Assert.IsFalse(SmtpOptions.TryCreate(host, port, security, from, userName, password, out var options, out var error));
        Assert.IsNull(options);
        Assert.AreEqual(expectedCode, error);
        Assert.DoesNotContain(UserName, error);
        Assert.DoesNotContain(Password, error);
    }
}
