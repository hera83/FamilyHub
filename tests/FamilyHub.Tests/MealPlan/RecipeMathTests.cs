using FamilyHub.Modules.MealPlan.Recipes;

namespace FamilyHub.Tests.MealPlan;

public class RecipeMathTests
{
    private static RecipeIngredient Line(string name, double? quantity = null, string unit = "", string? amount = null) =>
        new() { Name = name, Quantity = quantity, Unit = unit, Amount = amount ?? (quantity?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "") };

    private static Recipe Recipe(int id, int servings, params RecipeIngredient[] ingredients) =>
        new() { Id = id, Title = $"Ret {id}", Servings = servings, Ingredients = ingredients };

    [Theory]
    [InlineData(1.5, "1½")]
    [InlineData(0.5, "½")]
    [InlineData(0.25, "¼")]
    [InlineData(2.75, "2¾")]
    [InlineData(0.3333, "⅓")]
    [InlineData(1.6667, "1⅔")]
    [InlineData(2, "2")]
    [InlineData(2.4, "2,4")]
    [InlineData(0.05, "0,05")]
    [InlineData(12.5, "13")]
    [InlineData(1250, "1250")]
    [InlineData(2.995, "3")]
    [InlineData(0, "0")]
    public void Quantities_are_written_the_danish_way(double quantity, string expected)
    {
        Assert.Equal(expected, RecipeMath.FormatQuantity(quantity));
    }

    [Fact]
    public void Scaling_adjusts_numbers_and_keeps_text_amounts_and_headings()
    {
        var recipe = Recipe(1, 4, Line("mælk", 2, "dl"), Line("æg", amount: "2-3", unit: "stk"), Line("Glasur:"), Line("flormelis", 100, "gram"));

        var scaled = RecipeMath.Scale(recipe, 6);

        Assert.Equal("3 dl mælk", scaled[0].Text);
        Assert.Equal(3, scaled[0].Quantity);
        Assert.Equal("2-3 stk æg", scaled[1].Text); // can't be scaled – shown as written
        Assert.Null(scaled[1].Quantity);
        Assert.True(scaled[2].IsHeading);
        Assert.Equal("150 gram flormelis", scaled[3].Text);
    }

    [Fact]
    public void Recipes_without_a_number_of_people_are_not_scaled()
    {
        var recipe = Recipe(1, 0, Line("mel", 500, "gram"));

        Assert.Equal(1, RecipeMath.Factor(recipe, 8));
        Assert.Equal("500 gram mel", RecipeMath.Scale(recipe, 8).Single().Text);
    }

    [Fact]
    public void Shopping_lists_add_up_the_same_ingredient_across_dishes()
    {
        var pancakes = Recipe(1, 4, Line("Mælk", 5, "dl"), Line("Hvedemel", 250, "g"), Line("Æg", 3, "stk"));
        var buns = Recipe(2, 16, Line("mælk.", 6, "dl"), Line("hvedemel", 1000, "gram"), Line("Glasur:"), Line("Salt", amount: "1 knivspids"));

        var list = RecipeMath.CombineForShopping([(pancakes, 8), (buns, 8)]);

        Assert.Equal(["1 knivspids Salt", "1000 gram Hvedemel", "13 dl Mælk", "6 stk Æg"], list.Select(i => i.Text).OrderBy(t => t, StringComparer.Ordinal));
        Assert.Equal([1, 2], list.Single(i => i.Name == "Mælk").RecipeIds);
        Assert.DoesNotContain(list, i => i.Name.StartsWith("Glasur", StringComparison.Ordinal));
    }

    [Fact]
    public void Amounts_that_cannot_be_added_are_listed_side_by_side()
    {
        var one = Recipe(1, 4, Line("Æg", amount: "2-3", unit: "stk"));
        var two = Recipe(2, 4, Line("æg", 1, "stk"));

        var egg = RecipeMath.CombineForShopping([(one, 0), (two, 0)]).Single();

        Assert.Null(egg.Quantity);
        Assert.Equal("2-3 + 1 stk Æg", egg.Text);
    }

    [Theory]
    [InlineData("spiseskefuld", "spsk")]
    [InlineData("G", "gram")]
    [InlineData(" DL ", "dl")]
    [InlineData(null, "")]
    public void Units_with_the_same_meaning_are_merged(string? unit, string expected)
    {
        Assert.Equal(expected, RecipeMath.NormalizeUnit(unit));
    }
}

public class RecipeSearchTests
{
    private static readonly DateTimeOffset Base = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly Recipe[] Book =
    [
        new() { Id = 1, Title = "Æblekage", Category = "Dessert", CategoryId = 5, TotalTimeMinutes = 60, Difficulty = RecipeDifficulty.Easy, LastModified = Base.AddDays(1),
            Ingredients = [new() { Name = "Æbler" }, new() { Name = "Hvedemel" }, new() { Name = "Sukker" }] },
        new() { Id = 2, Title = "Boller", Category = "Brød", CategoryId = 3, TotalTimeMinutes = 90, Difficulty = RecipeDifficulty.Medium, LastModified = Base.AddDays(3),
            Notes = "Gode med smør", Ingredients = [new() { Name = "Mel" }, new() { Name = "Gær" }] },
        new() { Id = 3, Title = "Carbonara", Category = "Aftensmad", CategoryId = 2, TotalTimeMinutes = 20, Difficulty = RecipeDifficulty.Easy, LastModified = Base.AddDays(2),
            Ingredients = [new() { Name = "Spaghetti" }, new() { Name = "Æg" }, new() { Name = "Bacon i tern" }] },
    ];

    private static IEnumerable<int> Ids(RecipeQuery query) => RecipeSearch.Apply(Book, query).Select(r => r.Id);

    [Fact]
    public void Text_search_covers_title_category_notes_and_ingredients_ignoring_case_also_for_æøå()
    {
        Assert.Equal([1], Ids(new RecipeQuery { Search = "æBLE" }));
        Assert.Equal([3], Ids(new RecipeQuery { Search = "aftensmad" }));
        Assert.Equal([2], Ids(new RecipeQuery { Search = "SMØR" }));
        Assert.Equal([3], Ids(new RecipeQuery { Search = "bacon" }));
    }

    [Fact]
    public void Every_ingredient_must_match_part_of_a_name()
    {
        Assert.Equal([2, 1], Ids(new RecipeQuery { Ingredients = ["mel"] })); // Boller, Æblekage
        Assert.Equal([1], Ids(new RecipeQuery { Ingredients = ["mel", "sukker"] }));
    }

    [Fact]
    public void Filters_combine()
    {
        Assert.Equal([3], Ids(new RecipeQuery { Difficulty = RecipeDifficulty.Easy, MaxTotalMinutes = 30 }));
        Assert.Equal([2], Ids(new RecipeQuery { CategoryId = 3 }));
        Assert.Equal([2], Ids(new RecipeQuery { Category = "brød" }));
        Assert.Equal([2, 3], Ids(new RecipeQuery { ModifiedSince = Base.AddDays(2) }).Order());
    }

    [Fact]
    public void Sort_orders_match_the_api()
    {
        Assert.Equal([2, 3, 1], Ids(new RecipeQuery())); // Danish: Æ after the rest
        Assert.Equal([2, 3, 1], Ids(new RecipeQuery { Sort = RecipeSort.LastModified }));
        Assert.Equal([3, 1, 2], Ids(new RecipeQuery { Sort = RecipeSort.TotalTime }));
    }
}
