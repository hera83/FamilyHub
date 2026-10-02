using FamilyHub.Modules.MealPlan.Plan;
using FamilyHub.Modules.MealPlan.Recipes;
using FamilyHub.Modules.MealPlan.Shopping;

namespace FamilyHub.Tests.MealPlan;

public sealed class ShoppingCalculatorTests
{
    private static readonly DateOnly Monday = new(2026, 9, 28);

    private static RecipeIngredient Line(string amount, string unit, string name) => new()
    {
        Amount = amount,
        Quantity = double.TryParse(amount.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture, out var q) ? q : null,
        Unit = unit,
        Name = name,
    };

    private static Recipe Recipe(int id, string title, params RecipeIngredient[] ingredients) =>
        new() { Id = id, Title = title, Servings = 4, Ingredients = ingredients };

    private static ShoppingList Build(
        IEnumerable<Recipe> recipes,
        IEnumerable<FixedItem>? items = null,
        IEnumerable<GroceryRule>? rules = null,
        IReadOnlyDictionary<string, ShoppingMarks>? marks = null,
        DateOnly? from = null)
    {
        var dishes = recipes.Select((r, i) => (new PlannedDish(Monday.AddDays(i), DinnerCourse.Main, r.Id, r.Title), (Recipe?)r)).ToList();
        return ShoppingCalculator.Build(new ShoppingInput
        {
            Week = Monday,
            From = from ?? Monday,
            Dishes = dishes,
            FixedItems = [.. items ?? []],
            Rules = (rules ?? []).ToDictionary(r => r.Key),
            Marks = marks ?? new Dictionary<string, ShoppingMarks>(),
        });
    }

    private static ShoppingLine Single(ShoppingList list, string name) => Assert.Single(list.Lines, l => l.Name == name);

    // ------------------------------------------------------------------ units

    [Theory]
    [InlineData(2, "dl", 200, Measure.Volume)]
    [InlineData(1.5, "spiseskefuld", 22.5, Measure.Volume)]
    [InlineData(2, "tsk", 10, Measure.Volume)]
    [InlineData(1, "l", 1000, Measure.Volume)]
    [InlineData(250, "g", 250, Measure.Mass)]
    [InlineData(1.2, "kg", 1200, Measure.Mass)]
    [InlineData(3, "stk.", 3, Measure.Count)]
    [InlineData(2, "", 2, Measure.Count)]
    public void Units_are_read_into_their_base_unit(double quantity, string unit, double expected, Measure measure)
    {
        var amount = ShoppingUnits.ToBase(quantity, unit);

        Assert.Equal(measure, amount.Measure);
        Assert.Equal(expected, amount.Value, 6);
    }

    [Fact]
    public void Units_of_their_own_are_counted_in_the_singular()
    {
        Assert.Equal(new Measured(2, Measure.Other, "dåse"), ShoppingUnits.ToBase(2, "dåser"));
        Assert.Equal("3 dåser", ShoppingUnits.Format(new Measured(3, Measure.Other, "dåse")));
        Assert.Equal("1 dåse", ShoppingUnits.Format(new Measured(1, Measure.Other, "dåse")));
    }

    [Theory]
    [InlineData(500, "dl", "5 dl")]
    [InlineData(1300, "dl", "1,3 l")]
    [InlineData(250, "dl", "2½ dl")]
    [InlineData(500, "l", "½ l")]
    [InlineData(300, "l", "3 dl")]
    [InlineData(45, "spsk", "3 spsk")]
    [InlineData(20, "spsk", "1⅓ spsk")]
    [InlineData(105, "spsk", "7 spsk")]
    [InlineData(150, "spsk", "1½ dl")]
    [InlineData(30, "ml", "30 ml")]
    public void Volumes_are_written_in_the_unit_the_recipes_used(double ml, string preferred, string expected) =>
        Assert.Equal(expected, ShoppingUnits.Format(new Measured(ml, Measure.Volume), preferred));

    [Theory]
    [InlineData(800, "800 g")]
    [InlineData(1300, "1,3 kg")]
    [InlineData(1250, "1,25 kg")]
    [InlineData(2000, "2 kg")]
    public void Weights_turn_into_kilos_from_1000_g(double grams, string expected) =>
        Assert.Equal(expected, ShoppingUnits.Format(new Measured(grams, Measure.Mass)));

    [Theory]
    [InlineData(250, Measure.Volume, "¼ l")]
    [InlineData(500, Measure.Volume, "½ l")]
    [InlineData(200, Measure.Volume, "2 dl")]
    [InlineData(400, Measure.Mass, "400 g")]
    [InlineData(1200, Measure.Mass, "1,2 kg")]
    [InlineData(6, Measure.Count, "6 stk")]
    public void Pack_sizes_are_written_like_on_the_pack(double size, Measure measure, string expected) =>
        Assert.Equal(expected, ShoppingUnits.FormatPack(size, measure));

    // ------------------------------------------------------------------ packs

    [Theory]
    [InlineData(1300, new double[] { 1000 }, Measure.Volume, "2 l")]          // milk is bought in litres
    [InlineData(200, new double[] { 1000 }, Measure.Volume, "1 l")]
    [InlineData(320, new double[] { 250 }, Measure.Mass, "2 × 250 g")]         // butter
    [InlineData(1300, new double[] { 350, 500, 800, 1200 }, Measure.Mass, "800 g + 500 g")]
    [InlineData(800, new double[] { 350, 500, 800, 1200 }, Measure.Mass, "800 g")]
    [InlineData(400, new double[] { 350, 1200 }, Measure.Mass, "2 × 350 g")]  // not one huge pack
    [InlineData(1020, new double[] { 1000, 2000 }, Measure.Mass, "1 kg")]     // 2 % short is fine
    [InlineData(600, new double[] { 250, 500 }, Measure.Volume, "½ l + ¼ l")]  // cream
    [InlineData(7, new double[] { 6, 10 }, Measure.Count, "10 stk")]          // eggs
    [InlineData(800, new double[] { 400 }, Measure.Mass, "2 × 400 g")]
    public void Packs_cover_the_need_with_few_packs_and_little_left_over(double need, double[] sizes, Measure measure, string expected) =>
        Assert.Equal(expected, PackPlanner.Format(PackPlanner.Plan(need, sizes), measure));

    [Fact]
    public void Packs_need_a_need_and_a_size()
    {
        Assert.Empty(PackPlanner.Plan(0, [500]));
        Assert.Empty(PackPlanner.Plan(500, []));
        Assert.Equal([new PackCount(500, 200)], PackPlanner.Plan(100_000, [50, 500])); // a party: just the big packs
    }

    // ------------------------------------------------------------------ recognising groceries

    [Theory]
    [InlineData("Lun mælk", "mælk", "Mælk")]
    [InlineData("Lunken mælk", "mælk", "Mælk")]
    [InlineData("letmælk", "mælk", "Mælk")]
    [InlineData("Æg til pensling", "æg", "Æg")]
    [InlineData("Kærnemælk.", "kærnemælk", "Kærnemælk")]
    [InlineData("kærnemælk, A38 eller ymer", "kærnemælk", "Kærnemælk")]
    [InlineData("øko appelsiner", "appelsiner", "Appelsiner")]
    [InlineData("piskefløde", "fløde", "Fløde")]
    [InlineData("Flourmelis", "flormelis", "Flormelis")]
    [InlineData("evt. chili flager hvis man ønsker det stærkt.", "chiliflager", "Chiliflager")]
    [InlineData("citron saft efter smag", "citronsaft", "Citronsaft")]
    [InlineData("blandet kerner til topping.", "kerner", "Kerner")]
    public void Recipe_names_are_recognised(string name, string key, string display)
    {
        var identity = GroceryCatalog.Identify(name);

        Assert.True(identity.IsExact);
        Assert.Equal(key, identity.Key);
        Assert.Equal(display, identity.Name);
    }

    [Theory]
    [InlineData("Røget laks", GrocerySection.MeatAndFish)]
    [InlineData("creme fraiche 9 %", GrocerySection.Chilled)]
    [InlineData("Mandler der blendes", GrocerySection.Pantry)]
    [InlineData("pasteuriserede æggeblommer", GrocerySection.Chilled)]
    public void Related_names_borrow_the_section_but_keep_their_own_name(string name, GrocerySection section)
    {
        var identity = GroceryCatalog.Identify(name);

        Assert.False(identity.IsExact);
        Assert.Equal(section, identity.Grocery?.Section);
        Assert.Equal(GroceryCatalog.Clean(name), identity.Key);
    }

    [Fact]
    public void Every_name_in_the_catalogue_points_to_one_grocery()
    {
        var names = GroceryCatalog.All.SelectMany(g => g.Aliases.Prepend(g.Name)).Select(GroceryCatalog.Clean).ToList();

        Assert.Empty(names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key));
        Assert.All(GroceryCatalog.All, g => Assert.Equal(g.PackSizes.Count > 0, g.PackMeasure is not null));
    }

    // ------------------------------------------------------------------ the list

    [Fact]
    public void The_same_grocery_in_different_units_is_added_up_and_bought_in_whole_litres()
    {
        var list = Build(
        [
            Recipe(1, "Pandekager", Line("2", "dl", "Lun mælk"), Line("300", "ml", "mælk")),
            Recipe(2, "Boller", Line("8", "dl", "mælk")),
        ]);

        var milk = Single(list, "Mælk");
        Assert.Equal("2 l", milk.Amount);
        Assert.Equal("1,3 l", milk.Need);
        Assert.Equal(["Pandekager", "Boller"], milk.Sources);
        Assert.Equal(GrocerySection.Chilled, milk.Section);
    }

    [Fact]
    public void Weight_and_volume_meet_when_the_weight_per_dl_is_known()
    {
        var list = Build([Recipe(1, "Chokoladekage", Line("1000", "gram", "Kærnemælk.")), Recipe(2, "Boller", Line("3", "dl", "kærnemælk, A38 eller ymer"))]);

        var buttermilk = Single(list, "Kærnemælk");
        Assert.Equal("2 l", buttermilk.Amount);
        Assert.Equal("1,3 l", buttermilk.Need);
    }

    [Fact]
    public void Butter_is_bought_in_packs_and_a_spoonful_counts()
    {
        var list = Build([Recipe(1, "Kage", Line("320", "gram", "smør"), Line("2", "spsk", "Smør"))]);

        var butter = Single(list, "Smør");
        Assert.Equal("2 × 250 g", butter.Amount);
        Assert.Equal("349 g", butter.Need);
    }

    [Fact]
    public void Meat_shows_the_total_until_the_family_sets_their_pack_sizes()
    {
        Recipe[] recipes = [Recipe(1, "Lasagne", Line("800", "gram", "hakket oksekød")), Recipe(2, "Chili", Line("500", "gram", "oksekød"))];

        var total = Single(Build(recipes), "Hakket oksekød");
        Assert.Equal("1,3 kg", total.Amount);
        Assert.Null(total.Need);

        var packs = Single(Build(recipes, rules:
        [
            total.Rule with { Mode = BuyMode.Packs, PackMeasure = Measure.Mass, PackSizes = [1200, 800, 500, 350] },
        ]), "Hakket oksekød");
        Assert.Equal("800 g + 500 g", packs.Amount);
        Assert.Equal("1,3 kg", packs.Need);
        Assert.True(packs.HasOwnRule);
    }

    [Fact]
    public void Pieces_are_rounded_up_and_garlic_cloves_become_heads()
    {
        var list = Build([Recipe(1, "Suppe", new RecipeIngredient { Name = "løg", Amount = "½", Quantity = 0.5 }, Line("2", "fed", "hvidløg"), Line("4", "fed", "Hvidløg"))]);

        Assert.Equal("1 stk", Single(list, "Løg").Amount);
        Assert.Equal("½ stk", Single(list, "Løg").Need);
        Assert.Equal("1 stk", Single(list, "Hvidløg").Amount);
        Assert.Equal("6 fed", Single(list, "Hvidløg").Need);
    }

    [Fact]
    public void Staples_stay_off_the_list_until_they_are_missing()
    {
        Recipe[] recipes = [Recipe(1, "Chili", Line("", "", "salt"), Line("", "", "peber"), Line("2", "dåse", "hakkede tomater"))];

        var list = Build(recipes);
        Assert.Equal(["Hakkede tomater"], list.Lines.Select(l => l.Name));
        Assert.Equal(["Peber", "Salt"], list.Staples.Select(l => l.Name));
        Assert.All(list.Staples, s => Assert.False(s.IsNeeded));

        var missing = Build(recipes, marks: new Dictionary<string, ShoppingMarks> { ["salt"] = ShoppingMarks.Needed });
        Assert.Contains(missing.Lines, l => l.Name == "Salt" && l.IsStaple && l.IsNeeded);
        Assert.Equal(2, missing.Staples.Count);
    }

    [Fact]
    public void A_crossed_out_line_stays_on_the_list_as_done()
    {
        var list = Build([Recipe(1, "Chili", Line("2", "dåse", "hakkede tomater"), Line("1", "stk", "squash"))],
            marks: new Dictionary<string, ShoppingMarks> { ["squash"] = ShoppingMarks.Done });

        Assert.True(Single(list, "Squash").IsDone);
        Assert.Equal(1, list.Remaining);
        Assert.Equal("2 dåser", Single(list, "Hakkede tomater").Amount);
    }

    [Fact]
    public void Fixed_items_join_the_recipes_and_keep_their_own_name()
    {
        FixedItem[] items =
        [
            new() { Id = 1, Name = "Mælk", Quantity = 2, Unit = "l" },
            new() { Id = 2, Name = "Pålæg til madpakker", Quantity = 3, Unit = "pakke" },
            new() { Id = 3, Name = "Skyr", Quantity = 2, Unit = "stk", Week = Monday },
            new() { Id = 4, Name = "Kun i næste uge", Quantity = 1, Week = Monday.AddDays(7) },
            new() { Id = 5, Name = "Salt" },
        ];

        var list = Build([Recipe(1, "Pandekager", Line("2", "dl", "mælk"))], items);

        var milk = Single(list, "Mælk");
        Assert.Equal("3 l", milk.Amount);
        Assert.Equal("2,2 l", milk.Need);
        Assert.Equal([ShoppingCalculator.EveryWeekSource, "Pandekager"], milk.Sources);
        Assert.Equal("3 pakker", Single(list, "Pålæg til madpakker").Amount);
        Assert.Equal([ShoppingCalculator.ThisWeekSource], Single(list, "Skyr").Sources);
        Assert.DoesNotContain(list.Lines, l => l.Name == "Kun i næste uge");
        Assert.False(Single(list, "Salt").IsStaple); // asked for on purpose
        Assert.Equal(4, list.FixedItems.Count);
    }

    [Fact]
    public void Two_cartons_of_milk_are_two_litres()
    {
        var list = Build([Recipe(1, "Grød", Line("5", "dl", "mælk"))], [new() { Id = 1, Name = "mælk", Quantity = 2, Unit = "stk" }]);

        Assert.Equal("3 l", Single(list, "Mælk").Amount);
    }

    [Fact]
    public void Amounts_that_cannot_be_added_up_are_shown_beside_each_other()
    {
        var list = Build([Recipe(1, "A", Line("400", "gram", "hakkede tomater")), Recipe(2, "B", Line("1", "dåse", "hakkede tomater"))]);

        Assert.Equal("400 g + 1 dåse", Single(list, "Hakkede tomater").Amount);
    }

    [Fact]
    public void A_range_counts_as_its_largest_number_and_words_are_kept()
    {
        var list = Build([Recipe(1, "Boller", Line("700-800", "gram", "hvedemel"), Line("lidt", "", "rosiner"))],
            marks: new Dictionary<string, ShoppingMarks> { ["hvedemel"] = ShoppingMarks.Needed });

        Assert.Equal(800, ShoppingCalculator.UpperOfRange("700-800"));
        Assert.Equal(3, ShoppingCalculator.UpperOfRange("2–3"));
        Assert.Null(ShoppingCalculator.UpperOfRange("2"));
        Assert.Equal("1 kg", Single(list, "Hvedemel").Amount);
        Assert.Equal("lidt", Single(list, "Rosiner").Amount);
    }

    [Fact]
    public void Tap_water_and_headings_are_left_out_and_unknown_groceries_go_last()
    {
        var list = Build([Recipe(1, "Pizza", Line("5", "dl", "Vand"), Line("6", "spsk", "Kogende vand"), Line("", "", "Glasur:"),
            Line("1", "stk", "Dragefrugt"), Line("1", "stk", "squash"))]);

        Assert.Equal(["Squash", "Dragefrugt"], list.Lines.Select(l => l.Name));
        Assert.Equal(GrocerySection.Other, list.Lines[1].Section);
    }

    [Fact]
    public void Only_the_dishes_from_the_first_day_count_and_own_dishes_are_listed_apart()
    {
        var dishes = new List<(PlannedDish, Recipe?)>
        {
            (new PlannedDish(Monday, DinnerCourse.Main, 1, "Mandag"), Recipe(1, "Mandag", Line("1", "stk", "squash"))),
            (new PlannedDish(Monday.AddDays(2), DinnerCourse.Main, 2, "Onsdag"), Recipe(2, "Onsdag", Line("2", "stk", "squash"))),
            (new PlannedDish(Monday.AddDays(3), DinnerCourse.Main, null, "Rester"), null),
            (new PlannedDish(Monday.AddDays(7), DinnerCourse.Main, 3, "Næste uge"), Recipe(3, "Næste uge", Line("9", "stk", "squash"))),
        };

        var list = ShoppingCalculator.Build(new ShoppingInput { Week = Monday, From = Monday.AddDays(1), Dishes = dishes });

        Assert.Equal("2 stk", Single(list, "Squash").Amount);
        Assert.Equal(["Onsdag"], list.Dishes.Select(d => d.Title));
        Assert.Equal(["Rester"], list.WithoutIngredients.Select(d => d.Title));
    }

    [Fact]
    public void The_list_follows_the_shop_from_fruit_and_vegetables_to_the_pantry()
    {
        var list = Build([Recipe(1, "Chili", Line("2", "dåse", "kidneybønner"), Line("1", "stk", "squash"), Line("2,5", "dl", "creme fraiche 9 %"),
            Line("500", "gram", "hakket oksekød"))]);

        Assert.Equal(["Squash", "Hakket oksekød", "Creme fraiche 9 %", "Kidneybønner"], list.Lines.Select(l => l.Name));
    }

    [Fact]
    public void A_borrowed_grocery_is_not_sold_in_the_packs_of_the_one_it_borrows_from()
    {
        var list = Build([Recipe(1, "Is", Line("4", "stk", "pasteuriserede æggeblommer"))]);

        Assert.Equal("4 stk", Single(list, "Pasteuriserede æggeblommer").Amount);
    }
}
