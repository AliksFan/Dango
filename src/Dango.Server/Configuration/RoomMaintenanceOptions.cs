namespace Dango.Server.Configuration;

public static class RoomMaintenanceOptions
{
    public static bool LiveEnabled(string? value) => value switch
    {
        null or "" or "false" => false,
        "true" => true,
        _ => throw new Accounts.AccountFault("room_live_configuration_invalid")
    };
    // Permanent cleanup must be explicitly enabled by the operator of the configured database.
    public static bool ExpiryEnabled(string? value) => value switch
    {
        null or "" or "false" => false,
        "true" => true,
        _ => throw new Accounts.AccountFault("room_expiry_configuration_invalid")
    };
}
