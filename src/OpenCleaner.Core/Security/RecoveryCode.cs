using System.Security.Cryptography;

namespace OpenCleaner.Core.Security;

internal static class RecoveryCode
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Generate() => Format(RandomNumberGenerator.GetBytes(32));

    public static string Format(ReadOnlySpan<byte> data)
    {
        string encoded = Encode(data);
        var groups = new List<string>((encoded.Length + 3) / 4);
        for (int i = 0; i < encoded.Length; i += 4)
            groups.Add(encoded.Substring(i, Math.Min(4, encoded.Length - i)));
        return string.Join('-', groups);
    }

    public static string Normalize(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var builder = new System.Text.StringBuilder(code.Length);
        foreach (char raw in code)
        {
            if (raw is '-' or ' ' or '\t' or '\r' or '\n') continue;
            char c = char.ToUpperInvariant(raw);
            if (c is 'O') c = '0';
            if (c is 'I' or 'L') c = '1';
            if (!Alphabet.Contains(c)) throw new FormatException("Invalid recovery code character");
            builder.Append(c);
        }
        if (builder.Length == 0) throw new FormatException("The recovery code is empty");
        return builder.ToString();
    }

    private static string Encode(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return string.Empty;
        var result = new System.Text.StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0;
        int bits = 0;
        foreach (byte value in data)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                result.Append(Alphabet[(buffer >> bits) & 0x1F]);
            }
        }
        if (bits > 0) result.Append(Alphabet[(buffer << (5 - bits)) & 0x1F]);
        return result.ToString();
    }
}
