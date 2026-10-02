namespace FamilyHub.Modules.MealPlan.Shopping;

/// <summary>A number of packs of one size, e.g. 2 × 400 g.</summary>
public readonly record struct PackCount(double Size, int Count)
{
    public double Total => Size * Count;
}

/// <summary>
/// Which packs to buy for a need – milk comes in litres, butter in 250 g, minced beef in 400 g, 500 g or 800 g.
/// Pure functions – unit-tested.
/// </summary>
public static class PackPlanner
{
    /// <summary>
    /// A little less than the recipes say is fine: up to 5 % of the need, and at most a tenth of the smallest pack –
    /// so 1020 g flour is one 1 kg bag, not two, but 1,3 kg minced beef is never just 3 × 400 g.
    /// </summary>
    public const double Tolerance = 0.05;

    /// <summary>Never suggest more packs than this – beyond it the list just says the total.</summary>
    public const int MaxPacks = 30;

    /// <summary>
    /// The packs that cover <paramref name="need"/> (in base units) best: as few packs as possible and as little left over
    /// as possible, weighed against each other – so 1,3 kg minced beef with packs of 350 g, 500 g, 800 g and 1,2 kg becomes
    /// 800 g + 500 g, and 400 g with packs of 350 g and 1,2 kg becomes 2 × 350 g rather than one big pack.
    /// Largest packs first. Empty when there is no need or no sizes.
    /// </summary>
    public static IReadOnlyList<PackCount> Plan(double need, IEnumerable<double> sizes)
    {
        var options = sizes.Where(s => s > 0 && double.IsFinite(s)).Distinct().OrderDescending().ToArray();
        if (need <= 0 || !double.IsFinite(need) || options.Length == 0)
        {
            return [];
        }

        var smallest = options[^1];
        var enough = need - Math.Min(need * Tolerance, smallest / 10);
        if (Math.Ceiling(enough / smallest) > MaxPacks)
        {
            // Far more than packs make sense for (a party) – the largest packs, rounded up.
            return [new PackCount(options[0], (int)Math.Ceiling(enough / options[0]))];
        }

        var counts = new int[options.Length];
        int[]? best = null;
        var bestScore = double.MaxValue;

        void Search(int index, double total, int packs)
        {
            if (total >= enough)
            {
                // A pack counts as 1; what is left over counts as a share of the smallest pack.
                var score = packs + Math.Max(0, total - need) / smallest;
                if (score < bestScore - 1e-9)
                {
                    bestScore = score;
                    best = (int[])counts.Clone();
                }

                return;
            }

            if (index == options.Length || packs >= MaxPacks)
            {
                return;
            }

            // Try 0, 1, 2 … of this size, and fill up with the smaller sizes.
            var size = options[index];
            for (var count = 0; packs + count <= MaxPacks; count++)
            {
                counts[index] = count;
                var sum = total + count * size;
                Search(index + 1, sum, packs + count);
                if (sum >= enough)
                {
                    break; // more of this size only adds left-overs
                }
            }

            counts[index] = 0;
        }

        Search(0, 0, 0);
        return best is null
            ? []
            : [.. options.Select((size, i) => new PackCount(size, best[i])).Where(p => p.Count > 0)];
    }

    /// <summary>
    /// The packs the way a shopping list says them: one size of exactly 1 l, 1 kg or 1 stk → the total ("2 l"),
    /// otherwise each size ("2 × 250 g", "800 g + 500 g", "1 l + ½ l").
    /// </summary>
    public static string Format(IReadOnlyList<PackCount> packs, Measure measure)
    {
        if (packs.Count == 0)
        {
            return "";
        }

        if (packs.Count == 1 && IsWholeUnit(packs[0].Size, measure))
        {
            return ShoppingUnits.Format(new Measured(packs[0].Total, measure), measure == Measure.Volume ? "l" : null);
        }

        return string.Join(" + ", packs.Select(p => p.Count == 1
            ? ShoppingUnits.FormatPack(p.Size, measure)
            : $"{p.Count} × {ShoppingUnits.FormatPack(p.Size, measure)}"));
    }

    private static bool IsWholeUnit(double size, Measure measure) => measure switch
    {
        Measure.Volume or Measure.Mass => Math.Abs(size - 1000) < 1e-9,
        _ => Math.Abs(size - 1) < 1e-9,
    };
}
