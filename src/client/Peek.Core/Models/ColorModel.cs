namespace Peek.Core.Models;

public readonly record struct ColorModel(byte A, byte R, byte G, byte B)
{
    public static readonly ColorModel Transparent = new(0, 0, 0, 0);
    public static readonly ColorModel Black = new(255, 0, 0, 0);
    public static readonly ColorModel White = new(255, 255, 255, 255);

    public static ColorModel Parse(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            throw new ArgumentNullException(nameof(hex));

        hex = hex.Trim().TrimStart('#');

        return hex.Length switch
        {
            6 => new ColorModel(
                255,
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16)
            ),

            8 => new ColorModel(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16),
                Convert.ToByte(hex[6..8], 16)
            ),

            _ => throw new FormatException(
                "Color must be in RRGGBB or AARRGGBB format.")
        };
    }

    public static bool TryParse(string? hex, out ColorModel color)
    {
        try
        {
            color = Parse(hex ?? string.Empty);
            return true;
        }
        catch
        {
            color = default;
            return false;
        }
    }

    public string ToHex()
        => $"#{R:X2}{G:X2}{B:X2}";

    public string ToHexWithAlpha()
        => $"#{A:X2}{R:X2}{G:X2}{B:X2}";

    public bool EqualsHex(string hex)
    {
        var other = Parse(hex);
        return this == other;
    }

    public override string ToString()
        => A == 255
            ? ToHex()
            : ToHexWithAlpha();

    public static bool operator ==(ColorModel color, string hex)
        => color.EqualsHex(hex);

    public static bool operator !=(ColorModel color, string hex)
        => !color.EqualsHex(hex);

    public static implicit operator string(ColorModel color)
        => color.ToString();
}