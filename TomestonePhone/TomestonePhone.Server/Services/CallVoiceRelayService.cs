using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace TomestonePhone.Server.Services;

public sealed class CallVoiceRelayService
{
    private readonly IPhoneRepository repository;
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, ConnectedPeer>> sessions = new();

    public CallVoiceRelayService(IPhoneRepository repository)
    {
        this.repository = repository;
    }

    public Task<bool> CanJoinSessionAsync(Guid sessionId, Guid accountId, CancellationToken cancellationToken = default)
    {
        return this.repository.ReadAsync(state =>
        {
            var session = state.ActiveCallSessions.SingleOrDefault(item => item.Id == sessionId);
            return session is not null && session.ParticipantAccountIds.Contains(accountId);
        }, cancellationToken);
    }

    public async Task RelayAsync(Guid sessionId, Guid accountId, WebSocket socket, CancellationToken cancellationToken = default)
    {
        var peers = this.sessions.GetOrAdd(sessionId, _ => new ConcurrentDictionary<Guid, ConnectedPeer>());
        var peer = new ConnectedPeer(accountId, socket);
        if (peers.TryGetValue(accountId, out var existing))
        {
            await existing.CloseAsync().ConfigureAwait(false);
            peers.TryRemove(accountId, out _);
        }

        peers[accountId] = peer;
        var receiveBuffer = new byte[8 * 1024];

        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var packetBuffer = new MemoryStream();
                WebSocketReceiveResult? result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    if (result.MessageType != WebSocketMessageType.Binary)
                    {
                        continue;
                    }

                    packetBuffer.Write(receiveBuffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                var payload = packetBuffer.ToArray();
                if (payload.Length == 0)
                {
                    continue;
                }

                var outgoing = new byte[16 + payload.Length];
                var senderBytes = accountId.ToByteArray();
                Buffer.BlockCopy(senderBytes, 0, outgoing, 0, senderBytes.Length);
                Buffer.BlockCopy(payload, 0, outgoing, 16, payload.Length);

                foreach (var otherPeer in peers.Values.Where(item => item.AccountId != accountId))
                {
                    await otherPeer.SendAsync(outgoing, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            peers.TryRemove(accountId, out _);
            if (peers.IsEmpty)
            {
                this.sessions.TryRemove(sessionId, out _);
            }

            await peer.CloseAsync().ConfigureAwait(false);
        }
    }

    private sealed class ConnectedPeer
    {
        private readonly SemaphoreSlim sendLock = new(1, 1);

        public ConnectedPeer(Guid accountId, WebSocket socket)
        {
            this.AccountId = accountId;
            this.Socket = socket;
        }

        public Guid AccountId { get; }

        public WebSocket Socket { get; }

        public async Task SendAsync(byte[] payload, CancellationToken cancellationToken)
        {
            if (this.Socket.State != WebSocketState.Open)
            {
                return;
            }

            await this.sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (this.Socket.State == WebSocketState.Open)
                {
                    await this.Socket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                this.sendLock.Release();
            }
        }

        public async Task CloseAsync()
        {
            try
            {
                if (this.Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await this.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Voice relay closed", CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch
            {
            }
            finally
            {
                this.Socket.Dispose();
                this.sendLock.Dispose();
            }
        }
    }
}
