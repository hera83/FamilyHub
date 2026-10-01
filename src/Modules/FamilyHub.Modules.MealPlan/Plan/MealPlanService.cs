using FamilyHub.Core.Time;
using FamilyHub.Modules.MealPlan.Recipes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FamilyHub.Modules.MealPlan.Plan;

/// <summary>One dish of a day's dinner: a recipe from the recipe book, or the family's own text.</summary>
public sealed record PlannedDish(DateOnly Date, DinnerCourse Course, int? RecipeId, string Title)
{
    public bool IsFromRecipeBook => RecipeId is not null;
}

/// <summary>
/// The family's dinner plan – per day a main course and, if wanted, a starter and a dessert (only dinner; other meals
/// are not planned). Shared by every screen (singleton): <see cref="Changed"/> tells the others when something is
/// planned, moved or removed. Stored in SQLite (<see cref="MealPlanDbContext"/>); the database is created/upgraded on first use.
/// </summary>
public sealed class MealPlanService(IDbContextFactory<MealPlanDbContext> databases, IHubClock clock, ILogger<MealPlanService> logger)
{
    /// <summary>Max length of a dish typed by hand ("Rester", "Pizza ude").</summary>
    public const int MaxTitleLength = 100;

    // SQLite has one writer anyway; the gate also makes "move" (read both days, write both) atomic between screens.
    private readonly SemaphoreSlim gate = new(1, 1);
    private volatile bool ready;

    /// <summary>Raised after the plan changed. May come from any thread – use <c>InvokeAsync</c>.</summary>
    public event Action? Changed;

    /// <summary>
    /// The dishes in [<paramref name="from"/>, <paramref name="toExclusive"/>), by date and in menu order
    /// (starter, main course, dessert). Days without a plan are left out.
    /// </summary>
    public async Task<IReadOnlyDictionary<DateOnly, IReadOnlyList<PlannedDish>>> GetDishesAsync(DateOnly from, DateOnly toExclusive, CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseAsync(cancellationToken);
        await using var db = await databases.CreateDbContextAsync(cancellationToken);
        var rows = await db.Dinners.AsNoTracking()
            .Where(d => d.Date >= from && d.Date < toExclusive)
            .ToListAsync(cancellationToken);
        return rows
            .Select(ToModel)
            .GroupBy(d => d.Date)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PlannedDish>)[.. g.OrderBy(d => d.Course.Position())]);
    }

    /// <summary>Plans a recipe as the day's <paramref name="course"/> – replaces that course if it was planned.</summary>
    public Task<PlannedDish> PlanRecipeAsync(DateOnly date, DinnerCourse course, Recipe recipe, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return SaveAsync(new PlannedDish(date, course, recipe.Id, Limit(recipe.Title, RecipeDraft.MaxTitleLength)), cancellationToken);
    }

    /// <summary>Plans the family's own dish ("Rester", "Pizza ude") as the day's <paramref name="course"/> – replaces that course.</summary>
    public Task<PlannedDish> PlanTextAsync(DateOnly date, DinnerCourse course, string title, CancellationToken cancellationToken = default)
    {
        var text = CleanTitle(title) ?? throw new ArgumentException("Skriv hvad I skal have.", nameof(title));
        return SaveAsync(new PlannedDish(date, course, null, text), cancellationToken);
    }

    /// <summary>Removes one dish right away – the day's other courses stay. Returns it, for "Fortryd" via <see cref="RestoreAsync"/>.</summary>
    public async Task<PlannedDish?> RemoveAsync(DateOnly date, DinnerCourse course, CancellationToken cancellationToken = default)
    {
        PlannedDish? removed;
        await EnsureDatabaseAsync(cancellationToken);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await databases.CreateDbContextAsync(cancellationToken);
            var row = await db.Dinners.FindAsync([date, course], cancellationToken);
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

    /// <summary>"Fortryd": puts a removed dish back on its day.</summary>
    public Task<PlannedDish> RestoreAsync(PlannedDish dish, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dish);
        return SaveAsync(dish, cancellationToken);
    }

    /// <summary>
    /// Moves the whole dinner on <paramref name="from"/> (starter, main course and dessert) to <paramref name="to"/>.
    /// If <paramref name="to"/> already has dishes, the two days swap menus – so a changed schedule never loses a dish.
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
            var source = rows.Where(r => r.Date == from).Select(ToModel).ToList();
            var target = rows.Where(r => r.Date == to).Select(ToModel).ToList();
            if (source.Count == 0)
            {
                return;
            }

            // Date and course are the key: rows keep them and get the other day's content – one atomic save.
            var result = MealPlanRules.Move(source, target, from, to);
            var now = clock.Now;
            foreach (var date in new[] { from, to })
            {
                foreach (var course in DinnerCourses.MenuOrder)
                {
                    var row = rows.FirstOrDefault(r => r.Date == date && r.Course == course);
                    var wanted = result.FirstOrDefault(d => d.Date == date && d.Course == course);
                    if (wanted is null)
                    {
                        if (row is not null)
                        {
                            db.Dinners.Remove(row);
                        }
                    }
                    else if (row is null)
                    {
                        db.Dinners.Add(ToEntity(wanted, now));
                    }
                    else
                    {
                        row.RecipeId = wanted.RecipeId;
                        row.Title = wanted.Title;
                        row.UpdatedAt = now;
                    }
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

    private async Task<PlannedDish> SaveAsync(PlannedDish dish, CancellationToken cancellationToken)
    {
        await EnsureDatabaseAsync(cancellationToken);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await databases.CreateDbContextAsync(cancellationToken);
            var row = await db.Dinners.FindAsync([dish.Date, dish.Course], cancellationToken);
            if (row is null)
            {
                db.Dinners.Add(ToEntity(dish, clock.Now));
            }
            else
            {
                row.RecipeId = dish.RecipeId;
                row.Title = dish.Title;
                row.UpdatedAt = clock.Now;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }

        NotifyChanged();
        return dish;
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

    private static PlannedDish ToModel(DinnerEntity row) => new(row.Date, row.Course, row.RecipeId, row.Title);

    private static DinnerEntity ToEntity(PlannedDish dish, DateTimeOffset now) =>
        new() { Date = dish.Date, Course = dish.Course, RecipeId = dish.RecipeId, Title = dish.Title, UpdatedAt = now };

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

    /// <summary>Tonight's dinner shows on the home screen from this time in the morning …</summary>
    public static readonly TimeOnly TonightFrom = new(6, 0);

    /// <summary>… until this time, when the family has eaten.</summary>
    public static readonly TimeOnly TonightUntil = new(19, 0);

    /// <summary>The home screen shows today's dinner between 06:00 and 19:00.</summary>
    public static bool ShowTonight(TimeOnly now) => now >= TonightFrom && now < TonightUntil;

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
    /// Where the dishes end up when the dinner on <paramref name="from"/> is moved to <paramref name="to"/>: the whole menu
    /// goes to <paramref name="to"/>, and whatever was there goes to <paramref name="from"/> (the days swap).
    /// </summary>
    public static IReadOnlyList<PlannedDish> Move(IEnumerable<PlannedDish> source, IEnumerable<PlannedDish> target, DateOnly from, DateOnly to) =>
        [.. source.Select(d => d with { Date = to }), .. target.Select(d => d with { Date = from })];

    /// <summary>The courses not planned yet, in menu order. Empty = the day's menu is full.</summary>
    public static IReadOnlyList<DinnerCourse> FreeCourses(IEnumerable<PlannedDish> dishes)
    {
        var taken = dishes.Select(d => d.Course).ToHashSet();
        return [.. DinnerCourses.MenuOrder.Where(c => !taken.Contains(c))];
    }

    /// <summary>
    /// The course "Tilføj" suggests: the main course first, then dessert (far more common than a starter), then the starter.
    /// Null when all three are planned.
    /// </summary>
    public static DinnerCourse? NextCourse(IEnumerable<PlannedDish> dishes)
    {
        var free = FreeCourses(dishes);
        foreach (var course in new[] { DinnerCourse.Main, DinnerCourse.Dessert, DinnerCourse.Starter })
        {
            if (free.Contains(course))
            {
                return course;
            }
        }

        return null;
    }

    /// <summary>The dish that stands for the day (home screen): the main course, else the first one planned.</summary>
    public static PlannedDish? Headline(IReadOnlyList<PlannedDish> dishes) =>
        dishes.FirstOrDefault(d => d.Course == DinnerCourse.Main) ?? dishes.FirstOrDefault();

    /// <summary>
    /// The recipe book's category that fits a course – "Dessert" for desserts, a "Forret"/"Forretter" category for
    /// starters – so the picker starts there. Null (all recipes) for the main course, or when the book has no such category.
    /// </summary>
    public static int? CategoryFor(DinnerCourse course, IEnumerable<RecipeCategory> categories)
    {
        var word = course switch
        {
            DinnerCourse.Starter => "forret",
            DinnerCourse.Dessert => "dessert",
            _ => null,
        };

        return word is null
            ? null
            : categories.FirstOrDefault(c => c.RecipeCount > 0 && c.Name.Contains(word, StringComparison.OrdinalIgnoreCase))?.Id;
    }
}
