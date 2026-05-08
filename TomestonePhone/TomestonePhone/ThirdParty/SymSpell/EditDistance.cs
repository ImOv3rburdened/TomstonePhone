using System;

public class EditDistance
{
    public enum DistanceAlgorithm
    {
        Levenshtein,
        DamerauOSA,
    }

    private readonly DistanceAlgorithm algorithm;

    public EditDistance(DistanceAlgorithm algorithm)
    {
        this.algorithm = algorithm;
    }

    public int Compare(string string1, string string2, int maxDistance)
    {
        ArgumentNullException.ThrowIfNull(string1);
        ArgumentNullException.ThrowIfNull(string2);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDistance);

        if (Math.Abs(string1.Length - string2.Length) > maxDistance)
        {
            return -1;
        }

        return this.algorithm == DistanceAlgorithm.DamerauOSA
            ? CompareDamerauOsa(string1, string2, maxDistance)
            : CompareLevenshtein(string1, string2, maxDistance);
    }

    private static int CompareLevenshtein(string source, string target, int maxDistance)
    {
        var matrix = CreateMatrix(source.Length, target.Length);
        for (var row = 0; row <= source.Length; row++)
        {
            matrix[row, 0] = row;
        }

        for (var column = 0; column <= target.Length; column++)
        {
            matrix[0, column] = column;
        }

        for (var row = 1; row <= source.Length; row++)
        {
            var rowMinimum = int.MaxValue;
            for (var column = 1; column <= target.Length; column++)
            {
                var cost = source[row - 1] == target[column - 1] ? 0 : 1;
                matrix[row, column] = Math.Min(
                    Math.Min(matrix[row - 1, column] + 1, matrix[row, column - 1] + 1),
                    matrix[row - 1, column - 1] + cost);
                rowMinimum = Math.Min(rowMinimum, matrix[row, column]);
            }

            if (rowMinimum > maxDistance)
            {
                return -1;
            }
        }

        var distance = matrix[source.Length, target.Length];
        return distance <= maxDistance ? distance : -1;
    }

    private static int CompareDamerauOsa(string source, string target, int maxDistance)
    {
        var matrix = CreateMatrix(source.Length, target.Length);
        for (var row = 0; row <= source.Length; row++)
        {
            matrix[row, 0] = row;
        }

        for (var column = 0; column <= target.Length; column++)
        {
            matrix[0, column] = column;
        }

        for (var row = 1; row <= source.Length; row++)
        {
            var rowMinimum = int.MaxValue;
            for (var column = 1; column <= target.Length; column++)
            {
                var cost = source[row - 1] == target[column - 1] ? 0 : 1;
                var value = Math.Min(
                    Math.Min(matrix[row - 1, column] + 1, matrix[row, column - 1] + 1),
                    matrix[row - 1, column - 1] + cost);

                if (row > 1 &&
                    column > 1 &&
                    source[row - 1] == target[column - 2] &&
                    source[row - 2] == target[column - 1])
                {
                    value = Math.Min(value, matrix[row - 2, column - 2] + 1);
                }

                matrix[row, column] = value;
                rowMinimum = Math.Min(rowMinimum, value);
            }

            if (rowMinimum > maxDistance)
            {
                return -1;
            }
        }

        var distance = matrix[source.Length, target.Length];
        return distance <= maxDistance ? distance : -1;
    }

    private static int[,] CreateMatrix(int sourceLength, int targetLength)
    {
        return new int[sourceLength + 1, targetLength + 1];
    }
}
