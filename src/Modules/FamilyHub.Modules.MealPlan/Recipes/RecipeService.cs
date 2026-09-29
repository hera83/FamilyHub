using FamilyHub.Core.Storage;
using FamilyHub.Core.Time;
using Microsoft.Extensions.Logging;

namespace FamilyHub.Modules.MealPlan.Recipes;

/// <summary>How fresh the local copy of the recipe book is – shown with an InfoBox, never with toasts.</summary>
public sealed record RecipeSyncStatus
{
    public static RecipeSyncStatus Never { get; } = new();

    public DateTimeOffset? LastSuccess { get; init; }

    public DateTimeOffset? LastAttempt { get; init; }

    /// <summary>Why the last refresh failed. Null = it worked (or has not been tried).</summary>
    public RecipeApiError? Problem { get; init; }

    public bool IsRefreshing { get; init; }
}

/// <summary>The local copy of the recipe book, stored in <c>madplan/opskrifter.json</c>.</summary>
internal sealed record RecipeSnapshot
{
    public static RecipeSnapshot Empty { get; } = new();

    public IReadOnlyList<Recipe> Recipes { get; init; } = [];

    public RecipeLookups Lookups { get; init; } = RecipeLookups.Empty;

    public DateTimeOffset? LastSuccess { get; init; }
}

/// <summary>
/// The family's recipe book, shared by every screen (singleton). Keeps a copy of all recipes and lookups
/// in memory and on disk, so the kitchen screen shows recipes instantly – also after a restart without internet.
/// Reads come from the copy; writes go straight to the recipe book and update the copy.
/// <see cref="Changed"/> tells the screens. See docs/opskrifter-api.md.
/// </summary>
public sealed class RecipeService
{
    /// <summary><see cref="EnsureFreshAsync"/> fetches again when the copy is older than this.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(15);

    private static readonly StringComparer TitleOrder = StringComparer.Create(DanishFormat.Culture, ignoreCase: true);

    private readonly RecipeApiClient api;
    private readonly IHubClock clock;
    private readonly ILogger<RecipeService> logger;
    private readonly JsonFileStore<RecipeSnapshot> store;

    // One call to the recipe book at a time, so a refresh never overwrites a write that finished meanwhile.
    private readonly SemaphoreSlim gate = new(1, 1);

    private volatile RecipeSnapshot snapshot;
    private volatile RecipeSyncStatus status;

    public RecipeService(RecipeApiClient api, IAppDataPaths paths, IHubClock clock, ILogger<RecipeService> logger)
    {
        this.api = api;
        this.clock = clock;
        this.logger = logger;
        store = new JsonFileStore<RecipeSnapshot>(paths.GetFilePath("madplan/opskrifter.json"), logger);
        snapshot = store.Load() ?? RecipeSnapshot.Empty;
        status = RecipeSyncStatus.Never with { LastSuccess = snapshot.LastSuccess };
    }

    /// <summary>Raised after anything changed (recipes, categories, status). May come from any thread – use <c>InvokeAsync</c>.</summary>
    public event Action? Changed;

    /// <summary>The API key is in place. If not, show an InfoBox pointing to docs/opskrifter-api.md.</summary>
    public bool IsConfigured => api.IsConfigured;

    public RecipeSyncStatus Status => status;

    /// <summary>The book has been fetched at least once (now or before a restart).</summary>
    public bool HasData => snapshot.LastSuccess is not null;

    /// <summary>Every recipe, A–Å.</summary>
    public IReadOnlyList<Recipe> Recipes => snapshot.Recipes;

    /// <summary>Categories A–Å with recipe counts.</summary>
    public IReadOnlyList<RecipeCategory> Categories => snapshot.Lookups.Categories;

    /// <summary>Difficulties, categories, units and ingredient names – for pickers and suggestions.</summary>
    public RecipeLookups Lookups => snapshot.Lookups;

    public Recipe? Find(int id) => snapshot.Recipes.FirstOrDefault(r => r.Id == id);

    public RecipeCategory? FindCategory(int id) => snapshot.Lookups.Categories.FirstOrDefault(c => c.Id == id);

    /// <summary>Searches the local copy (instant, offline). Same filters as the API; every match, no pages.</summary>
    public IReadOnlyList<Recipe> Search(RecipeQuery query) => RecipeSearch.Apply(snapshot.Recipes, query);

    // ------------------------------------------------------------------ refresh

    /// <summary>Fetches the book if the copy is older than <see cref="MaxAge"/>. Call when a page opens. False = the copy may be old.</summary>
    public Task<bool> EnsureFreshAsync(CancellationToken cancellationToken = default) => RefreshAsync(force: false, cancellationToken);

    /// <summary>Fetches the whole book now ("Opdater"). False if it failed – <see cref="Status"/> says why; the old copy stays.</summary>
    public Task<bool> RefreshAsync(CancellationToken cancellationToken = default) => RefreshAsync(force: true, cancellationToken);

    private async Task<bool> RefreshAsync(bool force, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!force && IsFresh())
            {
                return true;
            }

            if (!api.IsConfigured)
            {
                SetStatus(status with { Problem = RecipeApiError.NotConfigured });
                return false;
            }

            SetStatus(status with { IsRefreshing = true, LastAttempt = clock.Now });
            try
            {
                var lookups = await api.GetLookupsAsync(cancellationToken);
                var recipes = await api.GetAllRecipesAsync(cancellationToken: cancellationToken);
                await SaveAsync(new RecipeSnapshot { Recipes = Sorted(recipes), Lookups = SortedLookups(lookups), LastSuccess = clock.Now });
                SetStatus(new RecipeSyncStatus { LastSuccess = snapshot.LastSuccess, LastAttempt = status.LastAttempt });
                return true;
            }
            catch (RecipeApiException ex)
            {
                logger.LogWarning(ex, "Could not refresh the recipe book ({Problem})", ex.Error);
                SetStatus(status with { IsRefreshing = false, Problem = ex.Error });
                return false;
            }
            catch (OperationCanceledException)
            {
                SetStatus(status with { IsRefreshing = false });
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Fetches one recipe again (e.g. when its page opens). Null if it has been deleted – it then leaves the copy too.</summary>
    public Task<Recipe?> ReloadRecipeAsync(int id, CancellationToken cancellationToken = default) =>
        WriteAsync(async () =>
        {
            var recipe = await api.GetRecipeAsync(id, cancellationToken);
            return (recipe, recipe is null ? Without(snapshot, id) : WithRecipe(snapshot, recipe));
        });

    // ------------------------------------------------------------------ recipes

    public Task<Recipe> CreateRecipeAsync(RecipeDraft draft, CancellationToken cancellationToken = default) =>
        WriteAsync(async () =>
        {
            var recipe = await api.CreateRecipeAsync(draft, cancellationToken);
            return (recipe, await WithFreshLookupsAsync(WithRecipe(snapshot, recipe), cancellationToken));
        });

    /// <summary>Replaces the recipe's fields, ingredients and steps. Start from <see cref="RecipeDraft.From"/>.</summary>
    public Task<Recipe> UpdateRecipeAsync(int id, RecipeDraft draft, CancellationToken cancellationToken = default) =>
        WriteAsync(async () =>
        {
            var recipe = await api.UpdateRecipeAsync(id, draft, cancellationToken);
            return (recipe, await WithFreshLookupsAsync(WithRecipe(snapshot, recipe), cancellationToken));
        });

    /// <summary>
    /// Deletes the recipe right away (with its photo). Returns what was deleted, for "Fortryd" via
    /// <see cref="RestoreRecipeAsync"/>. Already gone counts as deleted.
    /// </summary>
    public Task<Recipe?> DeleteRecipeAsync(int id, CancellationToken cancellationToken = default) =>
        WriteAsync(async () =>
        {
            var deleted = Find(id);
            try
            {
                await api.DeleteRecipeAsync(id, cancellationToken);
            }
            catch (RecipeApiException ex) when (ex.Error == RecipeApiError.NotFound)
            {
                // Someone else deleted it first – same result.
            }

            return (deleted, await WithFreshLookupsAsync(Without(snapshot, id), cancellationToken));
        });

    /// <summary>
    /// "Fortryd" after <see cref="DeleteRecipeAsync"/>: creates the recipe again from the deleted copy.
    /// It gets a new id, and the photo is gone (the recipe book deletes the file).
    /// </summary>
    public Task<Recipe> RestoreRecipeAsync(Recipe deleted, CancellationToken cancellationToken = default) =>
        CreateRecipeAsync(RecipeDraft.From(deleted), cancellationToken);

    /// <summary>Uploads or replaces the photo (jpg, png, webp, gif; max 10 MB – see <see cref="RecipeImages"/>).</summary>
    public Task<Recipe> SetRecipeImageAsync(int id, Stream image, string fileName, CancellationToken cancellationToken = default) =>
        WriteAsync(async () =>
        {
            var recipe = await api.SetRecipeImageAsync(id, image, fileName, cancellationToken);
            return (recipe, WithRecipe(snapshot, recipe));
        });

    public Task RemoveRecipeImageAsync(int id, CancellationToken cancellationToken = default) =>
        WriteAsync(async () =>
        {
            await api.RemoveRecipeImageAsync(id, cancellationToken);
            var next = Find(id) is { } recipe ? WithRecipe(snapshot, recipe with { ImageUrl = null }) : snapshot;
            return (true, next);
        });

    // ------------------------------------------------------------------ categories

    public Task<RecipeCategory> CreateCategoryAsync(RecipeCategoryDraft draft, CancellationToken cancellationToken = default) =>
        WriteAsync(async () =>
        {
            var category = await api.CreateCategoryAsync(draft, cancellationToken);
            return (category, await WithFreshLookupsAsync(snapshot, cancellationToken));
        });

    /// <summary>Renames the category and/or changes its icon – every recipe in it follows.</summary>
    public Task<RecipeCategory> UpdateCategoryAsync(int id, RecipeCategoryDraft draft, CancellationToken cancellationToken = default) =>
        WriteAsync(async () =>
        {
            var category = await api.UpdateCategoryAsync(id, draft, cancellationToken);
            var recipes = snapshot.Recipes.Select(r => r.CategoryId == id ? r with { Category = category.Name, CategoryIcon = category.Icon } : r);
            return (category, await WithFreshLookupsAsync(snapshot with { Recipes = Sorted(recipes) }, cancellationToken));
        });

    /// <summary>Deletes the category. Its recipes are kept, without a category. Returns what was deleted (for "Fortryd").</summary>
    public Task<RecipeCategory?> DeleteCategoryAsync(int id, CancellationToken cancellationToken = default) =>
        WriteAsync(async () =>
        {
            var deleted = FindCategory(id);
            try
            {
                await api.DeleteCategoryAsync(id, cancellationToken);
            }
            catch (RecipeApiException ex) when (ex.Error == RecipeApiError.NotFound)
            {
            }

            var recipes = snapshot.Recipes.Select(r => r.CategoryId == id ? r with { CategoryId = null, Category = "", CategoryIcon = "" } : r);
            return (deleted, await WithFreshLookupsAsync(snapshot with { Recipes = Sorted(recipes) }, cancellationToken));
        });

    // ------------------------------------------------------------------ plumbing

    private bool IsFresh() => snapshot.LastSuccess is { } last && clock.Now - last < MaxAge;

    /// <summary>Runs one write against the recipe book, then stores the resulting copy and tells the screens.</summary>
    private async Task<T> WriteAsync<T>(Func<Task<(T Result, RecipeSnapshot Next)>> write)
    {
        await gate.WaitAsync();
        try
        {
            var (result, next) = await write();
            await SaveAsync(next);
            NotifyChanged();
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Category counts and ingredient names change with every write. Nice to have – a failure keeps the old lookups.</summary>
    private async Task<RecipeSnapshot> WithFreshLookupsAsync(RecipeSnapshot next, CancellationToken cancellationToken)
    {
        try
        {
            return next with { Lookups = SortedLookups(await api.GetLookupsAsync(cancellationToken)) };
        }
        catch (RecipeApiException ex)
        {
            logger.LogInformation(ex, "Could not refresh the recipe book's lookups after a change");
            return next;
        }
    }

    private async Task SaveAsync(RecipeSnapshot next)
    {
        snapshot = next;
        if (next.LastSuccess is null)
        {
            // Only a write so far, never a full fetch: keep it in memory, but don't let a partial copy look complete on disk.
            return;
        }

        try
        {
            await store.SaveAsync(next);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not save the local copy of the recipe book");
        }
    }

    private void SetStatus(RecipeSyncStatus next)
    {
        status = next;
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "A RecipeService.Changed handler failed");
        }
    }

    private static RecipeSnapshot WithRecipe(RecipeSnapshot current, Recipe recipe) =>
        current with { Recipes = Sorted(current.Recipes.Where(r => r.Id != recipe.Id).Append(recipe)) };

    private static RecipeSnapshot Without(RecipeSnapshot current, int id) =>
        current with { Recipes = [.. current.Recipes.Where(r => r.Id != id)] };

    private static IReadOnlyList<Recipe> Sorted(IEnumerable<Recipe> recipes) =>
        [.. recipes.OrderBy(r => r.Title, TitleOrder).ThenBy(r => r.Id)];

    private static RecipeLookups SortedLookups(RecipeLookups lookups) => lookups with
    {
        Categories = [.. lookups.Categories.OrderBy(c => c.Name, TitleOrder)],
        Units = [.. lookups.Units.Distinct(StringComparer.OrdinalIgnoreCase).Order(TitleOrder)],
        Ingredients = [.. lookups.Ingredients.OrderByDescending(i => i.RecipeCount).ThenBy(i => i.Name, TitleOrder)],
    };
}
