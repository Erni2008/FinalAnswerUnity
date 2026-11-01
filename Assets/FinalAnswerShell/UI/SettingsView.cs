using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace FinalAnswer.UI
{
    /// <summary>Local, per-machine preferences (never sent to the server). Versioned PlayerPrefs keys.</summary>
    public sealed class ShellSettings
    {
        const string Prefix = "fa.v1.";
        public string Language = "ru";
        public bool ReducedMotion;
        public bool PlayIntro = true;
        public float Master = 1, Sfx = 1, Ui = 1, Cages = 1, Environment = 1;
        public string ServerUrl = "http://127.0.0.1:8080";

        public static ShellSettings Load()
        {
            var s = new ShellSettings
            {
                Language = PlayerPrefs.GetString(Prefix + "language", "ru"),
                ReducedMotion = PlayerPrefs.GetInt(Prefix + "reducedMotion", 0) == 1,
                PlayIntro = PlayerPrefs.GetInt(Prefix + "playIntro", 1) == 1,
                Master = PlayerPrefs.GetFloat(Prefix + "master", 1),
                Sfx = PlayerPrefs.GetFloat(Prefix + "sfx", 1),
                Ui = PlayerPrefs.GetFloat(Prefix + "ui", 1),
                Cages = PlayerPrefs.GetFloat(Prefix + "cages", 1),
                Environment = PlayerPrefs.GetFloat(Prefix + "environment", 1),
                ServerUrl = PlayerPrefs.GetString(Prefix + "serverUrl", "http://127.0.0.1:8080"),
            };
            return s;
        }

        public void Save()
        {
            PlayerPrefs.SetString(Prefix + "language", Language);
            PlayerPrefs.SetInt(Prefix + "reducedMotion", ReducedMotion ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "playIntro", PlayIntro ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "master", Master);
            PlayerPrefs.SetFloat(Prefix + "sfx", Sfx);
            PlayerPrefs.SetFloat(Prefix + "ui", Ui);
            PlayerPrefs.SetFloat(Prefix + "cages", Cages);
            PlayerPrefs.SetFloat(Prefix + "environment", Environment);
            PlayerPrefs.SetString(Prefix + "serverUrl", ServerUrl);
            PlayerPrefs.Save();
        }

        public void ApplyTo(ShellAudio audio, Localization texts)
        {
            if (audio != null)
            {
                audio.Master = Master;
                audio.Sfx = Sfx;
                audio.Ui = Ui;
                audio.Cages = Cages;
                audio.Environment = Environment;
            }
            if (texts != null) texts.Language = Language;
        }
    }

    /// <summary>Modal settings panel over the menu: language, reduced motion, server address and the AudioMix categories.</summary>
    public sealed class SettingsView
    {
        public RectTransform Root { get; }

        public SettingsView(Transform parent, Font font, ShellSettings settings, Action changed, Action close)
        {
            Root = UiKit.Stretch(UiKit.Rect("Settings", parent));
            Root.gameObject.AddComponent<Image>().color = new Color(.008f, .003f, .008f, .78f);
            var panel = UiKit.Panel(Root, "Panel", new Vector2(980, 980), spacing: 14);
            panel.GetComponent<Image>().color = new Color(.075f, .035f, .04f, .97f);
            Label(panel, font, settings.Language == "ru" ? "НАСТРОЙКИ" : "SETTINGS", 46, 70, TextAnchor.MiddleCenter);

            var language = new Stepper(UiKit.Field(panel, "Язык / Language"), 0, 1, settings.Language == "ru" ? 0 : 1, i => i == 0 ? "РУССКИЙ" : "ENGLISH", wrap: true);
            language.Changed += i => { settings.Language = i == 0 ? "ru" : "en"; changed(); };
            var motion = new Stepper(UiKit.Field(panel, "Меньше движения"), 0, 1, settings.ReducedMotion ? 1 : 0, i => i == 1 ? "ВКЛ" : "ВЫКЛ", wrap: true);
            motion.Changed += i => { settings.ReducedMotion = i == 1; changed(); };
            var intro = new Stepper(UiKit.Field(panel, "Интро перед игрой"), 0, 1, settings.PlayIntro ? 1 : 0, i => i == 1 ? "ВКЛ" : "ВЫКЛ", wrap: true);
            intro.Changed += i => { settings.PlayIntro = i == 1; changed(); };
            Volume(panel, "Общая громкость", settings.Master, v => { settings.Master = v; changed(); });
            Volume(panel, "Эффекты", settings.Sfx, v => { settings.Sfx = v; changed(); });
            Volume(panel, "Интерфейс", settings.Ui, v => { settings.Ui = v; changed(); });
            Volume(panel, "Клетки", settings.Cages, v => { settings.Cages = v; changed(); });
            Volume(panel, "Окружение", settings.Environment, v => { settings.Environment = v; changed(); });

            var serverRow = UiKit.Field(panel, "Сервер");
            var server = UiKit.Input(serverRow, settings.ServerUrl, 120, "http://127.0.0.1:8080");
            UiKit.Size(server.gameObject, flexibleWidth: 1);
            server.onEndEdit.AddListener(v =>
            {
                if (Uri.TryCreate(v.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
                {
                    settings.ServerUrl = v.Trim().TrimEnd('/');
                    changed();
                }
                else server.text = settings.ServerUrl;
            });
            var done = UiKit.Button(panel, settings.Language == "ru" ? "ГОТОВО" : "DONE", close, primary: true, height: 76);
            done.gameObject.AddComponent<UiSoundHooks>();
        }

        static void Label(Transform parent, Font font, string text, int size, float height, TextAnchor anchor)
        {
            var label = UiKit.Label(parent, text, size, Theme.Text, anchor, FontStyle.Bold);
            label.font = font;
            UiKit.Size(label.gameObject, height: height);
        }

        static void Volume(Transform parent, string caption, float value, Action<float> set)
        {
            var stepper = new Stepper(UiKit.Field(parent, caption), 0, 10, Mathf.RoundToInt(value * 10),
                i => (i * 10).ToString(CultureInfo.InvariantCulture) + " %");
            stepper.Changed += i => set(i / 10f);
        }
    }
}
