using System.IO;
using FinalAnswer.Domain;
using UnityEngine;

namespace FinalAnswer.EditorTools
{
    /// <summary>Verification helper: writes QR PNGs (8 px modules, 4-module quiet zone) for an external decoder.</summary>
    public static class QrExport
    {
        public static void Run()
        {
            var dir = System.Environment.GetEnvironmentVariable("FA_QR_OUT") ?? "Temp/qr";
            Directory.CreateDirectory(dir);
            string[] samples =
            {
                "http://192.168.1.23:8080/?room=ABC234",
                "http://10.0.0.5:18080/?room=XYZ789",
                "https://final-answer.example.org/?room=QWERTY&lang=ru&very-long-parameter=abcdefghijklmnopqrstuvwxyz0123456789",
                "http://172.20.10.2:8080/?room=HJK234",
            };
            for (var n = 0; n < samples.Length; n++)
            {
                var code = QrCode.Encode(samples[n]);
                const int scale = 8, quiet = 4;
                var size = (code.Size + quiet * 2) * scale;
                var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
                for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    int mx = x / scale - quiet, my = code.Size - 1 - (y / scale - quiet);
                    var dark = mx >= 0 && mx < code.Size && my >= 0 && my < code.Size && code[mx, my];
                    texture.SetPixel(x, y, dark ? Color.black : Color.white);
                }
                File.WriteAllBytes(Path.Combine(dir, n + ".png"), texture.EncodeToPNG());
                File.WriteAllText(Path.Combine(dir, n + ".txt"), samples[n]);
                Debug.Log("[QR] " + n + " v" + code.Version + " mask " + code.Mask);
            }
        }
    }
}
