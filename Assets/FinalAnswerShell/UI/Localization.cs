using System.Collections.Generic;
using System.Globalization;

namespace FinalAnswer.UI
{
    /// <summary>
    /// Reads the unchanged Java bundles (assets/i18n/messages*.properties, imported as .txt) and mirrors
    /// core/localization/Localization: key lookup with English fallback, {0}-style formats and server error codes.
    /// </summary>
    public sealed class Localization
    {
        readonly Dictionary<string, string> english;
        readonly Dictionary<string, string> russian;
        public string Language { get; set; } = "ru";

        public static Localization Current { get; set; }

        public Localization(string englishText, string russianText)
        {
            english = Parse(englishText);
            russian = Parse(russianText);
        }

        public string Get(string key)
        {
            if (Language == "ru" && russian.TryGetValue(key, out var ru)) return ru;
            return english.TryGetValue(key, out var en) ? en : key;
        }

        public string Format(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, Get(key), args);

        /// <summary>Server error code → localized text (Localization.error in Java).</summary>
        public string Error(string code)
        {
            switch (code)
            {
                case "ROOM_NOT_FOUND": return Get("error.room_missing");
                case "SESSION_ENDED": return Get("error.session_ended");
                case "INVALID_PASSWORD": return Get("error.password");
                case "ROOM_IS_FULL": return Get("error.room_full");
                case "NICKNAME_IN_USE": return Get("error.name_taken");
                case "INVALID_NICKNAME": return Get("error.player_name");
                case "SERVER_FULL": return Get("error.server_full");
                case "INVALID_ROOM_NAME": return Get("error.room_name");
                case "INVALID_PLAYER_LIMIT": return Get("error.player_limit");
                case "INVALID_GAME_MODE": return Get("error.game_mode");
                case "ROOM_NOT_EMPTY": return Get("error.reroll_players");
                case "NOT_ENOUGH_PLAYERS": return Get("network.need_players");
                case "PLAYER_OFFLINE": return Get("network.player_offline");
                case "CHARACTER_IN_USE": return Get("network.character_taken");
                case "HOST_ONLY": return Get("network.host_only");
                case "GAME_ALREADY_STARTED": return Get("network.started");
                case "CONNECTION_LOST": return Get("network.lost");
                case "RATE_LIMITED": return Get("network.rate");
                case "SERVER_UNAVAILABLE": return Get("error.server_unavailable");
                case "INVALID_RESPONSE": return Get("error.server_response");
                default: return code;
            }
        }

        /// <summary>Java .properties subset used by the bundles: key=value, # comments, \uXXXX and \n escapes.</summary>
        static Dictionary<string, string> Parse(string text)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(text)) return result;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r').TrimStart();
                if (line.Length == 0 || line[0] == '#' || line[0] == '!') continue;
                var split = line.IndexOf('=');
                if (split <= 0) continue;
                result[line.Substring(0, split).Trim()] = Unescape(line.Substring(split + 1));
            }
            return result;
        }

        static string Unescape(string value)
        {
            if (value.IndexOf('\\') < 0) return value;
            var builder = new System.Text.StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c != '\\' || i + 1 >= value.Length) { builder.Append(c); continue; }
                var next = value[++i];
                if (next == 'n') builder.Append('\n');
                else if (next == 't') builder.Append('\t');
                else if (next == 'u' && i + 4 < value.Length && int.TryParse(value.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                {
                    builder.Append((char)code);
                    i += 4;
                }
                else builder.Append(next);
            }
            return builder.ToString();
        }
    }
}
