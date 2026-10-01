using FamilyHub.Core.Time;
using FamilyHub.Modules.MealPlan.Components;
using FamilyHub.Modules.MealPlan.Recipes;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FamilyHub.Modules.MealPlan.Print;

/// <summary>
/// A recipe as an A4 PDF for the kitchen: title, facts and photo at the top, the ingredients in a column on the left and
/// the numbered steps on the right, notes at the bottom. Readable in black and white. Pure function – unit-tested.
/// </summary>
public static class RecipePdf
{
    // Paper, not screen: these are print colours and point sizes, not design tokens.
    private const string Accent = "#A4462A";
    private const string Text = "#1F1D1A";
    private const string Muted = "#6B655D";
    private const string Panel = "#F4F0EA";
    private const string Line = "#DDD6CC";
    private const float PanelPadding = 14;

    static RecipePdf()
    {
        // Free for individuals and small companies – a family's kitchen screen qualifies.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>The PDF. <paramref name="photo"/> is the recipe's photo (jpg/png/webp/gif) or null; a photo that cannot be read is left out.</summary>
    public static byte[] Create(Recipe recipe, byte[]? photo, DateOnly printedOn) => Compose(recipe, photo, printedOn).GeneratePdf();

    /// <summary>The document before it becomes a PDF – tests and previews render it to images.</summary>
    internal static IDocument Compose(Recipe recipe, byte[]? photo, DateOnly printedOn)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var image = TryLoad(photo);
        return Document.Create(document => document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(46);
                page.MarginVertical(40);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(style => style.FontSize(11).FontColor(Text).LineHeight(1.35f));

                page.Content().Column(column =>
                {
                    column.Spacing(18);
                    column.Item().Element(c => ComposeHeader(c, recipe));
                    if (image is not null)
                    {
                        column.Item().AlignCenter().MaxHeight(250).Image(image).FitArea();
                    }

                    column.Item().Element(c => ComposeBody(c, recipe));
                    if (recipe.Notes.Length > 0)
                    {
                        column.Item().Element(c => ComposeNotes(c, recipe.Notes));
                    }
                });

                page.Footer().PaddingTop(10).BorderTop(0.75f).BorderColor(Line).PaddingTop(6).Row(row =>
                {
                    row.RelativeItem().Text(FooterText(recipe, printedOn)).FontSize(8.5f).FontColor(Muted);
                    row.AutoItem().Text(text =>
                    {
                        text.DefaultTextStyle(style => style.FontSize(8.5f).FontColor(Muted));
                        text.Span("Side ");
                        text.CurrentPageNumber();
                        text.Span(" af ");
                        text.TotalPages();
                    });
                });
            }))
            .WithMetadata(new DocumentMetadata { Title = recipe.Title, Author = recipe.Author, Creator = "Family Hub" });
    }

    /// <summary>"Aftensmad", the title and "35 min · 4 personer · Let".</summary>
    private static void ComposeHeader(IContainer container, Recipe recipe) => container.Column(column =>
    {
        column.Spacing(4);
        if (recipe.HasCategory)
        {
            column.Item().Text(recipe.Category.ToUpper(DanishFormat.Culture)).FontSize(9).SemiBold().LetterSpacing(0.08f).FontColor(Accent);
        }

        column.Item().Text(recipe.Title).FontSize(26).Bold().LineHeight(1.1f);

        var facts = new[] { DinnerLook.Time(recipe), DinnerLook.Servings(recipe), recipe.Difficulty?.Label() }.OfType<string>().ToList();
        if (facts.Count > 0)
        {
            column.Item().PaddingTop(2).Text(string.Join("   ·   ", facts)).FontSize(11).FontColor(Muted);
        }

        column.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(Accent);
    });

    /// <summary>Ingredients on the left, steps on the right – or whichever of them the recipe has, full width.</summary>
    private static void ComposeBody(IContainer container, Recipe recipe)
    {
        var hasIngredients = recipe.Ingredients.Count > 0;
        var hasSteps = recipe.Steps.Count > 0;
        if (!hasIngredients && !hasSteps)
        {
            container.Text("Opskriften har hverken ingredienser eller fremgangsmåde i opskriftsbogen.").FontColor(Muted).Italic();
            return;
        }

        container.Row(row =>
        {
            row.Spacing(24);
            if (hasIngredients)
            {
                var ingredients = hasSteps ? row.ConstantItem(185) : row.RelativeItem();
                ingredients.Background(Panel).Padding(PanelPadding).Element(c => ComposeIngredients(c, recipe));
            }

            if (hasSteps)
            {
                // Level with the ingredients' heading inside its panel.
                row.RelativeItem().PaddingTop(hasIngredients ? PanelPadding : 0).Element(c => ComposeSteps(c, recipe.Steps));
            }
        });
    }

    private static void ComposeIngredients(IContainer container, Recipe recipe) => container.Column(column =>
    {
        column.Spacing(5);
        column.Item().PaddingBottom(4).Text("Ingredienser").FontSize(14).Bold();
        foreach (var line in RecipeMath.Scale(recipe.Ingredients, 1))
        {
            if (line.IsHeading)
            {
                column.Item().PaddingTop(6).Text(line.Name.TrimEnd(':').ToUpper(DanishFormat.Culture))
                    .FontSize(8.5f).SemiBold().LetterSpacing(0.06f).FontColor(Muted);
                continue;
            }

            var amount = string.Join(' ', new[] { line.AmountText, line.Unit }.Where(p => p.Length > 0));
            column.Item().Text(text =>
            {
                if (amount.Length > 0)
                {
                    text.Span(amount + " ").SemiBold();
                }

                text.Span(line.Name);
            });
        }
    });

    private static void ComposeSteps(IContainer container, IReadOnlyList<string> steps) => container.Column(column =>
    {
        column.Spacing(10);
        column.Item().PaddingBottom(2).Text("Sådan gør du").FontSize(14).Bold();
        for (var i = 0; i < steps.Count; i++)
        {
            var number = i + 1;
            var step = steps[i];
            column.Item().Row(row =>
            {
                row.ConstantItem(24).Text(number.ToString(DanishFormat.Culture)).FontSize(13).Bold().FontColor(Accent);
                row.RelativeItem().Text(step);
            });
        }
    });

    private static void ComposeNotes(IContainer container, string notes) =>
        container.BorderLeft(3).BorderColor(Accent).PaddingLeft(12).PaddingVertical(4).Column(column =>
        {
            column.Spacing(3);
            column.Item().Text("Noter").FontSize(11).Bold();
            column.Item().Text(notes).FontColor(Muted);
        });

    private static string FooterText(Recipe recipe, DateOnly printedOn)
    {
        var date = printedOn.ToString("d. MMMM yyyy", DanishFormat.Culture);
        return recipe.Author.Length > 0 ? $"Opskrift af {recipe.Author} · Udskrevet {date}" : $"Udskrevet {date}";
    }

    private static Image? TryLoad(byte[]? photo)
    {
        if (photo is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            return Image.FromBinaryData(photo);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or QuestPDF.Drawing.Exceptions.DocumentComposeException)
        {
            // An unreadable photo must not stop the recipe from being printed.
            return null;
        }
    }
}
