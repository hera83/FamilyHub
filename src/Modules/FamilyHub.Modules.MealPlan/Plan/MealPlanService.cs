using FamilyHub.Core.Time;
using FamilyHub.Modules.MealPlan.Recipes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FamilyHub.Modules.MealPlan.Plan;

/// <summary>The dinner planned for one day: a recipe from the recipe book, or the family's own text.</summary>
public sealed record PlannedDinner(DateOnly Date, int? RecipeId, string Title)
{
    public bool IsFromRecipeBook => RecipeId is not null;
}

/// <summary>
/// The family's dinner plan – one dinner per day (only dinner; other meals are not planned). Shared by every screen
/// (singleton): <see cref="Changed"/> tells the others when something is planned, moved or removed.
/// Stored in SQLite (<see cref="MealPlanDbContext"/>); the database is created/upgraded on first use.
/// </summary>
public sealed class MealPlanService(IDbContextFactory<MealPlanDbContext> databases, IHubClock clock, ILogger<MealPlanService> logger)
{
    /// <summary>Max length of a dinner typed by hand ("Rester", "Pizza ude").</summary>
    public const int MaxTitleLength = 100;

    // SQLite has one writer anyway; the gate also makes "move" (read both days, write both) atomic between screens.
    private readonly SemaphoreSlim gate = new(1, 1);
    private volatile bool ready;

    /// <summary>Raised after the plan changed. May come from any thread – use <c>InvokeAsync</c>.</summary>
    public event Action? Changed;

    /// <summary>The dinners in [<paramref name="from"/>, <paramref name="toExclusive"/>), by date. Days without a plan are left out.</summary>
    public async Task<IReadOnlyDictionary<DateOnly, PlannedDinner>> GetDinnersAsync(DateOnly from, DateOnly toExclusive, CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseAsync(cancellationToken);
        await using var db = await databases.CreateDbContextAsync(cancellationToken);
        var rows = await db.Dinners.AsNoTracking()
            .Where(d => d.Date >= from && d.Date < toExclusive)
            .ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToDictionary(d => d.Date);
    }

    /// <summary>Plans a recipe for the day – replaces what was there.</summary>
    public Task<PlannedDinner> PlanRecipeAsync(DateOnly date, Recipe recipe, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return SaveAsync(new PlannedDinner(date, recipe.Id, Limit(recipe.Title, RecipeDraft.MaxTitleLength)), cancellationToken);
    }

    /// <summary>Plans the family's own dinner ("Rester", "Pizza ude") – replaces what was there.</summary>
    public Task<PlannedDinner> PlanTextAsync(DateOnly date, string title, CancellationToken cancellationToken = default)
    {
        var text = CleanTitle(title) ?? throw new ArgumentException("Skriv hvad I skal have.", nameof(title));
        return SaveAsync(new PlannedDinner(date, null, text), cancellationToken);
    }

    /// <summary>Removes the day's dinner right away. Returns it, for "Fortryd" via <see cref="RestoreAsync"/>.</summary>
    public async Task<PlannedDinner?> RemoveAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        PlannedDinner? removed;
        await EnsureDatabaseAsync(cancellationToken);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await databases.CreateDbContextAsync(cancellationToken);
            var row = await db.Dinners.FindAsync([date], cancellationToken);
            if (row is null)
            {
                return null;
            }

            removed = ToModel(row);
            db.Dinners.Remove(row);
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }

        NotifyChanged();
        return removed;
    }

    /// <summary>"Fortryd": puts a removed dinner back on its day.</summary>
    public Task<PlannedDinner> RestoreAsync(PlannedDinner dinner, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dinner);
        return SaveAsync(dinner, cancellationToken);
    }

    /// <summary>
    /// Moves the dinner on <paramref name="from"/> to <paramref name="to"/>. If <paramref name="to"/> already has a dinner,
    /// the two swap places – so a changed schedule never loses a dish.
    /// </summary>
    public async Task MoveAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (from == to)
        {
            return;
        }

        await EnsureDatabaseAsync(cancellationToken);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await databases.CreateDbContextAsync(cancellationToken);
            var rows = await db.Dinners.Where(d => d.Date == from || d.Date == to).ToListAsync(cancellationToken);
            var source = rows.FirstOrDefault(r => r.Date == from);
            var target = rows.FirstOrDefault(r => r.Date == to);
            if (source is null)
            {
                return;
            }

            // The date is the key: rows keep their date and get the other day's content – one atomic save.
            var result = MealPlanRules.Move(ToModel(source), target is null ? null : ToModel(target), from, to);
            var now = clock.Now;
            foreach (var date in new[] { from, to })
            {
                var row = rows.FirstOrDefault(r => r.Date == date);
                var wanted = result.Where(r => r.Date == date).Select(r => r.Dinner).FirstOrDefault();
                if (wanted is null)
                {
                    db.Dinners.Remove(row!);
                }
                else if (row is null)
                {
                    db.Dinners.Add(ToEntity(wanted with { Date = date }, now));
                }
                else
                {
                    row.RecipeId = wanted.RecipeId;
                    row.Title = wanted.Title;
                    row.UpdatedAt = now;
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }

        NotifyChanged();
    }

    /// <summary>Creates or upgrades the database. Called on first use; safe to call again.</summary>
    public async Task EnsureDatabaseAsync(CancellationToken cancellationToken = default)
    {
        if (ready)
        {
            return;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!ready)
            {
                await using var db = await databases.CreateDbContextAsync(cancellationToken);
                await db.Database.MigrateAsync(cancellationToken);
                ready = true;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Trimmed, single spaces, capital first letter, at most <see cref="MaxTitleLength"/> characters. Null when nothing is left.</summary>
    public static string? CleanTitle(string? title)
    {
        var text = string.Join(' ', (title ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length == 0 ? null : DanishFormat.Capitalize(Limit(text, MaxTitleLength));
    }

    private async Task<PlannedDinner> SaveAsync(PlannedDinner dinner, CancellationToken cancellationToken)
    {
        await EnsureDatabaseAsync(cancellationToken);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await databases.CreateDbContextAsync(cancellationToken);
            var row = await db.Dinners.FindAsync([dinner.Date], cancellationToken);
            if (row is null)
            {
                db.Dinners.Add(ToEntity(dinner, clock.Now));
            }
            else
            {
                row.RecipeId = dinner.RecipeId;
                row.Title = dinner.Title;
                row.UpdatedAt = clock.Now;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }

        NotifyChanged();
        return dinner;
    }

    private void NotifyChanged()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "A MealPlanService.Changed handler failed");
        }
    }

    private static PlannedDinner ToModel(DinnerEntity row) => new(row.Date, row.RecipeId, row.Title);

    private static DinnerEntity ToEntity(PlannedDinner dinner, DateTimeOffset now) =>
        new() { Date = dinner.Date, RecipeId = dinner.RecipeId, Title = dinner.Title, UpdatedAt = now };

    private static string Limit(string text, int max) => text.Length <= max ? text : text[..max].TrimEnd();
}

/// <summary>The meal plan's rules as pure functions – unit-tested.</summary>
public static class MealPlanRules
{
    /// <summary>Monday of the (Danish, ISO) week containing <paramref name="date"/>.</summary>
    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>Monday to Sunday of the week containing <paramref name="date"/>.</summary>
    public static IReadOnlyList<DateOnly> WeekDays(DateOnly date)
    {
        var monday = WeekStart(date);
        return [.. Enumerable.Range(0, 7).Select(monday.AddDays)];
    }

    /// <summary>"Uge 40".</summary>
    public static string WeekTitle(DateOnly date) => $"Uge {DanishFormat.WeekNumber(date)}";

    /// <summary>"Denne uge · 28. september – 4. oktober", "Næste uge · …" or just the dates (with the year if it isn't this year).</summary>
    public static string WeekDetail(DateOnly date, DateOnly today)
    {
        var days = WeekDays(date);
        var range = DanishFormat.DayRange(days[0], days[6]);
        if (days[0].Year != today.Year || days[6].Year != today.Year)
        {
            range = days[0].Year == days[6].Year ? $"{range} {days[6].Year}" : range;
        }

        var weeks = (days[0].DayNumber - WeekStart(today).DayNumber) / 7;
        return weeks switch
        {
            0 => $"Denne uge · {range}",
            1 => $"Næste uge · {range}",
            -1 => $"Sidste uge · {range}",
            _ => range,
        };
    }

    /// <summary>
    /// Where the dinners end up when the one on <paramref name="from"/> is moved to <paramref name="to"/>:
    /// the source goes to <paramref name="to"/>, and whatever was there goes to <paramref name="from"/> (a swap).
    /// </summary>
    public static IReadOnlyList<(PlannedDinner Dinner, DateOnly Date)> Move(PlannedDinner source, PlannedDinner? target, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(source);
        return target is null ? [(source, to)] : [(source, to), (target, from)];
    }
}
