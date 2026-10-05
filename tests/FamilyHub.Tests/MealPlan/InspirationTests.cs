using System.Globalization;
using FamilyHub.Core.Mambeno;
using FamilyHub.Modules.MealPlan.Plan;
using FamilyHub.Modules.MealPlan.Recipes;

namespace FamilyHub.Tests.MealPlan;

public sealed class InspirationTests
{
    private static readonly IReadOnlyList<RecipeCategory> Book =
    [
        new() { Id = 2, Name = "Aftensmad", RecipeCount = 5 },
        new() { Id = 5, Name = "Dessert", RecipeCount = 1 },
        new() { Id = 4, Name = "Kager", RecipeCount = 6 },
    ];

    private static readonly MambenoRecipe Stew = new()
    {
        Id = 2992,
        Title = "Afrikansk gryderet med søde kartofler",
        Description = "En mild gryderet.",
        CategoryId = 16,
        Category = "Mindre kød og vegetar",
        Categories = [new(18, "Aftensmad"), new(16, "Mindre kød og vegetar")],
        PrepTimeMinutes = 35,
        TotalTimeMinutes = 35,
        Servings = 4,
        Notes = "Lad børnene skære kartoflerne.",
        SourceUrl = "https://mambeno.dk/opskrifter/afrikansk-gryderet/",
        Ingredients =
        [
            new() { Amount = "250", Quantity = 250, Unit = "gram", Name = "ris" },
            new() { Amount = "0.5", Quantity = 0.5, Unit = "spsk", Name = "paprika" },
            new() { Name = "salt og peber" },
        ],
        Steps = ["Kog risene.", "Svits løget."],
    };

    public InspirationTests() =>
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("da-DK");

    [Fact]
    public void A_mambeno_recipe_becomes_a_recipe_book_draft()
    {
        var draft = Inspiration.ToDraft(Stew, Book, DinnerCourse.Main);

        Assert.Equal("Afrikansk gryderet med søde kartofler", draft.Title);
        Assert.Equal(2, draft.CategoryId);   // the book's "Aftensmad", found among the recipe's Mambeno categories
        Assert.Null(draft.Category);          // never creates one of Mambeno's categories in the book
        Assert.Equal(35, draft.PrepTimeMinutes);
        Assert.Equal(4, draft.Servings);
        Assert.Equal("Mambeno", draft.Author);
        Assert.Equal("En mild gryderet.\n\nLad børnene skære kartoflerne.\n\nFra Mambeno: https://mambeno.dk/opskrifter/afrikansk-gryderet/", draft.Notes);
        Assert.Equal(["250 gram ris", "0,5 spsk paprika", "  salt og peber"], draft.Ingredients.Select(i => $"{i.Amount} {i.Unit} {i.Name}"));
        Assert.Equal(["Kog risene.", "Svits løget."], draft.Steps);
        Assert.Empty(RecipeValidation.Validate(draft));
    }

    [Fact]
    public void Without_a_matching_category_the_course_decides()
    {
        var cake = Stew with { Category = "Kager, bagværk og sødt", Categories = [new(14, "Kager, bagværk og sødt")] };

        Assert.Equal(5, Inspiration.ToDraft(cake, Book, DinnerCourse.Dessert).CategoryId);
        Assert.Null(Inspiration.ToDraft(cake, Book, DinnerCourse.Main).CategoryId);
    }

    [Fact]
    public void Groups_become_sub_headings_like_the_book_writes_them()
    {
        var burger = Stew with
        {
            Ingredients =
            [
                new() { Amount = "500", Unit = "gram", Name = "hakket oksekød", Group = "Burgere" },
                new() { Amount = "4", Unit = "stk", Name = "boller", Group = "Burgere" },
                new() { Amount = "1", Unit = "dl", Name = "mayonnaise", Group = "Dressing" },
            ],
        };

        var lines = Inspiration.ToDraft(burger, Book, DinnerCourse.Main).Ingredients;

        Assert.Equal(["Burgere:", "hakket oksekød", "boller", "Dressing:", "mayonnaise"], lines.Select(i => i.Name));
        Assert.True(new RecipeIngredient { Name = lines[0].Name }.IsHeading);
    }

    [Fact]
    public void Long_texts_are_cut_to_what_the_book_accepts()
    {
        var huge = Stew with { Title = new string('a', 250), Ingredients = [new() { Amount = new string('1', 60), Name = new string('b', 210) }] };

        var draft = Inspiration.ToDraft(huge, Book, DinnerCourse.Main);

        Assert.Empty(RecipeValidation.Validate(draft));
    }

    [Fact]
    public void The_book_copy_is_found_by_title()
    {
        Recipe[] book = [new() { Id = 7, Title = "Afrikansk  gryderet med SØDE kartofler" }, new() { Id = 8, Title = "Lasagne" }];

        Assert.Equal(7, Inspiration.FindInBook(Stew, book)?.Id);
        Assert.Null(Inspiration.FindInBook(Stew with { Title = "Lasagne med spinat" }, book));
    }

    [Fact]
    public void Each_course_starts_on_its_mambeno_category()
    {
        MambenoCategory[] categories =
        [
            new() { Id = 18, Name = "Aftensmad", RecipeCount = 3206 },
            new() { Id = 38, Name = "Fisk", RecipeCount = 501, ParentId = 18 },
            new() { Id = 14, Name = "Kager, bagværk og sødt", RecipeCount = 341 },
        ];

        Assert.Equal(18, Inspiration.CategoryFor(DinnerCourse.Main, categories));
        Assert.Equal(14, Inspiration.CategoryFor(DinnerCourse.Dessert, categories));
        Assert.Null(Inspiration.CategoryFor(DinnerCourse.Starter, categories));
        Assert.Null(Inspiration.CategoryFor(DinnerCourse.Main, []));
    }

    [Fact]
    public void Lists_show_the_facts_the_danish_way()
    {
        Assert.Equal("35 min · 4 personer · Mindre kød og vegetar", Inspiration.Summary(Stew));
        Assert.Equal("", Inspiration.Summary(Stew with { TotalTimeMinutes = 0, Servings = 0, Category = "" }));
        Assert.Equal("½ spsk paprika", Inspiration.LineText(Stew.Ingredients[1]));
        Assert.Equal("salt og peber", Inspiration.LineText(Stew.Ingredients[2]));
    }
}
