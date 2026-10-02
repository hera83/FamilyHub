using FamilyHub.Core.Time;
using FamilyHub.Modules.MealPlan.Plan;
using FamilyHub.Modules.MealPlan.Shopping;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FamilyHub.Modules.MealPlan.Print;

/// <summary>
/// The shopping list as an A4 PDF to take to the shop: by section, in two columns, with a box to tick per grocery.
/// Crossed-out groceries and staples that are not missing are left out. Black and white. Pure function – unit-tested.
/// </summary>
public static class ShoppingListPdf
{
    // Paper, not screen: these are print colours and point sizes, not design tokens.
    private const string Accent = "#A4462A";
    private const string Text = "#1F1D1A";
    private const string Muted = "#6B655D";
    private const string Line = "#DDD6CC";

    static ShoppingListPdf()
    {
        // Free for individuals and small companies – a family's kitchen screen qualifies.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Create(ShoppingList list, DateOnly printedOn) => Compose(list, printedOn).GeneratePdf();

    /// <summary>The groceries that go on paper: on the list and not crossed out.</summary>
    public static IReadOnlyList<ShoppingLine> LinesToPrint(ShoppingList list) => [.. list.Lines.Where(l => !l.IsDone)];

    /// <summary>The document before it becomes a PDF – tests render it to images.</summary>
    internal static IDocument Compose(ShoppingList list, DateOnly printedOn)
    {
        ArgumentNullException.ThrowIfNull(list);
        var lines = LinesToPrint(list);
        return Document.Create(document => document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(46);
                page.MarginVertical(40);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(style => style.FontSize(11.5f).FontColor(Text).LineHeight(1.3f));

                page.Header().PaddingBottom(16).Element(c => ComposeHeader(c, list));

                page.Content().Element(c =>
                {
                    if (lines.Count == 0)
                    {
                        c.Text("Der er ikke noget på listen – alt er streget ud.").FontColor(Muted).Italic();
                        return;
                    }

                    c.MultiColumn(columns =>
                    {
                        columns.Columns(2);
                        columns.Spacing(28);
                        columns.BalanceHeight();
                        columns.Content().Column(column =>
                        {
                            column.Spacing(14);
                            foreach (var section in lines.GroupBy(l => l.Section))
                            {
                                // A heading never stands alone at the bottom of a column.
                                column.Item().EnsureSpace(60).Element(s => ComposeSection(s, section.Key, [.. section]));
                            }
                        });
                    });
                });

                page.Footer().PaddingTop(10).BorderTop(0.75f).BorderColor(Line).PaddingTop(6).Row(row =>
                {
                    row.RelativeItem().Text($"Udskrevet {printedOn.ToString("d. MMMM yyyy", DanishFormat.Culture)}").FontSize(8.5f).FontColor(Muted);
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
            .WithMetadata(new DocumentMetadata { Title = $"Indkøbsliste {MealPlanRules.WeekTitle(list.Week).ToLower(DanishFormat.Culture)}", Creator = "Family Hub" });
    }

    /// <summary>"Indkøbsliste", "Uge 2 · 7.–13. januar" and the dishes.</summary>
    private static void ComposeHeader(IContainer container, ShoppingList list) => container.Column(column =>
    {
        column.Spacing(3);
        column.Item().Text("Indkøbsliste").FontSize(26).Bold().LineHeight(1.1f);
        column.Item().Text($"{MealPlanRules.WeekTitle(list.Week)} · {DanishFormat.DayRange(list.From, list.Week.AddDays(6))}").FontSize(11).FontColor(Muted);

        var dishes = list.Dishes.Select(d => d.Title).Distinct().ToList();
        if (dishes.Count > 0)
        {
            column.Item().PaddingTop(2).Text($"Ugens retter: {string.Join(" · ", dishes)}").FontSize(9.5f).FontColor(Muted);
        }

        column.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(Accent);
    });

    private static void ComposeSection(IContainer container, GrocerySection section, IReadOnlyList<ShoppingLine> lines) => container.Column(column =>
    {
        column.Spacing(5);
        column.Item().PaddingBottom(2).Text(section.Label().ToUpper(DanishFormat.Culture)).FontSize(9).SemiBold().LetterSpacing(0.08f).FontColor(Accent);
        foreach (var line in lines)
        {
            column.Item().Row(row =>
            {
                row.Spacing(8);
                row.ConstantItem(10).PaddingTop(2.5f).Height(10).Border(0.9f).BorderColor(Muted);
                row.RelativeItem().Text(text =>
                {
                    if (line.Amount.Length > 0)
                    {
                        text.Span(line.Amount + "  ").SemiBold();
                    }

                    text.Span(line.Name);
                });
            });
        }
    });
}
