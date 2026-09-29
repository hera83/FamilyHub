using System.Net;
using System.Text.Json;
using FamilyHub.Modules.MealPlan.Recipes;

namespace FamilyHub.Tests.MealPlan;

public class RecipeMappingTests
{
    // Trimmed copy of a real answer from GET /recipes.
    private const string PageJson = """
        {"items":[{"id":23,"title":"Appelsin blondie ","categoryId":4,"category":"Kager","categoryIcon":"cookie",
          "prepTimeMinutes":0,"cookTimeMinutes":0,"totalTimeMinutes":0,"servings":24,"difficulty":"Middel",
          "author":"Charlotte Ramskov","notes":"Kagen er bedst hvis man bager den dagen før.",
          "lastModified":"2026-09-20T16:38:45.8986644","imageUrl":null,
          "ingredients":[{"amount":"2 ","quantity":2,"unit":"stk","name":"Øko appelsiner"},
                         {"amount":"1,5","quantity":1.5,"unit":"dl","name":"Appelsinsaft"},
                         {"amount":"","quantity":null,"unit":"","name":"Glasur:"},
                         {"amount":"","quantity":null,"unit":"","name":"Flourmelis"},
                         {"amount":"","quantity":null,"unit":"","name":"  "}],
          "steps":["Tænd ovnen på 180 Grader varmluft","","mix glasuren.\r\nNår kagen er kold lunes glasuren."]}],
         "totalCount":23,"page":1,"pageSize":1,"totalPages":23}
        """;

    private static RecipePage Page() =>
        RecipeMapping.ToPage(JsonSerializer.Deserialize<RecipePageDto>(PageJson, RecipeApiClient.Json)!);

    [Fact]
    public void A_recipe_page_is_read_as_the_api_sends_it()
    {
        var page = Page();
        var recipe = page.Items.Single();

        Assert.True(page.HasMore);
        Assert.Equal(23, page.TotalCount);
        Assert.Equal("Appelsin blondie", recipe.Title);
        Assert.Equal(4, recipe.CategoryId);
        Assert.Equal(RecipeDifficulty.Medium, recipe.Difficulty);
        Assert.Equal(24, recipe.Servings);
        Assert.False(recipe.HasImage);
        Assert.Equal("2", recipe.Ingredients[0].Amount);
        Assert.Equal(1.5, recipe.Ingredients[1].Quantity);
    }

    [Fact]
    public void Last_modified_is_utc_although_the_api_sends_no_offset()
    {
        var recipe = Page().Items.Single();

        Assert.Equal("2026-09-20 16:38:45 +00:00", recipe.LastModified.ToString("yyyy-MM-dd HH:mm:ss zzz", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Sub_headings_in_the_ingredient_list_are_recognised_and_empty_lines_dropped()
    {
        var ingredients = Page().Items.Single().Ingredients;

        Assert.Equal(4, ingredients.Count);
        Assert.True(ingredients[2].IsHeading);
        Assert.Equal("Glasur:", ingredients[2].Name);
        Assert.False(ingredients[3].IsHeading); // "Flourmelis" without an amount is still an ingredient
    }

    [Fact]
    public void Empty_steps_are_dropped_and_line_breaks_normalised()
    {
        var steps = Page().Items.Single().Steps;

        Assert.Equal(2, steps.Count);
        Assert.Equal("mix glasuren.\nNår kagen er kold lunes glasuren.", steps[1]);
    }

    [Fact]
    public void Lookups_fall_back_to_the_three_difficulties_and_skip_blank_names()
    {
        var lookups = RecipeMapping.ToLookups(new LookupsDto(null, [new CategoryDto(2, "Aftensmad", null, 5)], ["dl", " "], [new IngredientUsageDto("Mel", 4), new IngredientUsageDto("", 1)]));

        Assert.Equal([RecipeDifficulty.Easy, RecipeDifficulty.Medium, RecipeDifficulty.Hard], lookups.Difficulties);
        Assert.Equal("cookie", lookups.Categories.Single().Icon);
        Assert.Equal(["dl"], lookups.Units);
        Assert.Equal("Mel", lookups.Ingredients.Single().Name);
    }

    [Fact]
    public void The_query_string_uses_the_apis_names_and_values()
    {
        var query = RecipeApiClient.ToQueryString(new RecipeQuery
        {
            Search = " æble ",
            CategoryId = 4,
            Difficulty = RecipeDifficulty.Hard,
            MaxTotalMinutes = 30,
            Ingredients = ["mel", "sukker", " "],
            ModifiedSince = new DateTimeOffset(2026, 9, 20, 18, 38, 40, TimeSpan.FromHours(2)),
            Sort = RecipeSort.LastModified,
            Page = 0,
            PageSize = 500,
        });

        Assert.Equal(
            "?search=%C3%A6ble&categoryId=4&difficulty=Sv%C3%A6r&maxTotalMinutes=30&ingredient=mel&ingredient=sukker"
            + "&modifiedSince=2026-09-20T16%3A38%3A40.0000000Z&sort=LastModified&page=1&pageSize=200",
            query);
    }

    [Fact]
    public void A_new_recipe_is_sent_trimmed_without_blank_lines_or_unset_fields()
    {
        var body = JsonSerializer.SerializeToElement(RecipeMapping.ToInput(new RecipeDraft
        {
            Title = " Boller ",
            Difficulty = RecipeDifficulty.Hard,
            Author = " ",
            Ingredients = [new IngredientDraft { Amount = "2 ", Unit = "", Name = " mel " }, new IngredientDraft { Name = " " }],
            Steps = ["Ælt", "  "],
        }), RecipeApiClient.Json);

        Assert.Equal("Boller", body.GetProperty("title").GetString());
        Assert.Equal("Svær", body.GetProperty("difficulty").GetString());
        Assert.False(body.TryGetProperty("author", out _)); // omitted → the recipe book uses "Mor"
        Assert.False(body.TryGetProperty("categoryId", out _));
        var ingredient = body.GetProperty("ingredients").EnumerateArray().Single();
        Assert.Equal("mel", ingredient.GetProperty("name").GetString());
        Assert.False(ingredient.TryGetProperty("unit", out _));
        Assert.Equal(1, body.GetProperty("steps").GetArrayLength());
    }

    [Fact]
    public void Editing_starts_from_a_draft_with_everything_from_the_recipe()
    {
        var recipe = Page().Items.Single();

        var draft = RecipeDraft.From(recipe);

        Assert.Equal(recipe.Title, draft.Title);
        Assert.Equal(4, draft.CategoryId);
        Assert.Null(draft.Category);
        Assert.Equal(recipe.Ingredients.Count, draft.Ingredients.Count);
        Assert.Equal("Glasur:", draft.Ingredients[2].Name);
        Assert.Equal(recipe.Steps, draft.Steps);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"title":"Manglende eller ugyldig API-nøgle","status":401,"detail":"Send den delte nøgle i headeren X-Api-Key."}""", RecipeApiError.Unauthorized, "Opskriftsbogen afviste nøglen.")]
    [InlineData(HttpStatusCode.NotFound, """{"title":"Not Found","status":404}""", RecipeApiError.NotFound, "Findes ikke længere i opskriftsbogen.")]
    [InlineData(HttpStatusCode.Conflict, """{"title":"Conflict","status":409,"detail":"Kategorien findes allerede."}""", RecipeApiError.Conflict, "Kategorien findes allerede.")]
    [InlineData(HttpStatusCode.BadGateway, "<html>502 Bad Gateway</html>", RecipeApiError.Failed, "Opskriftsbogen svarede med en fejl.")]
    public void Errors_get_a_short_danish_message(HttpStatusCode status, string body, RecipeApiError error, string message)
    {
        var ex = RecipeMapping.ToException(status, body);

        Assert.Equal(error, ex.Error);
        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public void Validation_errors_from_the_api_can_be_shown_under_the_field()
    {
        var ex = RecipeMapping.ToException(HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"Title":["The Title field is required."]}}""");

        Assert.Equal(RecipeApiError.Invalid, ex.Error);
        Assert.Equal("The Title field is required.", ex.FieldError("title"));
        Assert.Null(ex.FieldError("servings"));
    }

    [Theory]
    [InlineData("kage.JPG", "image/jpeg")]
    [InlineData("kage.webp", "image/webp")]
    [InlineData("kage.heic", null)]
    [InlineData(null, null)]
    public void Only_the_photo_formats_the_api_accepts_are_allowed(string? fileName, string? contentType)
    {
        Assert.Equal(contentType, RecipeImages.ContentType(fileName));
    }
}

public class RecipeValidationTests
{
    [Fact]
    public void A_valid_draft_has_no_errors()
    {
        Assert.Empty(RecipeValidation.Validate(new RecipeDraft { Title = "Boller", Ingredients = [new IngredientDraft { Name = "Mel", Amount = "500", Unit = "gram" }] }));
    }

    [Fact]
    public void Problems_are_reported_per_field_with_the_apis_field_names()
    {
        var errors = RecipeValidation.Validate(new RecipeDraft
        {
            Title = " ",
            Servings = 1001,
            PrepTimeMinutes = -1,
            Ingredients = [new IngredientDraft { Name = "Mel" }, new IngredientDraft { Name = "", Unit = new string('x', 51) }],
        });

        Assert.Equal("Skriv et navn på retten.", errors["Title"]);
        Assert.Equal("Skal være mellem 0 og 1000.", errors["Servings"]);
        Assert.True(errors.ContainsKey("PrepTimeMinutes"));
        Assert.Equal("Skriv hvad ingrediensen er.", errors["Ingredients[1].Name"]);
        Assert.Equal("Højst 50 tegn.", errors["Ingredients[1].Unit"]);
        Assert.False(errors.ContainsKey("Ingredients[0].Name"));
    }

    [Fact]
    public void A_category_needs_a_name()
    {
        Assert.Equal("Skriv et navn på kategorien.", RecipeValidation.Validate(new RecipeCategoryDraft { Name = "" })["Name"]);
    }
}
