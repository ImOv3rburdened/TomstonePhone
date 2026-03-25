using TomestonePhone.UI;

namespace TomestonePhone;

public sealed record PhoneNotification(
    Guid Id,
    string Title,
    string Body,
    PhoneTab Tab,
    Guid? TargetId,
    bool IsIncomingCall);
