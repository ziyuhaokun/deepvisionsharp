namespace DeepVisionSharp.Application.Galleries;

public static class GallerySampleSelection
{
    public static IReadOnlyList<string> SelectByMoveRatio(
        IReadOnlyList<string> orderedFileNames,
        double moveRatio,
        int? randomSeed,
        bool preserveFileOrder)
    {
        if (orderedFileNames.Count < 2)
        {
            return [];
        }

        var working = orderedFileNames.ToList();
        if (!preserveFileOrder)
        {
            Shuffle(working, randomSeed ?? Random.Shared.Next());
        }

        var moveCount = CalculateMoveCount(working.Count, moveRatio);
        return working.Take(moveCount).ToArray();
    }

    public static IReadOnlyList<string> SelectByMoveCount(
        IReadOnlyList<string> orderedFileNames,
        int moveCount,
        int? randomSeed,
        bool preserveFileOrder)
    {
        if (orderedFileNames.Count < 2)
        {
            return [];
        }

        var clampedCount = Math.Clamp(moveCount, 1, orderedFileNames.Count - 1);
        var working = orderedFileNames.ToList();
        if (!preserveFileOrder)
        {
            Shuffle(working, randomSeed ?? Random.Shared.Next());
        }

        return working.Take(clampedCount).ToArray();
    }

    public static int CalculateMoveCount(int totalCount, double moveRatio)
    {
        if (totalCount < 2)
        {
            return 0;
        }

        var ratioClamped = Math.Clamp(moveRatio, 0.05, 0.95);
        var moveCount = (int)Math.Round(totalCount * ratioClamped);
        return Math.Clamp(moveCount, 1, totalCount - 1);
    }

    private static void Shuffle(IList<string> items, int seed)
    {
        var random = new Random(seed);
        for (var index = items.Count - 1; index > 0; index--)
        {
            var swapIndex = random.Next(index + 1);
            (items[index], items[swapIndex]) = (items[swapIndex], items[index]);
        }
    }
}
