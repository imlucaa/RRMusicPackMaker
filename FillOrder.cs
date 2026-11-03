namespace RRMusicPackMaker;

/// <summary>How a batch of songs is handed out to tracks.</summary>
public enum FillOrder { Shuffled, InOrder }

/// <summary>Sorts file names the way a person would, so 2 comes before 10.</summary>
internal sealed class NaturalNames : IComparer<string?>
{
    public static readonly NaturalNames Instance = new();

    public int Compare(string? a, string? b)
    {
        a ??= "";
        b ??= "";
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int startA = i, startB = j;
                while (i < a.Length && char.IsDigit(a[i]))
                    i++;
                while (j < b.Length && char.IsDigit(b[j]))
                    j++;
                string x = a[startA..i].TrimStart('0'), y = b[startB..j].TrimStart('0');
                int byLength = x.Length.CompareTo(y.Length);
                int result = byLength != 0 ? byLength : string.CompareOrdinal(x, y);
                if (result != 0)
                    return result;
            }
            else
            {
                int result = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                if (result != 0)
                    return result;
                i++;
                j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }
}
