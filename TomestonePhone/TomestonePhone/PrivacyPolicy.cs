namespace TomestonePhone;

public static class PrivacyPolicy
{
    public const string Version = "2026-05-08";

    public const string Summary =
        "TomestonePhone sends the minimum data needed to operate its phone, chat, friends, contacts, calls, support, moderation, and account features through the configured HTTPS backend. The plugin does not collect analytics or telemetry.";

    public const string FullText =
        """
        TomestonePhone Privacy Policy

        1. TomestonePhone connects to the configured backend server using HTTPS and a DNS hostname. If you use a custom server, your TomestonePhone data goes to that server instead of the maintainer-run server.
        2. We collect your TomestonePhone username, password hash, assigned phone number, account role, status, notification settings, and related account metadata to operate the service.
        3. If you enable character/world sharing in Settings, the plugin may send your current character name and world to support user-facing display names in chats, calls, contacts, and friends lists. It does not send your Content ID, and accounts are not discoverable by character name.
        4. We store messages, chat participation, contacts, friend requests, call records, support tickets, moderation reports, audit logs, and related records to provide features, investigate abuse, and preserve operational and legal records.
        5. Voice calls relay live audio packets through the backend while you are in an active call. TomestonePhone does not intentionally record or store call audio.
        6. We collect and retain IP addresses and related access data for account security, abuse prevention, moderation, bans, unlawful-content investigations, and legal compliance.
        7. The plugin does not collect analytics or telemetry. If analytics are added later, they will require explicit opt-in before collection.
        8. The service does not host user-uploaded chat images. If you share external links, related message and moderation records may still be reviewed and retained.
        9. We may preserve records, including logs and moderation evidence, even if user-facing access is restricted or content visibility changes.
        10. We may disclose relevant information when required for safety response, legal process, or reporting obligations.
        11. You may request support, but deletion requests may be limited where retention is necessary for security, legal obligations, or evidentiary purposes.
        12. Continued use of the service constitutes ongoing acknowledgement of these practices.
        """;
}
