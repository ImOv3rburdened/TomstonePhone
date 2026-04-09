namespace TomestonePhone.Server.Services;

public sealed class GroupConversationPolicyOptions
{
    public int FreeMemberCap { get; set; } = 25;

    public int PaidMemberCap { get; set; } = 100;
}
