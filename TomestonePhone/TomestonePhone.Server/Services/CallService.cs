using Microsoft.Extensions.Options;
using TomestonePhone.Server.Models;
using TomestonePhone.Shared.Models;

namespace TomestonePhone.Server.Services;

public sealed class CallService : ICallService
{
    private readonly IPhoneRepository repository;
    private readonly VoiceOptions voiceOptions;
    private readonly CallPolicyOptions callPolicy;

    public CallService(IPhoneRepository repository, IOptions<VoiceOptions> voiceOptions, IOptions<CallPolicyOptions> callPolicy)
    {
        this.repository = repository;
        this.voiceOptions = voiceOptions.Value;
        this.callPolicy = callPolicy.Value;
    }

    public Task<IReadOnlyList<CallSummary>> GetRecentCallsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return this.repository.ReadAsync<IReadOnlyList<CallSummary>>(state =>
        {
            return state.Calls
                .Where(call => this.CanAccessConversation(state, accountId, call.ConversationId))
                .OrderByDescending(item => item.StartedUtc)
                .Select(call => this.MapSummary(call, accountId))
                .ToList();
        }, cancellationToken);
    }

    public Task<IReadOnlyList<ActiveCallSessionRecord>> GetActiveCallsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync<IReadOnlyList<ActiveCallSessionRecord>>(state =>
        {
            this.ExpireActiveSessions(state);
            return state.ActiveCallSessions
                .Where(session => this.CanAccessConversation(state, accountId, session.ConversationId))
                .Select(session => this.MapActiveSession(state, session, accountId))
                .OrderByDescending(item => item.StartedUtc)
                .ToList();
        }, cancellationToken);
    }

    public Task<CallSummary> StartCallAsync(Guid accountId, StartCallRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync(state =>
        {
            this.ExpireActiveSessions(state);
            var (_, call, _) = this.EnsureActiveSession(state, accountId, request);
            return this.MapSummary(call, accountId);
        }, cancellationToken);
    }

    public Task<ActiveCallSessionRecord?> StartOrJoinActiveCallAsync(Guid accountId, StartCallRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync<ActiveCallSessionRecord?>(state =>
        {
            this.ExpireActiveSessions(state);
            var (session, _, conversation) = this.EnsureActiveSession(state, accountId, request);
            return this.MapActiveSession(state, session, accountId, conversation);
        }, cancellationToken);
    }

    public Task<CallSummary?> EndActiveCallAsync(Guid accountId, EndActiveCallRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync<CallSummary?>(state =>
        {
            var session = state.ActiveCallSessions.SingleOrDefault(item => item.Id == request.SessionId);
            if (session is null || !this.CanAccessConversation(state, accountId, session.ConversationId))
            {
                return null;
            }

            var conversation = state.Conversations.Single(item => item.Id == session.ConversationId && !item.IsDeleted);
            var call = state.Calls.SingleOrDefault(item => item.Id == session.CallId);
            var removed = session.ParticipantAccountIds.Remove(accountId);

            if (session.IsGroup)
            {
                if (removed)
                {
                    var actor = state.Accounts.SingleOrDefault(item => item.Id == accountId);
                    this.AddSystemCallMessage(state, conversation, accountId, ChatMessageKind.CallLeft, $"{actor?.DisplayName ?? "Someone"} left the call", session.CallId, null);
                }

                if (session.ParticipantAccountIds.Count > 0)
                {
                    if (call is not null)
                    {
                        call.DurationSeconds = Math.Max(call.DurationSeconds, (int)Math.Max(0, Math.Round((DateTimeOffset.UtcNow - session.StartedUtc).TotalSeconds)));
                    }
                    return call is null ? null : this.MapSummary(call, accountId);
                }
            }

            return this.FinalizeSession(state, session, conversation, call, accountId);
        }, cancellationToken);
    }

    public Task<CallSummary?> CompleteCallAsync(Guid accountId, CompleteCallRequest request, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync<CallSummary?>(state =>
        {
            var call = state.Calls.SingleOrDefault(item => item.Id == request.CallId);
            if (call is null || !this.CanAccessConversation(state, accountId, call.ConversationId))
            {
                return null;
            }

            call.DurationSeconds = request.DurationSeconds;
            call.EndedUtc ??= call.StartedUtc.AddSeconds(Math.Max(0, request.DurationSeconds));
            call.Missed = request.Missed;
            return this.MapSummary(call, accountId);
        }, cancellationToken);
    }

    public Task<int> AcknowledgeMissedCallsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return this.repository.WriteAsync<int>(state =>
        {
            var count = 0;
            foreach (var call in state.Calls.Where(call => !call.IsGroup && call.Missed && !call.MissedAcknowledged && call.StartedByAccountId != accountId && this.CanAccessConversation(state, accountId, call.ConversationId)))
            {
                call.MissedAcknowledged = true;
                count++;
            }

            return count;
        }, cancellationToken);
    }

    private (PersistedActiveCallSession Session, PersistedCall Call, PersistedConversation Conversation) EnsureActiveSession(PersistedAppState state, Guid accountId, StartCallRequest request)
    {
        var conversation = state.Conversations.Single(item => item.Id == request.ConversationId && !item.IsDeleted);
        if (conversation.Members.All(item => item.AccountId != accountId))
        {
            throw new InvalidOperationException("Not authorized for this conversation.");
        }

        var caller = state.Accounts.Single(item => item.Id == accountId);
        var otherAccounts = conversation.Members
            .Select(item => state.Accounts.SingleOrDefault(account => account.Id == item.AccountId))
            .OfType<PersistedAccount>()
            .Where(account => account.Id != accountId)
            .ToList();

        var hasBlock = otherAccounts.Any(account => account.BlockedAccountIds.Contains(accountId) || caller.BlockedAccountIds.Contains(account.Id));
        if (hasBlock && !conversation.IsGroup)
        {
            throw new InvalidOperationException("Call unavailable.");
        }

        if (!request.IsGroup && !conversation.IsGroup && otherAccounts.Any(account => string.Equals(account.PresenceStatus, nameof(PhonePresenceStatus.DoNotDisturb), StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("That person is in Do Not Disturb.");
        }

        var existingSession = state.ActiveCallSessions.SingleOrDefault(item => item.ConversationId == conversation.Id);
        if (existingSession is not null)
        {
            if (!existingSession.ParticipantAccountIds.Contains(accountId))
            {
                existingSession.ParticipantAccountIds.Add(accountId);
                if (existingSession.IsGroup)
                {
                    this.AddSystemCallMessage(state, conversation, accountId, ChatMessageKind.CallJoined, $"{caller.DisplayName} joined the call", existingSession.CallId, null);
                }
            }

            var existingCall = state.Calls.Single(item => item.Id == existingSession.CallId);
            existingCall.Missed = false;
            return (existingSession, existingCall, conversation);
        }

        var voiceSession = this.CanProvideVoiceSession()
            ? this.CreateVoiceSessionInfo(conversation, request.IsGroup || conversation.IsGroup)
            : null;
        var startedUtc = DateTimeOffset.UtcNow;
        var call = new PersistedCall
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            DisplayName = conversation.Name,
            IsGroup = request.IsGroup || conversation.IsGroup,
            StartedByAccountId = accountId,
            StartedUtc = startedUtc,
            EndedUtc = null,
            DurationSeconds = 0,
            Missed = false,
            MissedAcknowledged = false,
            VoiceProvider = voiceSession?.Provider ?? string.Empty,
            VoiceHost = voiceSession?.Host ?? string.Empty,
            VoiceTcpPort = voiceSession?.TcpPort ?? 0,
            VoiceUdpPort = voiceSession?.UdpPort ?? 0,
            VoiceChannelName = voiceSession?.ChannelName ?? string.Empty,
            VoiceAccessToken = voiceSession?.AccessToken ?? string.Empty,
            VoiceQualityLabel = voiceSession?.QualityLabel ?? string.Empty,
            VoiceSampleRateHz = voiceSession?.SampleRateHz ?? 0,
            VoiceBitrateKbps = voiceSession?.BitrateKbps ?? 0,
            VoiceFrameSizeMs = voiceSession?.FrameSizeMs ?? 0,
        };
        state.Calls.Add(call);

        var session = new PersistedActiveCallSession
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            CallId = call.Id,
            DisplayName = conversation.Name,
            IsGroup = request.IsGroup || conversation.IsGroup,
            StartedUtc = startedUtc,
            StartedByAccountId = accountId,
            ParticipantAccountIds = [accountId],
            VoiceProvider = call.VoiceProvider,
            VoiceHost = call.VoiceHost,
            VoiceTcpPort = call.VoiceTcpPort,
            VoiceUdpPort = call.VoiceUdpPort,
            VoiceChannelName = call.VoiceChannelName,
            VoiceAccessToken = call.VoiceAccessToken,
            VoiceQualityLabel = call.VoiceQualityLabel,
            VoiceSampleRateHz = call.VoiceSampleRateHz,
            VoiceBitrateKbps = call.VoiceBitrateKbps,
            VoiceFrameSizeMs = call.VoiceFrameSizeMs,
        };
        state.ActiveCallSessions.Add(session);
        if (call.IsGroup)
        {
            this.AddSystemCallMessage(state, conversation, accountId, ChatMessageKind.CallStarted, $"{caller.DisplayName} started a call", call.Id, null);
        }
        return (session, call, conversation);
    }

    private void ExpireActiveSessions(PersistedAppState state)
    {
        var expiredSessions = state.ActiveCallSessions
            .Where(session => this.IsSessionExpired(session))
            .ToList();

        foreach (var session in expiredSessions)
        {
            var conversation = state.Conversations.Single(item => item.Id == session.ConversationId && !item.IsDeleted);
            var call = state.Calls.SingleOrDefault(item => item.Id == session.CallId);
            this.FinalizeSession(state, session, conversation, call, session.StartedByAccountId);
        }
    }

    private bool IsSessionExpired(PersistedActiveCallSession session)
    {
        var maxMinutes = session.IsGroup ? this.callPolicy.GroupCallMaxMinutes : this.callPolicy.DirectCallMaxMinutes;
        if (maxMinutes is null || maxMinutes <= 0)
        {
            return false;
        }

        return DateTimeOffset.UtcNow >= session.StartedUtc.AddMinutes(maxMinutes.Value);
    }

    private CallSummary FinalizeSession(PersistedAppState state, PersistedActiveCallSession session, PersistedConversation conversation, PersistedCall? call, Guid viewerAccountId)
    {
        call ??= state.Calls.SingleOrDefault(item => item.Id == session.CallId);
        if (call is null)
        {
            call = new PersistedCall
            {
                Id = session.CallId == Guid.Empty ? Guid.NewGuid() : session.CallId,
                ConversationId = session.ConversationId,
                DisplayName = session.DisplayName,
                IsGroup = session.IsGroup,
                StartedByAccountId = session.StartedByAccountId,
                StartedUtc = session.StartedUtc,
            };
            state.Calls.Add(call);
        }

        call.EndedUtc = DateTimeOffset.UtcNow;
        call.DurationSeconds = Math.Max(call.DurationSeconds, (int)Math.Max(0, Math.Round((call.EndedUtc.Value - session.StartedUtc).TotalSeconds)));
        call.Missed = !session.IsGroup && (session.ParticipantAccountIds?.Count ?? 0) <= 1;
        if (session.IsGroup)
        {
            this.AddSystemCallMessage(state, conversation, session.StartedByAccountId, ChatMessageKind.CallEnded, $"Call ended - {FormatDuration(call.DurationSeconds)}", call.Id, call.DurationSeconds);
        }
        state.ActiveCallSessions.RemoveAll(item => item.Id == session.Id);
        return this.MapSummary(call, viewerAccountId);
    }

    private void AddSystemCallMessage(PersistedAppState state, PersistedConversation conversation, Guid actorAccountId, ChatMessageKind kind, string body, Guid callId, int? durationSeconds)
    {
        var actor = state.Accounts.SingleOrDefault(item => item.Id == actorAccountId);
        conversation.Messages.Add(new PersistedMessage
        {
            Id = Guid.NewGuid(),
            SenderAccountId = actorAccountId,
            Body = body,
            SenderGameIdentity = actor?.LastKnownGameIdentity,
            SenderPhoneNumber = actor?.PhoneNumber ?? string.Empty,
            SentAtUtc = DateTimeOffset.UtcNow,
            Kind = kind.ToString(),
            RelatedCallId = callId,
            RelatedCallDurationSeconds = durationSeconds,
            Embeds = [],
        });
    }

    private static string FormatDuration(int durationSeconds)
    {
        return TimeSpan.FromSeconds(Math.Max(0, durationSeconds)) switch
        {
            var duration when duration.TotalHours >= 1 => $"{(int)duration.TotalHours}h {duration.Minutes}m",
            var duration when duration.TotalMinutes >= 1 => $"{duration.Minutes}m {duration.Seconds}s",
            var duration => $"{duration.Seconds}s",
        };
    }

    private bool CanAccessConversation(PersistedAppState state, Guid accountId, Guid conversationId)
    {
        return state.Conversations.Any(item => item.Id == conversationId && !item.IsDeleted && item.Members.Any(member => member.AccountId == accountId));
    }

    private CallSummary MapSummary(PersistedCall call, Guid accountId)
    {
        var direction = call.IsGroup
            ? CallDirection.Group
            : call.StartedByAccountId == accountId ? CallDirection.Outgoing : CallDirection.Incoming;
        var missedForViewer = !call.IsGroup && call.Missed && direction == CallDirection.Incoming;
        var acknowledged = !missedForViewer || call.MissedAcknowledged;
        return new CallSummary(
            call.Id,
            call.DisplayName,
            call.IsGroup ? CallKind.Group : CallKind.Direct,
            direction,
            call.StartedUtc,
            call.EndedUtc,
            TimeSpan.FromSeconds(Math.Max(0, call.DurationSeconds)),
            missedForViewer,
            acknowledged,
            this.MapVoiceSession(call.VoiceProvider, call.VoiceHost, call.VoiceTcpPort, call.VoiceUdpPort, call.VoiceChannelName, call.VoiceAccessToken, call.VoiceQualityLabel, call.VoiceSampleRateHz, call.VoiceBitrateKbps, call.VoiceFrameSizeMs));
    }

    private ActiveCallSessionRecord MapActiveSession(PersistedAppState state, PersistedActiveCallSession session, Guid accountId, PersistedConversation? conversation = null)
    {
        conversation ??= state.Conversations.Single(item => item.Id == session.ConversationId && !item.IsDeleted);
        var startedBy = state.Accounts.SingleOrDefault(item => item.Id == session.StartedByAccountId);
        var participantIds = session.ParticipantAccountIds is { Count: > 0 }
            ? session.ParticipantAccountIds
            : conversation.Members.Select(item => item.AccountId).ToList();
        var participants = participantIds
            .Select(id => state.Accounts.SingleOrDefault(account => account.Id == id)?.DisplayName ?? $"Unknown ({id})")
            .ToList();
        return new ActiveCallSessionRecord(
            session.Id,
            session.ConversationId,
            session.CallId,
            string.IsNullOrWhiteSpace(session.DisplayName) ? conversation.Name : session.DisplayName,
            session.IsGroup,
            session.StartedUtc,
            session.StartedByAccountId,
            startedBy is null ? $"Unknown ({session.StartedByAccountId})" : AccountLabelFormatter.GetDisplayName(startedBy),
            participants,
            participantIds.Contains(accountId),
            this.MapVoiceSession(session.VoiceProvider, session.VoiceHost, session.VoiceTcpPort, session.VoiceUdpPort, session.VoiceChannelName, session.VoiceAccessToken, session.VoiceQualityLabel, session.VoiceSampleRateHz, session.VoiceBitrateKbps, session.VoiceFrameSizeMs));
    }

    private bool CanProvideVoiceSession()
    {
        return this.voiceOptions.Enabled
            && !string.IsNullOrWhiteSpace(this.voiceOptions.Provider)
            && !string.IsNullOrWhiteSpace(this.voiceOptions.Host)
            && this.voiceOptions.TcpPort > 0
            && this.voiceOptions.UdpPort > 0;
    }

    private VoiceSessionInfo CreateVoiceSessionInfo(PersistedConversation conversation, bool isGroup)
    {
        var channelPrefix = isGroup ? "group" : "direct";
        return new VoiceSessionInfo(
            this.voiceOptions.Provider,
            this.voiceOptions.Host,
            this.voiceOptions.TcpPort,
            this.voiceOptions.UdpPort,
            $"{channelPrefix}-{conversation.Id:N}",
            Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant(),
            this.voiceOptions.QualityLabel,
            this.voiceOptions.SampleRateHz,
            this.voiceOptions.BitrateKbps,
            this.voiceOptions.FrameSizeMs);
    }

    private VoiceSessionInfo? MapVoiceSession(string provider, string host, int tcpPort, int udpPort, string channelName, string accessToken, string qualityLabel, int sampleRateHz, int bitrateKbps, int frameSizeMs)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(channelName))
        {
            return null;
        }

        return new VoiceSessionInfo(provider, host, tcpPort, udpPort, channelName, accessToken, qualityLabel, sampleRateHz, bitrateKbps, frameSizeMs);
    }
}


