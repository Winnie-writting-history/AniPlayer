using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AnniPlayer.Services
{
    /// <summary>
    /// Pure C# Zero-Dependency QR Code Generator (Model 2, Byte Encoding, ECC-M/L)
    /// Produces crisp, standard-compliant BitmapSource for any wallet address or text.
    /// </summary>
    public static class QrCodeService
    {
        #region ══ QR CODE CONSTANTS & TABLES ══

        // Galois Field GF(2^8) with poly 0x11D
        private static readonly byte[] Exp = new byte[512];
        private static readonly byte[] Log = new byte[256];

        // Version definitions: (Version, TotalCodewords, EccCodewordsPerBlock, NumBlocks) for ECC Level M
        private struct VersionInfo
        {
            public int Version;
            public int Dimension;
            public int TotalDataBytes;
            public int EccBytesPerBlock;
            public int NumBlocksGroup1;
            public int DataBytesGroup1;
            public int NumBlocksGroup2;
            public int DataBytesGroup2;
            public int[] AlignmentPatternCoords;
        }

        private static readonly VersionInfo[] Versions = new VersionInfo[]
        {
            // Ver 1: 21x21, 16 data bytes
            new VersionInfo { Version = 1, Dimension = 21, TotalDataBytes = 16, EccBytesPerBlock = 10, NumBlocksGroup1 = 1, DataBytesGroup1 = 16, NumBlocksGroup2 = 0, DataBytesGroup2 = 0, AlignmentPatternCoords = Array.Empty<int>() },
            // Ver 2: 25x25, 28 data bytes
            new VersionInfo { Version = 2, Dimension = 25, TotalDataBytes = 28, EccBytesPerBlock = 16, NumBlocksGroup1 = 1, DataBytesGroup1 = 28, NumBlocksGroup2 = 0, DataBytesGroup2 = 0, AlignmentPatternCoords = new[] { 6, 18 } },
            // Ver 3: 29x29, 44 data bytes (Fits 42-char ETH and 34-char TRON addresses easily!)
            new VersionInfo { Version = 3, Dimension = 29, TotalDataBytes = 44, EccBytesPerBlock = 26, NumBlocksGroup1 = 1, DataBytesGroup1 = 44, NumBlocksGroup2 = 0, DataBytesGroup2 = 0, AlignmentPatternCoords = new[] { 6, 22 } },
            // Ver 4: 33x33, 64 data bytes
            new VersionInfo { Version = 4, Dimension = 33, TotalDataBytes = 64, EccBytesPerBlock = 18, NumBlocksGroup1 = 2, DataBytesGroup1 = 32, NumBlocksGroup2 = 0, DataBytesGroup2 = 0, AlignmentPatternCoords = new[] { 6, 26 } },
            // Ver 5: 37x37, 86 data bytes
            new VersionInfo { Version = 5, Dimension = 37, TotalDataBytes = 86, EccBytesPerBlock = 24, NumBlocksGroup1 = 2, DataBytesGroup1 = 43, NumBlocksGroup2 = 0, DataBytesGroup2 = 0, AlignmentPatternCoords = new[] { 6, 30 } },
            // Ver 6: 41x41, 108 data bytes
            new VersionInfo { Version = 6, Dimension = 41, TotalDataBytes = 108, EccBytesPerBlock = 16, NumBlocksGroup1 = 4, DataBytesGroup1 = 27, NumBlocksGroup2 = 0, DataBytesGroup2 = 0, AlignmentPatternCoords = new[] { 6, 34 } }
        };

        static QrCodeService()
        {
            // Initialize Galois Field tables
            int value = 1;
            for (int exponent = 0; exponent < 255; exponent++)
            {
                Exp[exponent] = (byte)value;
                Exp[exponent + 255] = (byte)value;
                Log[value] = (byte)exponent;
                value <<= 1;
                if ((value & 0x100) != 0)
                {
                    value ^= 0x11D;
                }
            }
        }

        #endregion

        #region ══ PUBLIC API ══

        /// <summary>
        /// Generates a high-quality BitmapSource QR Code image for the specified text.
        /// </summary>
        public static BitmapSource GenerateQrBitmap(string text, int pixelSize = 256, int quietZone = 3)
        {
            if (string.IsNullOrEmpty(text)) text = " ";
            byte[] textBytes = System.Text.Encoding.UTF8.GetBytes(text);

            // Select smallest version that fits textBytes
            VersionInfo ver = Versions[0];
            bool found = false;
            foreach (var v in Versions)
            {
                // Byte mode overhead: 4 bits mode + 8 bits char count = 12 bits = 2 bytes approx
                if (textBytes.Length + 2 <= v.TotalDataBytes)
                {
                    ver = v;
                    found = true;
                    break;
                }
            }
            if (!found) ver = Versions[Versions.Length - 1];

            // 1. Encode data bits
            byte[] dataBytes = EncodeDataBits(textBytes, ver);

            // 2. Compute Reed-Solomon Error Correction
            byte[] finalCodewords = ComputeEccAndInterleave(dataBytes, ver);

            // 3. Construct Matrix
            int dim = ver.Dimension;
            int[,] matrix = new int[dim, dim]; // 0 = unassigned, 1 = black, -1 = white
            bool[,] isFunction = new bool[dim, dim];

            // Finder patterns
            AddFinderPattern(matrix, isFunction, 0, 0);
            AddFinderPattern(matrix, isFunction, dim - 7, 0);
            AddFinderPattern(matrix, isFunction, 0, dim - 7);

            // Timing patterns
            for (int i = 8; i < dim - 8; i++)
            {
                int val = (i % 2 == 0) ? 1 : -1;
                matrix[6, i] = val; isFunction[6, i] = true;
                matrix[i, 6] = val; isFunction[i, 6] = true;
            }

            // Alignment patterns
            if (ver.AlignmentPatternCoords.Length >= 2)
            {
                foreach (int r in ver.AlignmentPatternCoords)
                {
                    foreach (int c in ver.AlignmentPatternCoords)
                    {
                        if (isFunction[r, c]) continue;
                        AddAlignmentPattern(matrix, isFunction, r, c);
                    }
                }
            }

            // Dark module & reserve format info
            matrix[4 * ver.Version + 9, 8] = 1;
            isFunction[4 * ver.Version + 9, 8] = true;
            ReserveFormatInfo(isFunction, dim);

            // 4. Place Data Bits
            PlaceDataBits(matrix, isFunction, finalCodewords, dim);

            // 5. Select Best Mask (0..7) and Apply
            int bestMask = 0;
            int bestPenalty = int.MaxValue;
            int[,] bestMatrix = new int[dim, dim];

            for (int mask = 0; mask < 8; mask++)
            {
                int[,] testMatrix = (int[,])matrix.Clone();
                ApplyMask(testMatrix, isFunction, mask, dim);
                ApplyFormatInfo(testMatrix, mask, dim);

                int penalty = CalculatePenalty(testMatrix, dim);
                if (penalty < bestPenalty)
                {
                    bestPenalty = penalty;
                    bestMask = mask;
                    bestMatrix = testMatrix;
                }
            }

            // 6. Render to WPF BitmapSource
            return RenderToBitmap(bestMatrix, dim, pixelSize, quietZone);
        }

        #endregion

        #region ══ INTERNAL ENCODING & ALGORITHMS ══

        private static byte[] EncodeDataBits(byte[] textBytes, VersionInfo ver)
        {
            var bits = new List<bool>();

            // Mode indicator: 0100 (Byte mode)
            bits.Add(false); bits.Add(true); bits.Add(false); bits.Add(false);

            // Character count indicator: 8 bits
            int count = textBytes.Length;
            for (int i = 7; i >= 0; i--)
            {
                bits.Add(((count >> i) & 1) == 1);
            }

            // Data bytes
            foreach (byte b in textBytes)
            {
                for (int i = 7; i >= 0; i--)
                {
                    bits.Add(((b >> i) & 1) == 1);
                }
            }

            // Terminator: up to 4 zero bits
            int maxBits = ver.TotalDataBytes * 8;
            int term = Math.Min(4, maxBits - bits.Count);
            for (int i = 0; i < term; i++) bits.Add(false);

            // Pad to multiple of 8
            while (bits.Count % 8 != 0) bits.Add(false);

            // Convert to byte array
            var result = new List<byte>();
            for (int i = 0; i < bits.Count; i += 8)
            {
                byte val = 0;
                for (int b = 0; b < 8; b++)
                {
                    if (bits[i + b]) val |= (byte)(1 << (7 - b));
                }
                result.Add(val);
            }

            // Fill pad bytes (0xEC, 0x11)
            byte[] padBytes = { 0xEC, 0x11 };
            int padIndex = 0;
            while (result.Count < ver.TotalDataBytes)
            {
                result.Add(padBytes[padIndex % 2]);
                padIndex++;
            }

            return result.ToArray();
        }

        private static byte[] ComputeEccAndInterleave(byte[] data, VersionInfo ver)
        {
            int numBlocks = ver.NumBlocksGroup1 + ver.NumBlocksGroup2;
            byte[][] dataBlocks = new byte[numBlocks][];
            byte[][] eccBlocks = new byte[numBlocks][];

            int offset = 0;
            for (int b = 0; b < ver.NumBlocksGroup1; b++)
            {
                int len = ver.DataBytesGroup1;
                dataBlocks[b] = new byte[len];
                Array.Copy(data, offset, dataBlocks[b], 0, len);
                eccBlocks[b] = GenerateReedSolomon(dataBlocks[b], ver.EccBytesPerBlock);
                offset += len;
            }
            for (int b = 0; b < ver.NumBlocksGroup2; b++)
            {
                int idx = ver.NumBlocksGroup1 + b;
                int len = ver.DataBytesGroup2;
                dataBlocks[idx] = new byte[len];
                Array.Copy(data, offset, dataBlocks[idx], 0, len);
                eccBlocks[idx] = GenerateReedSolomon(dataBlocks[idx], ver.EccBytesPerBlock);
                offset += len;
            }

            // Interleave data codewords
            var final = new List<byte>();
            int maxDataLen = Math.Max(ver.DataBytesGroup1, ver.DataBytesGroup2);
            for (int i = 0; i < maxDataLen; i++)
            {
                for (int b = 0; b < numBlocks; b++)
                {
                    if (i < dataBlocks[b].Length)
                    {
                        final.Add(dataBlocks[b][i]);
                    }
                }
            }

            // Interleave ECC codewords
            for (int i = 0; i < ver.EccBytesPerBlock; i++)
            {
                for (int b = 0; b < numBlocks; b++)
                {
                    final.Add(eccBlocks[b][i]);
                }
            }

            return final.ToArray();
        }

        private static byte[] GenerateReedSolomon(byte[] data, int eccCount)
        {
            byte[] genPoly = BuildGeneratorPoly(eccCount);
            byte[] ecc = new byte[eccCount];

            foreach (byte b in data)
            {
                byte factor = (byte)(b ^ ecc[0]);
                for (int i = 0; i < eccCount - 1; i++)
                {
                    ecc[i] = (byte)(ecc[i + 1] ^ GfMultiply(genPoly[i], factor));
                }
                ecc[eccCount - 1] = GfMultiply(genPoly[eccCount - 1], factor);
            }

            return ecc;
        }

        private static byte[] BuildGeneratorPoly(int count)
        {
            byte[] poly = { 1 };
            for (int i = 0; i < count; i++)
            {
                byte[] term = { 1, Exp[i] };
                poly = MultiplyPolys(poly, term);
            }
            // Drop highest power coefficient (which is 1)
            byte[] result = new byte[count];
            Array.Copy(poly, 1, result, 0, count);
            return result;
        }

        private static byte[] MultiplyPolys(byte[] p1, byte[] p2)
        {
            byte[] result = new byte[p1.Length + p2.Length - 1];
            for (int i = 0; i < p1.Length; i++)
            {
                for (int j = 0; j < p2.Length; j++)
                {
                    result[i + j] ^= GfMultiply(p1[i], p2[j]);
                }
            }
            return result;
        }

        private static byte GfMultiply(byte a, byte b)
        {
            if (a == 0 || b == 0) return 0;
            return Exp[Log[a] + Log[b]];
        }

        #endregion

        #region ══ MATRIX PATTERNS & LAYOUT ══

        private static void AddFinderPattern(int[,] matrix, bool[,] isFunction, int row, int col)
        {
            for (int r = -1; r <= 7; r++)
            {
                for (int c = -1; c <= 7; c++)
                {
                    int mr = row + r;
                    int mc = col + c;
                    if (mr < 0 || mr >= matrix.GetLength(0) || mc < 0 || mc >= matrix.GetLength(1)) continue;

                    bool isBlack = (r >= 0 && r <= 6 && (c == 0 || c == 6)) ||
                                   (c >= 0 && c <= 6 && (r == 0 || r == 6)) ||
                                   (r >= 2 && r <= 4 && c >= 2 && c <= 4);

                    matrix[mr, mc] = isBlack ? 1 : -1;
                    isFunction[mr, mc] = true;
                }
            }
        }

        private static void AddAlignmentPattern(int[,] matrix, bool[,] isFunction, int centerRow, int centerCol)
        {
            for (int r = -2; r <= 2; r++)
            {
                for (int c = -2; c <= 2; c++)
                {
                    int mr = centerRow + r;
                    int mc = centerCol + c;
                    bool isBlack = Math.Abs(r) == 2 || Math.Abs(c) == 2 || (r == 0 && c == 0);
                    matrix[mr, mc] = isBlack ? 1 : -1;
                    isFunction[mr, mc] = true;
                }
            }
        }

        private static void ReserveFormatInfo(bool[,] isFunction, int dim)
        {
            for (int i = 0; i <= 8; i++)
            {
                isFunction[8, i] = true;
                isFunction[i, 8] = true;
            }
            for (int i = dim - 8; i < dim; i++)
            {
                isFunction[8, i] = true;
                isFunction[i, 8] = true;
            }
        }

        private static void PlaceDataBits(int[,] matrix, bool[,] isFunction, byte[] data, int dim)
        {
            int byteIdx = 0;
            int bitIdx = 7;
            bool upward = true;

            for (int right = dim - 1; right > 0; right -= 2)
            {
                if (right == 6) right--; // Skip vertical timing column

                for (int vert = 0; vert < dim; vert++)
                {
                    int r = upward ? (dim - 1 - vert) : vert;

                    for (int c = right; c >= right - 1; c--)
                    {
                        if (isFunction[r, c]) continue;

                        int bitVal = -1;
                        if (byteIdx < data.Length)
                        {
                            bitVal = ((data[byteIdx] >> bitIdx) & 1) == 1 ? 1 : -1;
                            bitIdx--;
                            if (bitIdx < 0)
                            {
                                bitIdx = 7;
                                byteIdx++;
                            }
                        }

                        matrix[r, c] = bitVal;
                    }
                }
                upward = !upward;
            }
        }

        private static void ApplyMask(int[,] matrix, bool[,] isFunction, int mask, int dim)
        {
            for (int r = 0; r < dim; r++)
            {
                for (int c = 0; c < dim; c++)
                {
                    if (isFunction[r, c]) continue;

                    bool invert = mask switch
                    {
                        0 => (r + c) % 2 == 0,
                        1 => r % 2 == 0,
                        2 => c % 3 == 0,
                        3 => (r + c) % 3 == 0,
                        4 => (r / 2 + c / 3) % 2 == 0,
                        5 => ((r * c) % 2) + ((r * c) % 3) == 0,
                        6 => (((r * c) % 2) + ((r * c) % 3)) % 2 == 0,
                        7 => (((r + c) % 2) + ((r * c) % 3)) % 2 == 0,
                        _ => false
                    };

                    if (invert)
                    {
                        matrix[r, c] = (matrix[r, c] == 1) ? -1 : 1;
                    }
                }
            }
        }

        private static void ApplyFormatInfo(int[,] matrix, int mask, int dim)
        {
            // ECC Level M = 00, Mask = mask (3 bits) -> 5 bits data
            int data = (0 << 3) | mask;
            int rem = data << 10;
            int generator = 0x537;

            for (int i = 4; i >= 0; i--)
            {
                if (((rem >> (i + 10)) & 1) == 1)
                {
                    rem ^= (generator << i);
                }
            }

            int formatBits = ((data << 10) | rem) ^ 0x5412;

            // Place format bits: 15 bits
            int[] r1 = { 8, 8, 8, 8, 8, 8, 8, 8, 7, 5, 4, 3, 2, 1, 0 };
            int[] c1 = { 0, 1, 2, 3, 4, 5, 7, 8, 8, 8, 8, 8, 8, 8, 8 };

            int[] r2 = { dim - 1, dim - 2, dim - 3, dim - 4, dim - 5, dim - 6, dim - 7, 8, 8, 8, 8, 8, 8, 8, 8 };
            int[] c2 = { 8, 8, 8, 8, 8, 8, 8, dim - 8, dim - 7, dim - 6, dim - 5, dim - 4, dim - 3, dim - 2, dim - 1 };

            for (int i = 0; i < 15; i++)
            {
                int val = ((formatBits >> i) & 1) == 1 ? 1 : -1;
                matrix[r1[i], c1[i]] = val;
                matrix[r2[i], c2[i]] = val;
            }
        }

        private static int CalculatePenalty(int[,] matrix, int dim)
        {
            int penalty = 0;

            // Rule 1: 5 or more consecutive identical modules
            for (int r = 0; r < dim; r++)
            {
                int count = 1;
                for (int c = 1; c < dim; c++)
                {
                    if (matrix[r, c] == matrix[r, c - 1]) count++;
                    else
                    {
                        if (count >= 5) penalty += 3 + (count - 5);
                        count = 1;
                    }
                }
                if (count >= 5) penalty += 3 + (count - 5);
            }

            for (int c = 0; c < dim; c++)
            {
                int count = 1;
                for (int r = 1; r < dim; r++)
                {
                    if (matrix[r, c] == matrix[r - 1, c]) count++;
                    else
                    {
                        if (count >= 5) penalty += 3 + (count - 5);
                        count = 1;
                    }
                }
                if (count >= 5) penalty += 3 + (count - 5);
            }

            // Rule 2: 2x2 blocks of same color
            for (int r = 0; r < dim - 1; r++)
            {
                for (int c = 0; c < dim - 1; c++)
                {
                    int val = matrix[r, c];
                    if (val == matrix[r + 1, c] && val == matrix[r, c + 1] && val == matrix[r + 1, c + 1])
                    {
                        penalty += 3;
                    }
                }
            }

            return penalty;
        }

        private static BitmapSource RenderToBitmap(int[,] matrix, int dim, int targetPixelSize, int quietZone)
        {
            int totalModules = dim + quietZone * 2;
            int scale = Math.Max(1, targetPixelSize / totalModules);
            int width = totalModules * scale;
            int height = width;

            int stride = (width * 32 + 31) / 32 * 4;
            byte[] pixels = new byte[height * stride];

            // Fill pure white background #FFFFFF
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;     // B
                pixels[i + 1] = 255; // G
                pixels[i + 2] = 255; // R
                pixels[i + 3] = 255; // A
            }

            // Draw dark modules #000000
            for (int r = 0; r < dim; r++)
            {
                for (int c = 0; c < dim; c++)
                {
                    if (matrix[r, c] == 1) // Black module
                    {
                        int startX = (c + quietZone) * scale;
                        int startY = (r + quietZone) * scale;

                        for (int py = 0; py < scale; py++)
                        {
                            int rowOffset = (startY + py) * stride;
                            for (int px = 0; px < scale; px++)
                            {
                                int pixelOffset = rowOffset + (startX + px) * 4;
                                pixels[pixelOffset] = 12;     // B
                                pixels[pixelOffset + 1] = 15; // G
                                pixels[pixelOffset + 2] = 20; // R
                                pixels[pixelOffset + 3] = 255;// A
                            }
                        }
                    }
                }
            }

            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            bitmap.Freeze();
            return bitmap;
        }

        #endregion
    }
}
