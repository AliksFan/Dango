using System.Net;
using System.Text.Json.Serialization;
using MailKit.Security;
using MimeKit;

namespace Dango.Server.Email;

// Created from operator configuration, never from an API payload. No constructor can bypass validation.
public sealed class SmtpOptions
{
    private SmtpOptions(string host, int port, SecureSocketOptions security, string fromAddress,
        string? userName, string? password, int timeoutMilliseconds, int attemptTimeoutMilliseconds, bool isLoopbackTest)
    {
        Host = host;
        Port = port;
        Security = security;
        FromAddress = fromAddress;
        UserName = userName;
        Password = password;
        TimeoutMilliseconds = timeoutMilliseconds;
        AttemptTimeoutMilliseconds = attemptTimeoutMilliseconds;
        IsLoopbackTest = isLoopbackTest;
    }

    [JsonIgnore] public string Host { get; }
    public int Port { get; }
    public SecureSocketOptions Security { get; }
    [JsonIgnore] public string FromAddress { get; }
    [JsonIgnore] public string? UserName { get; }
    [JsonIgnore] public string? Password { get; }
    public int TimeoutMilliseconds { get; }
    public int AttemptTimeoutMilliseconds { get; }
    public bool IsLoopbackTest { get; }

    public override string ToString() => "SmtpOptions [redacted]";

    public static SmtpOptions Create(string host, int port, string sender, string? username, string? password, string tls)
    {
        var security = tls switch
        {
            "StartTls" => SecureSocketOptions.StartTls,
            "SslOnConnect" => SecureSocketOptions.SslOnConnect,
            _ => throw new ArgumentException("smtp_tls_configuration_invalid", nameof(tls))
        };
        if (!TryCreate(host, port, security, sender, username, password, out var options, out var errorCode))
            throw new ArgumentException(errorCode);
        return options!;
    }

    public static bool TryCreate(string? host, int port, SecureSocketOptions security, string? fromAddress,
        string? userName, string? password, out SmtpOptions? options, out string errorCode,
        int timeoutMilliseconds = 15_000, int attemptTimeoutMilliseconds = 30_000)
    {
        options = null;
        errorCode = "smtp_host_invalid";
        if (!ValidHost(host)) return false;
        errorCode = "smtp_port_invalid";
        if (port is < 1 or > 65_535) return false;
        errorCode = "smtp_tls_configuration_invalid";
        if (security is not (SecureSocketOptions.StartTls or SecureSocketOptions.SslOnConnect)) return false;
        errorCode = "smtp_from_invalid";
        if (!TryMailbox(fromAddress, out _)) return false;
        errorCode = "smtp_credentials_invalid";
        if (string.IsNullOrWhiteSpace(userName) || userName.Length > 320 || userName != userName.Trim()
            || userName.Any(char.IsControl) || string.IsNullOrEmpty(password) || password.Length > 1024
            || password.Any(char.IsControl)) return false;
        errorCode = "smtp_timeout_invalid";
        if (!ValidTimeouts(timeoutMilliseconds, attemptTimeoutMilliseconds)) return false;

        options = new SmtpOptions(host!, port, security, fromAddress!, userName, password,
            timeoutMilliseconds, attemptTimeoutMilliseconds, false);
        errorCode = string.Empty;
        return true;
    }

    // Explicit test capability: the caller cannot replace loopback with a host or DNS alias.
    public static SmtpOptions ForLoopbackTest(int port, string fromAddress = "sender@example.invalid",
        int timeoutMilliseconds = 15_000, int attemptTimeoutMilliseconds = 30_000)
    {
        if (port is < 1 or > 65_535) throw new ArgumentException("smtp_port_invalid", nameof(port));
        if (!TryMailbox(fromAddress, out _)) throw new ArgumentException("smtp_from_invalid", nameof(fromAddress));
        if (!ValidTimeouts(timeoutMilliseconds, attemptTimeoutMilliseconds))
            throw new ArgumentException("smtp_timeout_invalid", nameof(timeoutMilliseconds));
        return new SmtpOptions("127.0.0.1", port, SecureSocketOptions.None, fromAddress, null, null,
            timeoutMilliseconds, attemptTimeoutMilliseconds, true);
    }

    internal static bool TryMailbox(string? value, out MailboxAddress? address)
    {
        address = null;
        if (string.IsNullOrEmpty(value) || value.Length > 254 || value != value.Trim() || value.Any(char.IsControl)
            || !MailboxAddress.TryParse(value, out var parsed) || !string.IsNullOrEmpty(parsed.Name)
            || parsed.Address != value || parsed.Address.LastIndexOf('@') is not (> 0)
            || parsed.Address.EndsWith('@')) return false;
        address = parsed;
        return true;
    }

    private static bool ValidTimeouts(int timeout, int attemptTimeout) =>
        timeout is >= 100 and <= 60_000 && attemptTimeout >= timeout && attemptTimeout <= 120_000;

    private static bool ValidHost(string? host)
    {
        if (string.IsNullOrEmpty(host) || host.Length > 253 || host != host.Trim() || host.Any(char.IsControl)) return false;
        if (IPAddress.TryParse(host, out _)) return true;
        foreach (var label in host.Split('.'))
        {
            if (label.Length is < 1 or > 63 || !char.IsAsciiLetterOrDigit(label[0])
                || !char.IsAsciiLetterOrDigit(label[^1])
                || label.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) return false;
        }
        return true;
    }
}
