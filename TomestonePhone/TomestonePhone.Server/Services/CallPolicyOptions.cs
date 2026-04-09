namespace TomestonePhone.Server.Services;

public sealed class CallPolicyOptions
{
    public int? DirectCallMaxMinutes { get; set; }

    public int? GroupCallMaxMinutes { get; set; }
}
