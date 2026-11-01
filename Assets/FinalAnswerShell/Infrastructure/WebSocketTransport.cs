using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FinalAnswer.Application;
using FinalAnswer.Contracts;

namespace FinalAnswer.Infrastructure
{
    /// <summary>
    /// One socket generation over System.Net.WebSockets.ClientWebSocket: a single receive loop that reassembles
    /// fragments up to EndOfMessage and a single serialized send loop. The inbox is bounded; overflow closes the
    /// socket so the room session resyncs through AUTH instead of growing memory.
    /// </summary>
    public sealed class WebSocketTransport : IRealtimeTransport
    {
        const int InboxLimit = 1024;
        const int OutboxLimit = 256;

        readonly ConcurrentQueue<TransportEvent> inbox = new ConcurrentQueue<TransportEvent>();
        readonly BlockingCollection<string> outbox = new BlockingCollection<string>(OutboxLimit);
        readonly CancellationTokenSource cancel = new CancellationTokenSource();
        readonly ClientWebSocket socket = new ClientWebSocket();
        int closedSignalled;
        bool disposed;

        public void Connect(Uri endpoint)
        {
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            Task.Run(() => RunAsync(endpoint, cancel.Token));
        }

        public void Send(string text)
        {
            if (disposed || outbox.IsAddingCompleted) return;
            if (!outbox.TryAdd(text)) SignalClosed("SEND_QUEUE_FULL");
        }

        public bool TryDequeue(out TransportEvent next) => inbox.TryDequeue(out next);

        public void Close() => Dispose();

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            outbox.CompleteAdding();
            try
            {
                if (socket.State == WebSocketState.Open)
                    socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "CLIENT_CLOSED", CancellationToken.None);
            }
            catch (Exception) { /* best effort */ }
            cancel.Cancel();
        }

        async Task RunAsync(Uri endpoint, CancellationToken token)
        {
            try
            {
                await socket.ConnectAsync(endpoint, token).ConfigureAwait(false);
                Enqueue(new TransportEvent(TransportEventKind.Opened, ""));
                var sending = Task.Run(() => SendLoopAsync(token), token);
                await ReceiveLoopAsync(token).ConfigureAwait(false);
                await Task.WhenAny(sending, Task.Delay(100, CancellationToken.None)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { SignalClosed("CANCELLED"); }
            catch (Exception error) { SignalClosed("CONNECTION_FAILED: " + error.GetType().Name); }
            finally
            {
                // The CancellationTokenSource is left to the GC: Dispose() may still call Cancel() on it.
                try { socket.Dispose(); } catch (Exception) { /* already gone */ }
            }
        }

        async Task ReceiveLoopAsync(CancellationToken token)
        {
            var buffer = new byte[8192];
            using (var message = new MemoryStream())
            {
                while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        SignalClosed(string.IsNullOrEmpty(result.CloseStatusDescription) ? "SERVER_CLOSED" : result.CloseStatusDescription);
                        return;
                    }
                    if (result.MessageType != WebSocketMessageType.Text) continue;
                    message.Write(buffer, 0, result.Count);
                    // Server snapshots may exceed the client envelope bound; cap generously to stay bounded.
                    if (message.Length > EnvelopeCodec.MaxMessageBytes * 64)
                    {
                        SignalClosed("MESSAGE_TOO_LARGE");
                        return;
                    }
                    if (!result.EndOfMessage) continue;
                    var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                    message.SetLength(0);
                    if (inbox.Count >= InboxLimit)
                    {
                        SignalClosed("INBOX_OVERFLOW");
                        return;
                    }
                    Enqueue(new TransportEvent(TransportEventKind.Message, text));
                }
            }
            SignalClosed("CONNECTION_LOST");
        }

        async Task SendLoopAsync(CancellationToken token)
        {
            try
            {
                foreach (var text in outbox.GetConsumingEnumerable(token))
                {
                    var bytes = Encoding.UTF8.GetBytes(text);
                    await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { SignalClosed("SEND_FAILED: " + error.GetType().Name); }
        }

        void Enqueue(TransportEvent next)
        {
            if (!disposed) inbox.Enqueue(next);
        }

        void SignalClosed(string reason)
        {
            if (Interlocked.Exchange(ref closedSignalled, 1) == 1) return;
            Enqueue(new TransportEvent(TransportEventKind.Closed, reason));
        }
    }
}
