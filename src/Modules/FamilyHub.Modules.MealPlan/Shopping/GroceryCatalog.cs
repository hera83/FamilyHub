using System.Text.RegularExpressions;
using FamilyHub.Core.Time;
using FamilyHub.Modules.MealPlan.Recipes;

namespace FamilyHub.Modules.MealPlan.Shopping;

/// <summary>Where in the shop a grocery is – the shopping list is grouped this way. The values are stored in the database.</summary>
public enum GrocerySection
{
    Produce = 1,
    Bakery = 2,
    MeatAndFish = 3,
    Chilled = 4,
    Pantry = 5,
    Frozen = 6,
    Drinks = 7,
    Household = 8,
    Other = 9,
}

/// <summary>How a grocery goes on the shopping list. The values are stored in the database.</summary>
public enum BuyMode
{
    /// <summary>The total the dishes need: "1,3 kg hakket oksekød".</summary>
    Total = 0,

    /// <summary>Whole packs: "2 l mælk", "2 × 250 g smør", "800 g + 500 g hakket oksekød".</summary>
    Packs = 1,
}

public static class GrocerySections
{
    /// <summary>The order of a Danish supermarket: fruit and vegetables first, household last.</summary>
    public static IReadOnlyList<GrocerySection> ShopOrder { get; } =
    [
        GrocerySection.Produce, GrocerySection.Bakery, GrocerySection.MeatAndFish, GrocerySection.Chilled,
        GrocerySection.Pantry, GrocerySection.Frozen, GrocerySection.Drinks, GrocerySection.Household, GrocerySection.Other,
    ];

    public static string Label(this GrocerySection section) => section switch
    {
        GrocerySection.Produce => "Frugt og grønt",
        GrocerySection.Bakery => "Brød",
        GrocerySection.MeatAndFish => "Kød og fisk",
        GrocerySection.Chilled => "Mejeri og køl",
        GrocerySection.Pantry => "Kolonial",
        GrocerySection.Frozen => "Frost",
        GrocerySection.Drinks => "Drikkevarer",
        GrocerySection.Household => "Husholdning",
        _ => "Andet",
    };

    /// <summary>Position in <see cref="ShopOrder"/>, for sorting.</summary>
    public static int Position(this GrocerySection section)
    {
        for (var i = 0; i < ShopOrder.Count; i++)
        {
            if (ShopOrder[i] == section)
            {
                return i;
            }
        }

        return ShopOrder.Count;
    }
}

/// <summary>What the hub knows about a grocery out of the box: where it is in the shop and how it is sold.</summary>
public sealed record Grocery
{
    /// <summary>The name on the shopping list, e.g. "Mælk".</summary>
    public required string Name { get; init; }

    public GrocerySection Section { get; init; } = GrocerySection.Other;

    /// <summary>Other names in recipes, lower case: "sødmælk", "letmælk" …</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>What the pack sizes are measured in. Null = no packs (the total goes on the list).</summary>
    public Measure? PackMeasure { get; init; }

    /// <summary>The pack sizes in the shop, in base units (ml, g, stk). Empty = the total goes on the list.</summary>
    public IReadOnlyList<double> PackSizes { get; init; } = [];

    /// <summary>Grams in one dl – so "4 dl havregryn" and "200 g havregryn" can be added up.</summary>
    public double? GramsPerDl { get; init; }

    /// <summary>The family always has it (salt, pepper, oil) – it only goes on the list when they say it's missing.</summary>
    public bool IsStaple { get; init; }

    /// <summary>Never bought (tap water).</summary>
    public bool IsFree { get; init; }

    /// <summary>Units only this grocery has: "fed" hvidløg = 0,1 stk, "terning" gær = 50 g.</summary>
    public IReadOnlyDictionary<string, Measured> Units { get; init; } = new Dictionary<string, Measured>();

    /// <summary>The key the grocery is recognised and stored by: the name in lower case.</summary>
    public string Key => GroceryCatalog.Clean(Name);
}

/// <summary>An ingredient recognised (or not) in the catalogue.</summary>
/// <param name="Key">What the same grocery is added up and remembered by ("mælk").</param>
/// <param name="Name">The name on the list ("Mælk").</param>
/// <param name="Grocery">What the catalogue knows, or null.</param>
/// <param name="IsExact">
/// The ingredient <em>is</em> the grocery ("Lun mælk" → Mælk). False when only the last or first word matched
/// ("røget laks" → Laks): the section and staple setting are borrowed, but it keeps its own name and key, and pack sizes and
/// units are not used (røget laks is not sold like laks).
/// </param>
public sealed record GroceryIdentity(string Key, string Name, Grocery? Grocery, bool IsExact);

/// <summary>
/// The groceries the hub knows (Danish supermarket, 2026) and how recipe names are recognised. The family can change
/// any of it per grocery (<see cref="GroceryRule"/>). Pure functions – unit-tested.
/// </summary>
public static partial class GroceryCatalog
{
    // Static fields are set in the order they are written: Fillers and Cuts (used by Clean) must come before All and Index.

    // Words that describe how an ingredient is used, not what is bought: "Lun mælk" is milk.
    private static readonly HashSet<string> Fillers = new(StringComparer.Ordinal)
    {
        "evt", "evt.", "eventuelt", "lidt", "ca", "ca.", "cirka",
        "lun", "lunken", "lunkent", "lunkne", "lune", "kold", "koldt", "kolde", "iskold", "iskoldt", "kogende", "varm", "varmt", "varme",
        "stuetempereret", "blødt", "blød", "smeltet", "frisk", "friske", "fersk", "ferske", "økologisk", "økologiske", "øko",
    };

    // Where the grocery ends and the description begins: "æg til pensling", "citronsaft efter smag", "chiliflager hvis …".
    private static readonly string[] Cuts = [" til ", " efter smag", " hvis ", " eller efter", " – ", " - "];

    private static readonly Measure? Volume = Measure.Volume;
    private static readonly Measure? Mass = Measure.Mass;
    private static readonly Measure? Count = Measure.Count;

    private static Grocery Produce(string name, params string[] aliases) => new() { Name = name, Section = GrocerySection.Produce, Aliases = aliases };

    private static Grocery Bakery(string name, params string[] aliases) => new() { Name = name, Section = GrocerySection.Bakery, Aliases = aliases };

    private static Grocery Meat(string name, params string[] aliases) => new() { Name = name, Section = GrocerySection.MeatAndFish, Aliases = aliases };

    private static Grocery Chilled(string name, params string[] aliases) => new() { Name = name, Section = GrocerySection.Chilled, Aliases = aliases };

    private static Grocery Pantry(string name, params string[] aliases) => new() { Name = name, Section = GrocerySection.Pantry, Aliases = aliases };

    private static Grocery Staple(string name, params string[] aliases) => Pantry(name, aliases) with { IsStaple = true };

    private static Grocery Frozen(string name, params string[] aliases) => new() { Name = name, Section = GrocerySection.Frozen, Aliases = aliases };

    private static Grocery Drink(string name, params string[] aliases) => new() { Name = name, Section = GrocerySection.Drinks, Aliases = aliases };

    private static Grocery Household(string name, params string[] aliases) => new() { Name = name, Section = GrocerySection.Household, Aliases = aliases };

    /// <summary>Every grocery the hub knows. Some aliases are spellings from the family's recipe book ("flourmelis").</summary>
    public static IReadOnlyList<Grocery> All { get; } =
    [
        // Frugt og grønt
        Produce("Løg", "gule løg", "gult løg"),
        Produce("Rødløg", "rødt løg", "røde løg"),
        Produce("Forårsløg"),
        Produce("Hvidløg") with { Units = new Dictionary<string, Measured> { ["fed"] = new(0.1, Measure.Count) } },
        Produce("Kartofler", "kartoffel", "nye kartofler", "bagekartofler"),
        Produce("Søde kartofler", "sød kartoffel"),
        Produce("Gulerødder", "gulerod"),
        Produce("Squash", "courgette"),
        Produce("Peberfrugt", "peberfrugter", "rød peberfrugt", "grøn peberfrugt", "gul peberfrugt"),
        Produce("Snackpeber", "snackpebre", "snackpeberfrugter"),
        Produce("Agurk", "agurker"),
        Produce("Snackagurker", "snackagurk"),
        Produce("Tomater", "tomat"),
        Produce("Cherrytomater", "cherrytomat", "snacktomater"),
        Produce("Salat", "salathoved", "icebergsalat", "hjertesalat"),
        Produce("Rucola"),
        Produce("Spinat", "babyspinat"),
        Produce("Broccoli"),
        Produce("Blomkål"),
        Produce("Porre", "porrer"),
        Produce("Bladselleri", "stangselleri"),
        Produce("Champignoner", "champignon", "svampe"),
        Produce("Citroner", "citron"),
        Produce("Lime", "limefrugt", "limefrugter"),
        Produce("Appelsiner", "appelsin"),
        Produce("Æbler", "æble"),
        Produce("Bananer", "banan"),
        Produce("Pærer", "pære"),
        Produce("Vindruer", "druer"),
        Produce("Avocado", "avocadoer", "avokado"),
        Produce("Ingefær"),
        Produce("Chili", "chilier", "rød chili"),
        Produce("Persille", "bredbladet persille", "kruspersille"),
        Produce("Purløg"),
        Produce("Basilikum"),
        Produce("Koriander"),
        Produce("Dild"),
        Produce("Mynte"),
        Produce("Frugt"),
        Produce("Grøntsager", "grønt"),

        // Brød
        Bakery("Rugbrød"),
        Bakery("Franskbrød", "toastbrød"),
        Bakery("Boller", "rundstykker"),
        Bakery("Burgerboller"),
        Bakery("Tortillas", "tortilla", "wraps", "tortillapandekager"),
        Bakery("Pitabrød", "pita"),
        Bakery("Knækbrød"),

        // Kød og fisk – sold in many sizes, so the total goes on the list unless the family sets packs.
        Meat("Hakket oksekød", "oksekød", "hakkebøf", "oksefars"),
        Meat("Hakket svinekød", "svinekød", "grisekød"),
        Meat("Hakket kalv og flæsk", "kalv og flæsk", "fars"),
        Meat("Kylling", "hel kylling"),
        Meat("Kyllingebryst", "kyllingebryster", "kyllingebrystfilet", "kyllingefilet", "kyllingefileter", "kyllingeinderfilet", "kyllingeinderfileter"),
        Meat("Kyllingelår", "kyllingeoverlår"),
        Meat("Bacon", "bacon i tern", "bacontern", "baconterninger", "baconskiver"),
        Meat("Medister", "medisterpølse"),
        Meat("Pølser", "pølse", "hotdogpølser"),
        Meat("Laks", "laksefilet", "laksefileter"),
        Meat("Torsk", "torskefilet", "torskefileter"),
        Meat("Rejer"),
        Meat("Fisk"),

        // Mejeri og køl
        Chilled("Mælk", "sødmælk", "letmælk", "minimælk", "skummetmælk") with { PackMeasure = Volume, PackSizes = [1000], GramsPerDl = 100 },
        Chilled("Kærnemælk") with { PackMeasure = Volume, PackSizes = [1000], GramsPerDl = 100 },
        Chilled("Fløde", "piskefløde") with { PackMeasure = Volume, PackSizes = [250, 500], GramsPerDl = 100 },
        Chilled("Madlavningsfløde") with { PackMeasure = Volume, PackSizes = [250, 500], GramsPerDl = 100 },
        Chilled("Creme fraiche", "cremefraiche", "crème fraîche") with { GramsPerDl = 100 },
        Chilled("Yoghurt", "yoghurt naturel", "græsk yoghurt") with { GramsPerDl = 100 },
        Chilled("Ymer"),
        Chilled("A38"),
        Chilled("Skyr"),
        Chilled("Smør", "saltet smør", "usaltet smør") with
        {
            PackMeasure = Mass, PackSizes = [250], GramsPerDl = 95, Units = new Dictionary<string, Measured> { ["pakke"] = new(250, Measure.Mass) },
        },
        Chilled("Margarine", "plantemargarine") with { GramsPerDl = 95 },
        Chilled("Æg", "æggeblomme", "æggeblommer", "æggehvide", "æggehvider") with { PackMeasure = Count, PackSizes = [6, 10] },
        Chilled("Ost", "skiveost", "mellemlagret ost"),
        Chilled("Revet ost", "revet mozzarella", "pizzaost"),
        Chilled("Parmesan", "revet parmesan", "parmigiano"),
        Chilled("Mozzarella"),
        Chilled("Feta", "fetaost"),
        Chilled("Hytteost"),
        Chilled("Flødeost", "philadelphia"),
        Chilled("Mascarpone"),
        Chilled("Pålæg"),
        Chilled("Leverpostej"),
        Chilled("Risengrød", "færdig risengrød"),
        Chilled("Gær", "bagegær") with
        {
            PackMeasure = Mass, PackSizes = [50], Units = new Dictionary<string, Measured> { ["terning"] = new(50, Measure.Mass) },
        },

        // Kolonial
        Pantry("Hvedemel", "mel", "alm. mel", "almindelig hvedemel") with { PackMeasure = Mass, PackSizes = [1000, 2000], GramsPerDl = 60, IsStaple = true },
        Pantry("Tipo 00-mel", "tipo 00", "mel tipo 00", "tipo 00 mel", "pizzamel") with { PackMeasure = Mass, PackSizes = [1000], GramsPerDl = 60 },
        Pantry("Grahamsmel") with { PackMeasure = Mass, PackSizes = [1000], GramsPerDl = 60 },
        Pantry("Fuldkornsmel", "fuldkornshvedemel") with { PackMeasure = Mass, PackSizes = [1000], GramsPerDl = 60 },
        Pantry("Rugmel") with { PackMeasure = Mass, PackSizes = [1000], GramsPerDl = 60 },
        Pantry("Sukker", "alm. sukker", "rørsukker") with { PackMeasure = Mass, PackSizes = [1000, 2000], GramsPerDl = 85, IsStaple = true },
        Pantry("Flormelis", "flourmelis", "florsukker") with { PackMeasure = Mass, PackSizes = [500], GramsPerDl = 50 },
        Pantry("Brun farin", "farin") with { PackMeasure = Mass, PackSizes = [500], GramsPerDl = 80 },
        Pantry("Havregryn", "store havregryn", "små havregryn") with { PackMeasure = Mass, PackSizes = [1000], GramsPerDl = 35 },
        Pantry("Ris", "jasminris", "basmatiris", "langkornet ris") with { PackMeasure = Mass, PackSizes = [1000], GramsPerDl = 85 },
        Pantry("Vilde ris"),
        Pantry("Sushiris", "sushi-ris"),
        Pantry("Spaghetti") with { PackMeasure = Mass, PackSizes = [500] },
        Pantry("Fuldkornsspaghetti") with { PackMeasure = Mass, PackSizes = [500] },
        Pantry("Pasta", "penne", "fusilli", "makaroni", "farfalle") with { PackMeasure = Mass, PackSizes = [500] },
        Pantry("Lasagneplader"),
        Pantry("Nudler", "ægnudler"),
        Pantry("Hakkede tomater", "flåede tomater", "tomater på dåse", "hakkede tomater på dåse"),
        Pantry("Tomatpuré", "tomatpure", "tomatkoncentrat"),
        Pantry("Kidneybønner"),
        Pantry("Kikærter"),
        Pantry("Hvide bønner"),
        Pantry("Sorte bønner"),
        Pantry("Linser", "røde linser"),
        Pantry("Majs", "majskerner"),
        Pantry("Kokosmælk"),
        Pantry("Kondenseret mælk", "sødet kondenseret mælk"),
        Pantry("Tun", "tun i vand"),
        Pantry("Ketchup"),
        Pantry("Sennep", "dijonsennep", "dijonsennup", "groft sennep"),
        Pantry("Mayonnaise", "mayo"),
        Pantry("Honning", "akaciahonning") with { GramsPerDl = 140 },
        Pantry("Sirup", "lys sirup", "mørk sirup", "glukosesirup") with { GramsPerDl = 140 },
        Pantry("Citronsaft", "citron saft") with { GramsPerDl = 100 },
        Pantry("Kakao", "kakaopulver", "kakao pulver") with { GramsPerDl = 40 },
        Pantry("Chokolade"),
        Pantry("Mørk chokolade", "chokolade 70 %"),
        Pantry("Hvid chokolade"),
        Pantry("Mælkechokolade"),
        Pantry("Kokosmel") with { GramsPerDl = 35 },
        Pantry("Mandler"),
        Pantry("Hasselnødder"),
        Pantry("Peanuts", "jordnødder"),
        Pantry("Rosiner"),
        Pantry("Solsikkekerner"),
        Pantry("Græskarkerner"),
        Pantry("Kerner", "blandede kerner", "blandet kerner"),
        Pantry("Tranebær", "tørrede tranebær"),
        Pantry("Tørrede abrikoser", "abrikoser"),
        Pantry("Mandelaroma"),
        Pantry("Frugtfarve", "madfarve", "rød frugtfarve", "rød frugt farve", "frugt farve", "farve"),
        Pantry("Tørgær"),

        // Basisvarer – the family has them, so they only go on the list when they say so.
        Staple("Salt", "groft salt", "fint salt", "flagesalt", "havsalt") with { GramsPerDl = 120 },
        Staple("Peber", "sort peber", "hvid peber", "friskkværnet peber", "peber fra kværn", "kværnet peber"),
        Staple("Olie", "rapsolie", "solsikkeolie", "neutral olie", "vegetabilsk olie", "madolie"),
        Staple("Olivenolie", "ekstra jomfru olivenolie"),
        Staple("Eddike", "hvidvinseddike", "æblecidereddike", "lagereddike"),
        Staple("Soja", "sojasauce", "sojasovs", "soya"),
        Staple("Bouillon", "bouillonterning", "bouillonterninger", "grøntsagsbouillon", "hønsebouillon", "oksebouillon", "kyllingebouillon", "fond"),
        Staple("Bagepulver"),
        Staple("Natron"),
        Staple("Vaniljesukker", "vanillesukker"),
        Staple("Majsstivelse", "majsstivlese", "maizena", "maizenamel"),
        Staple("Hjortetaksalt"),
        Staple("Paprika", "paprikapulver", "røget paprika"),
        Staple("Spidskommen", "stødt spidskommen"),
        Staple("Kanel", "stødt kanel"),
        Staple("Karry", "karrypulver"),
        Staple("Oregano", "tørret oregano"),
        Staple("Timian", "tørret timian"),
        Staple("Rosmarin", "tørret rosmarin"),
        Staple("Chiliflager", "chili flager"),
        Staple("Chilipulver", "cayennepeber"),
        Staple("Muskatnød", "revet muskatnød"),
        Staple("Kardemomme", "stødt kardemomme"),
        Staple("Gurkemeje"),
        Staple("Laurbærblade", "laurbærblad"),
        Staple("Garam masala"),
        Staple("Kommen"),
        Staple("Hvidløgspulver"),
        Staple("Kaffe"),

        // Frost
        Frozen("Ærter", "frosne ærter", "grønne ærter"),
        Frozen("Frosne bær", "bær"),
        Frozen("Is", "vaniljeis", "flødeis"),

        // Drikkevarer
        Drink("Appelsinjuice", "appelsinsaft", "appelsin saft", "juice") with { PackMeasure = Volume, PackSizes = [1000], GramsPerDl = 100 },
        Drink("Æblejuice"),
        Drink("Sodavand"),
        Drink("Danskvand"),

        // Husholdning – for the family's fixed items
        Household("Køkkenrulle"),
        Household("Toiletpapir"),
        Household("Opvasketabs", "opvaskemiddel"),
        Household("Vaskemiddel"),
        Household("Affaldsposer"),
        Household("Bagepapir"),
        Household("Alufolie", "sølvpapir"),
        Household("Fryseposer"),
        Household("Madpapir"),

        // Tap water is never bought.
        new Grocery { Name = "Vand", Aliases = ["isvand", "postevand"], IsFree = true },
    ];

    private static readonly Dictionary<string, Grocery> Index = BuildIndex();

    [GeneratedRegex(@"\s*(min\.?\s*)?\d+([.,]\d+)?\s*%.*$")]
    private static partial Regex Percentage();

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex Brackets();

    /// <summary>
    /// The grocery behind an ingredient name: "Lun mælk" → Mælk, "æg til pensling" → Æg, "kærnemælk, A38 eller ymer" → Kærnemælk,
    /// "røget laks" → its own grocery in Laks' section. Unknown names keep their own (cleaned) name.
    /// </summary>
    public static GroceryIdentity Identify(string? name)
    {
        var key = Clean(name);
        if (key.Length == 0)
        {
            return new GroceryIdentity("", "", null, false);
        }

        // Exact: the whole name, or what comes before a comma ("kærnemælk, a38 eller ymer").
        var beforeComma = key.Split(',')[0].Trim();
        foreach (var candidate in new[] { key, beforeComma })
        {
            if (Index.TryGetValue(candidate, out var grocery))
            {
                return new GroceryIdentity(grocery.Key, grocery.Name, grocery, true);
            }
        }

        // Borrowed: the name without a fat percentage, or its last or first word ("røget laks", "mandler der blendes").
        var plain = Percentage().Replace(key, "").Trim();
        var words = plain.Replace(',', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var borrowed = new[] { plain, words.LastOrDefault(), words.FirstOrDefault() }
            .Where(c => !string.IsNullOrEmpty(c))
            .Select(c => Index.GetValueOrDefault(c!))
            .FirstOrDefault(g => g is not null);
        return new GroceryIdentity(key, DanishFormat.Capitalize(key), borrowed, false);
    }

    /// <summary>
    /// The name reduced to what is bought: lower case, single spaces, no description ("til pensling", "efter smag", "(…)")
    /// and no words like "lun" or "evt.".
    /// </summary>
    public static string Clean(string? name)
    {
        var text = Brackets().Replace(RecipeMath.NormalizeName(name), " ");
        foreach (var cut in Cuts)
        {
            var at = text.IndexOf(cut, StringComparison.Ordinal);
            if (at > 0)
            {
                text = text[..at];
            }
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (words.Count > 1 && Fillers.Contains(words[0]))
        {
            words.RemoveAt(0);
        }

        return string.Join(' ', words).Trim(' ', '.', ',', ':', ';');
    }

    private static Dictionary<string, Grocery> BuildIndex()
    {
        var index = new Dictionary<string, Grocery>(StringComparer.Ordinal);
        foreach (var grocery in All)
        {
            foreach (var name in grocery.Aliases.Prepend(grocery.Name))
            {
                // A duplicate is a mistake in the list above – the unit test catches it, so the first one wins here.
                index.TryAdd(Clean(name), grocery);
            }
        }

        return index;
    }
}
