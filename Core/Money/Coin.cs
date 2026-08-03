using System.Text;

namespace Core;

/// <summary>
/// Renders a copper amount the way the game quotes it - <c>4g 50s 10c</c>.
/// </summary>
public static class Coin
{
    public const int COPPER_PER_GOLD = 10000;
    public const int COPPER_PER_SILVER = 100;

    // The purse is capped by int copper, so the longest possible string is
    // "214748g 36s 47c" - 15 chars. Rounded up to leave the builder room rather
    // than have it grow on the one value that sits right on the boundary.
    private const int MAX_FORMATTED_LENGTH = 32;

    /// <summary>
    /// Only the units that carry a value, so a clean gold drop reads "2g"
    /// rather than "2g 0s 0c". Zero is "0c" rather than an empty string.
    /// </summary>
    public static string Format(int copper)
    {
        if (copper == 0)
            return "0c";

        StringBuilder sb = new(MAX_FORMATTED_LENGTH);

        Append(sb, copper / COPPER_PER_GOLD, 'g');
        Append(sb, copper / COPPER_PER_SILVER % COPPER_PER_SILVER, 's');
        Append(sb, copper % COPPER_PER_SILVER, 'c');

        return sb.ToString();

        static void Append(StringBuilder sb, int value, char unit)
        {
            if (value == 0)
            {
                return;
            }

            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(value).Append(unit);
        }
    }
}
