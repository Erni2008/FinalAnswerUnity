using System;
using System.Collections.Generic;
using FinalAnswer.Contracts;
using FinalAnswer.Domain;
using Newtonsoft.Json.Linq;

namespace FinalAnswer.Application
{
    public enum ConnectionState { Idle, Connecting, Authenticating, Online, Reconnecting, Failed, Closed }

    public sealed class CommandRejection
    {
        public string CommandType { get; }
        public string RequestId { get; }
        public string Code { get; }

        public CommandRejection(string commandType, string requestId, string code)
        {
            CommandType = commandType;
            RequestId = requestId;
            Code = code;
        }
    }

    /// <summary>
    /// Host room session over the unchanged v1 protocol: AUTH with the host token, app-level PING every 10 s,
    /// request/reply correlation and reconnect with the same token. Owns no game rules.
    /// Everything runs from <see cref="Tick"/> on the main thread.
    /// </summary>
    public sealed class RoomManager
    {
        public const long PingIntervalMs = 10_000;
        public const long AuthTimeoutMs = 5_000;
        const int MaxEventsPerTick = 256;

        readonly Func<IRealtimeTransport> transportFactory;
        readonly IMonotonicClock clock;
        readonly ReconnectPolicy reconnect;
        readonly Dictionary<string, PendingCommand> pending = new Dictionary<string, PendingCommand>();

        IRealtimeTransport transport;
        string baseUrl = "";
        string token = "";
        long lastPingAt;
        long connectStartedAt;
        long reconnectAt;
        bool intentionalClose;

        public SnapshotStore Snapshots { get; } = new SnapshotStore();
        public ServerClock ServerClock { get; } = new ServerClock();
        public ConnectionState State { get; private set; } = ConnectionState.Idle;
        public string RoomCode { get; private set; } = "";
        public string BaseUrl => baseUrl;
        public string LastCloseReason { get; private set; } = "";
        public int ReconnectAttempt => reconnect.Attempt;

        public event Action<ConnectionState> ConnectionChanged;
        public event Action<CommandRejection> CommandRejected;
        /// <summary>Every non-snapshot or snapshot-carrying event, after the snapshot was applied.</summary>
        public event Action<Envelope> EventReceived;
        public event Action<string> RoomClosed;

        public RoomManager(Func<IRealtimeTransport> transportFactory, IMonotonicClock clock, ReconnectPolicy reconnect = null)
        {
            this.transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.reconnect = reconnect ?? new ReconnectPolicy();
        }

        public long ServerNow => ServerClock.Now(clock.NowMs);

        /// <summary>Starts the realtime session for a room created over HTTP. The token stays in memory only.</summary>
        public void Begin(string serverBaseUrl, RoomSnapshot created)
        {
            if (created == null || created.Token.Length == 0) throw new ArgumentException("Create response has no host credentials");
            End();
            baseUrl = serverBaseUrl.TrimEnd('/');
            token = created.Token;
            RoomCode = created.RoomCode;
            intentionalClose = false;
            reconnect.Reset();
            ServerClock.Reset();
            Snapshots.Reset();
            Snapshots.TryApply(created);
            OpenSocket();
        }

        /// <summary>Sends a command. Returns its requestId, or null when not online.</summary>
        public string Send(string type, JObject payload, Action<Envelope> onReply = null)
        {
            if (State != ConnectionState.Online || transport == null) return null;
            var requestId = EnvelopeCodec.NewRequestId();
            transport.Send(EnvelopeCodec.Encode(new Envelope(type, requestId, payload ?? new JObject())));
            pending[requestId] = new PendingCommand(type, clock.NowMs, onReply);
            if (pending.Count > 512) TrimPending();
            return requestId;
        }

        /// <summary>Payload with roomCode pre-filled, which every room command accepts.</summary>
        public JObject RoomPayload() => new JObject { ["roomCode"] = RoomCode };

        /// <summary>Developer tool: drop the socket as if the network failed, exercising the real reconnect path.</summary>
        public void SimulateConnectionLoss()
        {
            if (transport == null) return;
            ConnectionLost("DEV_SIMULATED_DROP");
        }

        /// <summary>Asks for a fresh STATE (existing SYNC command).</summary>
        public string RequestSync() => Send(MessageTypes.Sync, RoomPayload());

        public void RetryNow()
        {
            if (State != ConnectionState.Failed && State != ConnectionState.Reconnecting) return;
            reconnect.Reset();
            OpenSocket();
        }

        public void Tick()
        {
            var now = clock.NowMs;
            if (State == ConnectionState.Reconnecting && reconnectAt > 0 && now >= reconnectAt)
            {
                reconnectAt = 0;
                OpenSocket();
            }
            if (transport == null) return;

            for (var i = 0; i < MaxEventsPerTick && transport != null && transport.TryDequeue(out var next); i++)
                Handle(next, now);

            if (transport == null) return;
            if ((State == ConnectionState.Connecting || State == ConnectionState.Authenticating) && now - connectStartedAt > AuthTimeoutMs + 2_000)
                ConnectionLost("AUTH_TIMEOUT");
            else if (State == ConnectionState.Online && now - lastPingAt >= PingIntervalMs)
            {
                lastPingAt = now;
                Send(MessageTypes.Ping, RoomPayload());
            }
        }

        /// <summary>Closes the socket and, as host, the room (HTTP leave is done by the caller if needed).</summary>
        public void End()
        {
            intentionalClose = true;
            if (transport != null && State == ConnectionState.Online)
            {
                try { transport.Send(EnvelopeCodec.Encode(new Envelope(MessageTypes.LeaveRoom, EnvelopeCodec.NewRequestId(), RoomPayload()))); }
                catch (Exception) { /* closing anyway */ }
            }
            DisposeTransport();
            pending.Clear();
            token = "";
            SetState(ConnectionState.Idle);
        }

        void OpenSocket()
        {
            DisposeTransport();
            transport = transportFactory();
            connectStartedAt = clock.NowMs;
            SetState(reconnect.Attempt > 0 ? ConnectionState.Reconnecting : ConnectionState.Connecting);
            transport.Connect(SocketUri(baseUrl));
        }

        void Handle(TransportEvent next, long now)
        {
            switch (next.Kind)
            {
                case TransportEventKind.Opened:
                    return;
                case TransportEventKind.Closed:
                    ConnectionLost(next.Text);
                    return;
            }

            Envelope message;
            try { message = EnvelopeCodec.Decode(next.Text); }
            catch (FormatException) { return; }

            if (message.Type == MessageTypes.Connected)
            {
                SetState(ConnectionState.Authenticating);
                var auth = new JObject { ["roomCode"] = RoomCode, ["token"] = token };
                transport.Send(EnvelopeCodec.Encode(new Envelope(MessageTypes.Auth, EnvelopeCodec.NewRequestId(), auth)));
                return;
            }
            if (message.Type == MessageTypes.RoomClosed)
            {
                LastCloseReason = message.Payload.Value<string>("reason") ?? "";
                intentionalClose = true;
                DisposeTransport();
                SetState(ConnectionState.Closed);
                RoomClosed?.Invoke(LastCloseReason);
                return;
            }

            PendingCommand command = null;
            if (!message.IsBroadcast && pending.TryGetValue(message.RequestId, out command)) pending.Remove(message.RequestId);

            // Correlation/rejection first, then the snapshot (equal revision must not be dropped before the ACK).
            if (MessageTypes.IsRejection(message.Type))
            {
                var code = message.Payload.Value<string>("code") ?? "UNKNOWN";
                if (State == ConnectionState.Authenticating) { FailAuth(code); return; }
                CommandRejected?.Invoke(new CommandRejection(command?.Type ?? "", message.RequestId, code));
            }
            else if (message.Payload["roomId"] != null)
            {
                var snapshot = new RoomSnapshot(message.Payload);
                if (command != null) ServerClock.Sample(snapshot.ServerTime, command.SentAt, now);
                else ServerClock.SampleBroadcast(snapshot.ServerTime, now);
                if (message.Type == MessageTypes.Authenticated)
                {
                    reconnect.Reset();
                    lastPingAt = now;
                    SetState(ConnectionState.Online);
                }
                Snapshots.TryApply(snapshot);
            }

            command?.OnReply?.Invoke(message);
            EventReceived?.Invoke(message);
        }

        void FailAuth(string code)
        {
            // Bad/ended credentials cannot be fixed by retrying with the same token.
            LastCloseReason = code;
            intentionalClose = true;
            DisposeTransport();
            SetState(ConnectionState.Failed);
        }

        void ConnectionLost(string reason)
        {
            LastCloseReason = reason;
            DisposeTransport();
            pending.Clear();
            if (intentionalClose) return;
            if (reason == "SESSION_REPLACED" || reconnect.Exhausted)
            {
                SetState(ConnectionState.Failed);
                return;
            }
            reconnectAt = clock.NowMs + reconnect.NextDelayMs();
            SetState(ConnectionState.Reconnecting);
        }

        void DisposeTransport()
        {
            if (transport == null) return;
            var old = transport;
            transport = null; // late events of this generation are never drained again
            old.Dispose();
        }

        void TrimPending()
        {
            var cutoff = clock.NowMs - 60_000;
            var stale = new List<string>();
            foreach (var entry in pending) if (entry.Value.SentAt < cutoff) stale.Add(entry.Key);
            foreach (var key in stale) pending.Remove(key);
        }

        void SetState(ConnectionState next)
        {
            if (State == next) return;
            State = next;
            ConnectionChanged?.Invoke(next);
        }

        public static Uri SocketUri(string httpBase)
        {
            var uri = new Uri(httpBase.TrimEnd('/') + "/ws");
            var builder = new UriBuilder(uri) { Scheme = uri.Scheme == "https" ? "wss" : "ws" };
            if (uri.IsDefaultPort) builder.Port = -1;
            return builder.Uri;
        }

        sealed class PendingCommand
        {
            public readonly string Type;
            public readonly long SentAt;
            public readonly Action<Envelope> OnReply;

            public PendingCommand(string type, long sentAt, Action<Envelope> onReply)
            {
                Type = type;
                SentAt = sentAt;
                OnReply = onReply;
            }
        }
    }
}
