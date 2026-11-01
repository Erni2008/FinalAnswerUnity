using System;
using System.Collections.Generic;
using System.Text;

namespace FinalAnswer.Domain
{
    /// <summary>
    /// Minimal ISO/IEC 18004 encoder for the phone join URL: byte mode, error correction M (as the Java
    /// ConnectionQr/zxing call), versions 1–10, automatic mask. Pure C#; generated locally, no network.
    /// </summary>
    public sealed class QrCode
    {
        // Error correction level M: ecc codewords per block, then (blocks, data codewords) for groups 1 and 2.
        static readonly int[][] BlocksM =
        {
            null,
            new[] { 10, 1, 16, 0, 0 }, new[] { 16, 1, 28, 0, 0 }, new[] { 26, 1, 44, 0, 0 }, new[] { 18, 2, 32, 0, 0 },
            new[] { 24, 2, 43, 0, 0 }, new[] { 16, 4, 27, 0, 0 }, new[] { 18, 4, 31, 0, 0 }, new[] { 22, 2, 38, 2, 39 },
            new[] { 22, 3, 36, 2, 37 }, new[] { 26, 4, 43, 1, 44 },
        };
        static readonly int[][] Alignment =
        {
            null, new int[0], new[] { 6, 18 }, new[] { 6, 22 }, new[] { 6, 26 }, new[] { 6, 30 }, new[] { 6, 34 },
            new[] { 6, 22, 38 }, new[] { 6, 24, 42 }, new[] { 6, 26, 46 }, new[] { 6, 28, 50 },
        };
        public const int MaxVersion = 10;

        public int Version { get; }
        public int Size { get; }
        public int Mask { get; }
        readonly bool[,] modules;
        readonly bool[,] function;

        /// <summary>True for a dark module at column x, row y (0,0 = top-left, no quiet zone).</summary>
        public bool this[int x, int y] => modules[y, x];

        QrCode(int version, byte[] codewords)
        {
            Version = version;
            Size = version * 4 + 17;
            modules = new bool[Size, Size];
            function = new bool[Size, Size];
            DrawFunctionPatterns();
            DrawCodewords(codewords);
            var best = 0;
            var bestPenalty = int.MaxValue;
            for (var mask = 0; mask < 8; mask++)
            {
                ApplyMask(mask);
                DrawFormatBits(mask);
                var penalty = Penalty();
                if (penalty < bestPenalty) { bestPenalty = penalty; best = mask; }
                ApplyMask(mask); // XOR undo
            }
            Mask = best;
            ApplyMask(best);
            DrawFormatBits(best);
        }

        public static QrCode Encode(string text)
        {
            var data = Encoding.UTF8.GetBytes(text ?? "");
            for (var version = 1; version <= MaxVersion; version++)
            {
                var countBits = version < 10 ? 8 : 16;
                var capacity = DataCodewords(version);
                if (4 + countBits + data.Length * 8 > capacity * 8) continue;
                var bits = new BitBuffer();
                bits.Append(0b0100, 4);
                bits.Append(data.Length, countBits);
                foreach (var b in data) bits.Append(b, 8);
                bits.Append(0, Math.Min(4, capacity * 8 - bits.Count));
                bits.Append(0, (8 - bits.Count % 8) % 8);
                for (var pad = 0xEC; bits.Count < capacity * 8; pad ^= 0xEC ^ 0x11) bits.Append(pad, 8);
                return new QrCode(version, AddErrorCorrection(version, bits.ToBytes()));
            }
            throw new ArgumentException("Text too long for a version " + MaxVersion + " QR code");
        }

        static int DataCodewords(int version)
        {
            var b = BlocksM[version];
            return b[1] * b[2] + b[3] * b[4];
        }

        static byte[] AddErrorCorrection(int version, byte[] data)
        {
            var spec = BlocksM[version];
            var divisor = RsDivisor(spec[0]);
            var dataBlocks = new List<byte[]>();
            var eccBlocks = new List<byte[]>();
            var offset = 0;
            for (var group = 0; group < 2; group++)
            {
                int count = spec[1 + group * 2], length = spec[2 + group * 2];
                for (var i = 0; i < count; i++)
                {
                    var block = new byte[length];
                    Array.Copy(data, offset, block, 0, length);
                    offset += length;
                    dataBlocks.Add(block);
                    eccBlocks.Add(RsRemainder(block, divisor));
                }
            }
            var result = new List<byte>();
            var longest = 0;
            foreach (var block in dataBlocks) longest = Math.Max(longest, block.Length);
            for (var i = 0; i < longest; i++)
                foreach (var block in dataBlocks) if (i < block.Length) result.Add(block[i]);
            for (var i = 0; i < spec[0]; i++)
                foreach (var block in eccBlocks) result.Add(block[i]);
            return result.ToArray();
        }

        void DrawFunctionPatterns()
        {
            for (var i = 0; i < Size; i++)
            {
                SetFunction(6, i, i % 2 == 0);
                SetFunction(i, 6, i % 2 == 0);
            }
            DrawFinder(3, 3);
            DrawFinder(Size - 4, 3);
            DrawFinder(3, Size - 4);
            var positions = Alignment[Version];
            var last = positions.Length - 1;
            for (var i = 0; i < positions.Length; i++)
            for (var j = 0; j < positions.Length; j++)
            {
                if (i == 0 && j == 0 || i == 0 && j == last || i == last && j == 0) continue;
                for (var dy = -2; dy <= 2; dy++)
                for (var dx = -2; dx <= 2; dx++)
                    SetFunction(positions[i] + dx, positions[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
            DrawFormatBits(0); // reserve; real bits are drawn after mask selection
            if (Version >= 7)
            {
                var rem = Version;
                for (var i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
                var bits = Version << 12 | rem;
                for (var i = 0; i < 18; i++)
                {
                    var bit = (bits >> i & 1) != 0;
                    int a = Size - 11 + i % 3, b = i / 3;
                    SetFunction(a, b, bit);
                    SetFunction(b, a, bit);
                }
            }
        }

        void DrawFinder(int cx, int cy)
        {
            for (var dy = -4; dy <= 4; dy++)
            for (var dx = -4; dx <= 4; dx++)
            {
                int x = cx + dx, y = cy + dy;
                var distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                if (x >= 0 && x < Size && y >= 0 && y < Size) SetFunction(x, y, distance != 2 && distance != 4);
            }
        }

        void DrawFormatBits(int mask)
        {
            var data = 0 << 3 | mask; // level M = 0b00
            var rem = data;
            for (var i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            var bits = (data << 10 | rem) ^ 0x5412;
            for (var i = 0; i <= 5; i++) SetFunction(8, i, Bit(bits, i));
            SetFunction(8, 7, Bit(bits, 6));
            SetFunction(8, 8, Bit(bits, 7));
            SetFunction(7, 8, Bit(bits, 8));
            for (var i = 9; i < 15; i++) SetFunction(14 - i, 8, Bit(bits, i));
            for (var i = 0; i < 8; i++) SetFunction(Size - 1 - i, 8, Bit(bits, i));
            for (var i = 8; i < 15; i++) SetFunction(8, Size - 15 + i, Bit(bits, i));
            SetFunction(8, Size - 8, true);
        }

        void DrawCodewords(byte[] data)
        {
            var i = 0;
            for (var right = Size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (var vert = 0; vert < Size; vert++)
                for (var j = 0; j < 2; j++)
                {
                    var x = right - j;
                    var upward = ((right + 1) & 2) == 0;
                    var y = upward ? Size - 1 - vert : vert;
                    if (function[y, x] || i >= data.Length * 8) continue;
                    modules[y, x] = (data[i >> 3] >> (7 - (i & 7)) & 1) != 0;
                    i++;
                }
            }
        }

        void ApplyMask(int mask)
        {
            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                if (function[y, x]) continue;
                bool invert;
                switch (mask)
                {
                    case 0: invert = (x + y) % 2 == 0; break;
                    case 1: invert = y % 2 == 0; break;
                    case 2: invert = x % 3 == 0; break;
                    case 3: invert = (x + y) % 3 == 0; break;
                    case 4: invert = (x / 3 + y / 2) % 2 == 0; break;
                    case 5: invert = x * y % 2 + x * y % 3 == 0; break;
                    case 6: invert = (x * y % 2 + x * y % 3) % 2 == 0; break;
                    default: invert = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                }
                modules[y, x] ^= invert;
            }
        }

        int Penalty()
        {
            var result = 0;
            var dark = 0;
            for (var pass = 0; pass < 2; pass++)
            for (var a = 0; a < Size; a++)
            {
                var run = 1;
                for (var b = 1; b < Size; b++)
                {
                    bool previous = pass == 0 ? modules[a, b - 1] : modules[b - 1, a];
                    bool current = pass == 0 ? modules[a, b] : modules[b, a];
                    if (current == previous) { run++; if (run == 5) result += 3; else if (run > 5) result++; }
                    else run = 1;
                }
            }
            for (var y = 0; y < Size - 1; y++)
            for (var x = 0; x < Size - 1; x++)
            {
                var c = modules[y, x];
                if (c == modules[y, x + 1] && c == modules[y + 1, x] && c == modules[y + 1, x + 1]) result += 3;
            }
            for (var a = 0; a < Size; a++)
            for (var b = 0; b + 10 < Size; b++)
            {
                if (Finder(i => modules[a, b + i])) result += 40;
                if (Finder(i => modules[b + i, a])) result += 40;
            }
            foreach (var m in modules) if (m) dark++;
            var total = Size * Size;
            var k = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
            return result + Math.Max(0, k) * 10;
        }

        static bool Finder(Func<int, bool> at)
        {
            bool[] pattern = { true, false, true, true, true, false, true };
            var core = true;
            for (var i = 0; i < 7; i++) core &= at(i + 2) == pattern[i];
            if (!core) return false;
            var leftLight = !at(0) && !at(1);
            var rightLight = !at(9) && !at(10);
            return leftLight || rightLight;
        }

        void SetFunction(int x, int y, bool dark)
        {
            modules[y, x] = dark;
            function[y, x] = true;
        }

        static bool Bit(int value, int index) => (value >> index & 1) != 0;

        static byte[] RsDivisor(int degree)
        {
            var result = new byte[degree];
            result[degree - 1] = 1;
            var root = 1;
            for (var i = 0; i < degree; i++)
            {
                for (var j = 0; j < degree; j++)
                {
                    result[j] = Multiply(result[j], root);
                    if (j + 1 < degree) result[j] ^= result[j + 1];
                }
                root = Multiply(root, 0x02);
            }
            return result;
        }

        static byte[] RsRemainder(byte[] data, byte[] divisor)
        {
            var result = new byte[divisor.Length];
            foreach (var b in data)
            {
                var factor = b ^ result[0];
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (var i = 0; i < result.Length; i++) result[i] ^= Multiply(divisor[i], factor);
            }
            return result;
        }

        static byte Multiply(int x, int y)
        {
            var z = 0;
            for (var i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return (byte)z;
        }

        sealed class BitBuffer
        {
            readonly List<bool> bits = new List<bool>();
            public int Count => bits.Count;

            public void Append(int value, int length)
            {
                for (var i = length - 1; i >= 0; i--) bits.Add((value >> i & 1) != 0);
            }

            public byte[] ToBytes()
            {
                var result = new byte[bits.Count / 8];
                for (var i = 0; i < bits.Count; i++) if (bits[i]) result[i >> 3] |= (byte)(0x80 >> (i & 7));
                return result;
            }
        }
    }
}
