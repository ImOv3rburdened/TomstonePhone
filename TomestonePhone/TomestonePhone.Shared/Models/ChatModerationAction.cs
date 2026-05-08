namespace TomestonePhone.Shared.Models;

public enum ChatModerationAction
{
    AddMember,
    RequestAddMember,
    ApprovePendingMemberRequest,
    DeclinePendingMemberRequest,
    RemoveMember,
    PromoteModerator,
    DemoteModerator,
    TransferOwnership,
    CloseConversation,
    DeleteConversation,
    LeaveConversation,
    HideConversation,
}
