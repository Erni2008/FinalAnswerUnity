using System;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FinalAnswer.Contracts
{
    /// <summary>WebSocket v1 envelope: {"type","requestId","payload"}. Mirrors shared/Protocol.java.</summary>
    public sealed class Envelope
    {
        public string Type { get; }
        /// <summary>Empty for server broadcasts; replies carry the requestId of the original command.</summary>
        public string RequestId { get; }
        public JObject Payload { get; }

        public Envelope(string type, string requestId, JObject payload)
        {
            Type = type ?? throw new ArgumentNullException(nameof(type));
            RequestId = requestId ?? "";
            Payload = payload ?? new JObject();
        }

        public bool IsBroadcast => RequestId.Length == 0;
    }

    public static class EnvelopeCodec
    {
        public const int ProtocolVersion = 1;
        /// <summary>Protocol.MAX_MESSAGE_BYTES, measured in UTF-8 bytes rather than C# chars.</summary>
        public const int MaxMessageBytes = 16384;
        public const int MaxJsonDepth = 24;

        static readonly Regex ClientType = new Regex("^[A-Z_]{1,40}$", RegexOptions.CultureInvariant);
        static readonly Regex ClientRequestId = new Regex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant);

        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            // Payloads are plain data: never honour polymorphic $type metadata.
            TypeNameHandling = TypeNameHandling.None,
            DateParseHandling = DateParseHandling.None,
            // 64-bit seeds and identifiers must not pass through double.
            FloatParseHandling = FloatParseHandling.Decimal,
            MaxDepth = MaxJsonDepth,
        };

        /// <summary>Encodes a client command; throws on anything the Java server would reject before reading it.</summary>
        public static string Encode(Envelope message)
        {
            if (!ClientType.IsMatch(message.Type)) throw new ArgumentException("Invalid client message type: " + message.Type);
            if (!ClientRequestId.IsMatch(message.RequestId)) throw new ArgumentException("Invalid requestId");
            var json = new JObject
            {
                ["type"] = message.Type,
                ["requestId"] = message.RequestId,
                ["payload"] = message.Payload,
            }.ToString(Formatting.None);
            if (Encoding.UTF8.GetByteCount(json) > MaxMessageBytes) throw new ArgumentException("Message exceeds " + MaxMessageBytes + " bytes");
            return json;
        }

        /// <summary>Decodes a server message. Broadcast requestIds are empty, so client regexes are not applied here.</summary>
        public static Envelope Decode(string text)
        {
            if (text == null) throw new FormatException("Empty message");
            JObject root;
            try
            {
                using (var reader = new JsonTextReader(new System.IO.StringReader(text)))
                {
                    reader.MaxDepth = MaxJsonDepth;
                    reader.FloatParseHandling = Settings.FloatParseHandling;
                    reader.DateParseHandling = Settings.DateParseHandling;
                    root = JObject.Load(reader);
                }
            }
            catch (JsonException error)
            {
                throw new FormatException("Malformed server JSON", error);
            }
            var type = root.Value<string>("type");
            if (string.IsNullOrEmpty(type)) throw new FormatException("Missing type");
            var payload = root["payload"] as JObject ?? new JObject();
            return new Envelope(type, root.Value<string>("requestId") ?? "", payload);
        }

        public static JObject ParseObject(string text)
        {
            using (var reader = new JsonTextReader(new System.IO.StringReader(text)))
            {
                reader.MaxDepth = MaxJsonDepth;
                reader.FloatParseHandling = Settings.FloatParseHandling;
                reader.DateParseHandling = Settings.DateParseHandling;
                return JObject.Load(reader);
            }
        }

        public static bool IsValidRequestId(string id) => id != null && ClientRequestId.IsMatch(id);

        public static string NewRequestId() => Guid.NewGuid().ToString("N");
    }
}
