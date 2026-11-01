using System;
using System.Collections.Generic;

namespace FinalAnswer.Contracts
{
    /// <summary>One room mode identifier accepted by RoomService.MODES (core GameMode.getId()).</summary>
    public sealed class GameModeInfo
    {
        public string Id { get; }
        public string Title { get; }
        public string Description { get; }

        public GameModeInfo(string id, string title, string description)
        {
            Id = id;
            Title = title;
            Description = description;
        }
    }

    public static class GameModes
    {
        // Texts copied from assets/i18n/messages_ru.properties. The server only stores the id; it does not
        // implement separate rules for each mode yet (see docs/migration/MIGRATION_PLAN.md §1).
        public static readonly IReadOnlyList<GameModeInfo> All = new[]
        {
            new GameModeInfo("standard", "СТАНДАРТНЫЙ",
                "Игроки отвечают на вопросы викторины. Неправильные ответы отправляют игроков в смертельные мини-игры. Выбывшие становятся призраками, но продолжают участвовать."),
            new GameModeInfo("deadline", "ДЕДЛАЙН",
                "Ускоренная версия стандартного режима: меньше времени на ответы, меньше вопросов, более быстрые мини-игры."),
            new GameModeInfo("every_man_for_himself", "КАЖДЫЙ САМ ЗА СЕБЯ",
                "Каждый борется за собственное выживание. Никаких союзов. Победитель только один."),
            new GameModeInfo("trust_no_one", "НИКОМУ НЕ ВЕРЬ",
                "Временные союзы, тайные цели и предательства. Выбирай, кому доверять."),
            new GameModeInfo("house_rules", "ПРАВИЛА ДОМА",
                "Случайные модификаторы раундов и неожиданные испытания от ведущего."),
        };

        public static GameModeInfo Find(string id)
        {
            foreach (var mode in All) if (mode.Id == id) return mode;
            return null;
        }
    }

    /// <summary>Form fields of POST /rooms, validated with the same bounds as RoomService.create.</summary>
    public sealed class RoomSettings
    {
        public const int MinPlayers = 2, MaxPlayersLimit = 8, MinQuestions = 1, MaxQuestions = 12;

        public string Name = "Final Answer";
        public int MaxPlayers = 4;
        public string GameMode = "standard";
        public int QuestionCount = 5;
        public string Password = "";

        public IReadOnlyDictionary<string, string> ToForm()
        {
            Validate();
            var form = new Dictionary<string, string>
            {
                ["name"] = Name.Trim(),
                ["maxPlayers"] = MaxPlayers.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["gameMode"] = GameMode,
                ["questionCount"] = QuestionCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            if (Password.Length > 0) form["password"] = Password;
            return form;
        }

        public void Validate()
        {
            var name = (Name ?? "").Trim();
            if (name.Length == 0 || name.Length > 40) throw new ArgumentException("Название комнаты: от 1 до 40 символов");
            if (MaxPlayers < MinPlayers || MaxPlayers > MaxPlayersLimit) throw new ArgumentException("Игроков: от 2 до 8");
            if (QuestionCount < MinQuestions || QuestionCount > MaxQuestions) throw new ArgumentException("Вопросов: от 1 до 12");
            if (GameModes.Find(GameMode) == null) throw new ArgumentException("Неизвестный режим");
            if ((Password ?? "").Length > 64) throw new ArgumentException("Пароль: не длиннее 64 символов");
        }
    }
}
