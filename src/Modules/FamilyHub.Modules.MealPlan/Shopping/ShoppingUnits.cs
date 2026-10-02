using System.Globalization;
using FamilyHub.Core.Time;
using FamilyHub.Modules.MealPlan.Recipes;

namespace FamilyHub.Modules.MealPlan.Shopping;

/// <summary>What an amount measures. Amounts of the same kind can be added up (2 dl + 300 ml = 5 dl).</summary>
public enum Measure
{
    /// <summary>Volume, counted in millilitres (ml, cl, dl, l, spsk, tsk).</summary>
    Volume = 0,

    /// <summary>Weight, counted in grams (g, kg).</summary>
    Mass = 1,

    /// <summary>Pieces (stk – or no unit at all: "2 løg").</summary>
    Count = 2,

    /// <summary>Anything else ("dåse", "glas", "pakke") – only added up with the same unit.</summary>
    Other = 3,
}

/// <summary>
/// An amount in its base unit: millilitres, grams or pieces – or in its own unit for <see cref="Measure.Other"/>
/// (<see cref="Unit"/> is then the singular, e.g. "dåse").
/// </summary>
public readonly record struct Measured(double Value, Measure Measure, string Unit = "");

/// <summary>
/// Units on the shopping list: reading the recipe book's units, adding them up and writing the result the Danish way
/// ("1,3 l", "2½ dl", "1,25 kg", "3 dåser"). Pure functions – unit-tested.
/// </summary>
public static class ShoppingUnits
{
    // Base units: ml for volume, g for mass, stk for pieces. Spoons are the Danish measuring spoons.
    private static readonly Dictionary<string, (Measure Measure, double Factor)> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ml"] = (Measure.Volume, 1),
        ["cl"] = (Measure.Volume, 10),
        ["dl"] = (Measure.Volume, 100),
        ["l"] = (Measure.Volume, 1000),
        ["liter"] = (Measure.Volume, 1000),
        ["spsk"] = (Measure.Volume, 15),
        ["tsk"] = (Measure.Volume, 5),
        ["gram"] = (Measure.Mass, 1),
        ["kg"] = (Measure.Mass, 1000),
        ["kilo"] = (Measure.Mass, 1000),
        ["stk"] = (Measure.Count, 1),
        [""] = (Measure.Count, 1),
    };

    // Singular → plural for units counted in their own right ("2 dåser"). Words not here are the same in both.
    private static readonly Dictionary<string, string> Plurals = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dåse"] = "dåser",
        ["pakke"] = "pakker",
        ["pose"] = "poser",
        ["bakke"] = "bakker",
        ["flaske"] = "flasker",
        ["terning"] = "terninger",
        ["bundt"] = "bundter",
        ["skive"] = "skiver",
        ["knivspids"] = "knivspidser",
        ["håndfuld"] = "håndfulde",
        ["karton"] = "kartoner",
    };

    private static readonly Dictionary<string, string> Singulars =
        Plurals.ToDictionary(p => p.Value, p => p.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The amount in its base unit: <c>(2, "dl")</c> → 200 ml, <c>(1, "kg")</c> → 1000 g, <c>(3, "dåser")</c> → 3 "dåse".
    /// Units are read like on the shopping list (<see cref="RecipeMath.NormalizeUnit"/>: "g" = "gram", "spiseskefuld" = "spsk").
    /// </summary>
    public static Measured ToBase(double quantity, string? unit)
    {
        var normalized = RecipeMath.NormalizeUnit(unit);
        return Known.TryGetValue(normalized, out var known)
            ? new Measured(quantity * known.Factor, known.Measure)
            : new Measured(quantity, Measure.Other, Singular(normalized));
    }

    /// <summary>How many base units one <paramref name="unit"/> is (dl → 100), or null for units of their own ("dåse").</summary>
    public static double? Factor(string? unit, Measure measure) =>
        Known.TryGetValue(RecipeMath.NormalizeUnit(unit), out var known) && known.Measure == measure ? known.Factor : null;

    /// <summary>"dåser" → "dåse", so 2 dåser and 1 dåse are added up.</summary>
    public static string Singular(string unit) => Singulars.TryGetValue(unit, out var singular) ? singular : unit;

    /// <summary>"dåse" → "dåser" when there is more than one.</summary>
    public static string Plural(string unit, double quantity) =>
        quantity > 1 && Plurals.TryGetValue(unit, out var plural) ? plural : unit;

    /// <summary>
    /// A total the way a Danish shopping list writes it: 1300 ml → "1,3 l", 250 ml → "2½ dl", 1250 g → "1,25 kg", 3 "dåse" → "3 dåser".
    /// <paramref name="preferredUnit"/> is the unit the recipes used (the largest of them), so spoons stay spoons and dl stays dl
    /// until it adds up to a litre.
    /// </summary>
    public static string Format(Measured amount, string? preferredUnit = null) => amount.Measure switch
    {
        Measure.Volume => FormatVolume(amount.Value, RecipeMath.NormalizeUnit(preferredUnit)),
        Measure.Mass => FormatMass(amount.Value),
        Measure.Count => $"{RecipeMath.FormatQuantity(amount.Value)} stk",
        _ => Join(RecipeMath.FormatQuantity(amount.Value), Plural(amount.Unit, amount.Value)),
    };

    /// <summary>One pack: 250 ml → "¼ l", 200 ml → "2 dl", 400 g → "400 g", 1200 g → "1,2 kg", 6 → "6 stk".</summary>
    public static string FormatPack(double size, Measure measure) => measure switch
    {
        Measure.Volume when size >= 250 && IsMultipleOf(size, 250) => $"{RecipeMath.FormatQuantity(size / 1000)} l",
        Measure.Volume when size >= 100 => $"{RecipeMath.FormatQuantity(size / 100)} dl",
        Measure.Volume => $"{Whole(size)} ml",
        Measure.Mass => FormatMass(size),
        _ => $"{Whole(size)} stk",
    };

    private static string FormatVolume(double ml, string preferred)
    {
        // Up to ten tablespoons stays in spoons ("7 spsk kakao"); more than that is easier to measure in dl.
        var spoon = preferred is "spsk" or "tsk";
        if (spoon && ml < 150)
        {
            return $"{RecipeMath.FormatQuantity(ml / Known[preferred].Factor)} {preferred}";
        }

        if (ml >= 1000 || (preferred == "l" && IsMultipleOf(ml, 250)))
        {
            return $"{RecipeMath.FormatQuantity(ml / 1000)} l";
        }

        if (preferred is "ml" or "cl" && ml < 100)
        {
            return preferred == "cl" ? $"{RecipeMath.FormatQuantity(ml / 10)} cl" : $"{Whole(ml)} ml";
        }

        return ml >= 50 || preferred is "dl" or "l" ? $"{RecipeMath.FormatQuantity(ml / 100)} dl" : $"{Whole(ml)} ml";
    }

    private static string FormatMass(double grams) => grams >= 1000
        ? $"{Math.Round(grams / 1000, 2, MidpointRounding.AwayFromZero).ToString("0.##", DanishFormat.Culture)} kg"
        : $"{Whole(grams)} g";

    private static string Whole(double value) =>
        Math.Max(1, Math.Round(value, MidpointRounding.AwayFromZero)).ToString("0", CultureInfo.InvariantCulture);

    private static bool IsMultipleOf(double value, double step) => Math.Abs(value / step - Math.Round(value / step)) < 1e-6;

    private static string Join(string amount, string unit) => unit.Length == 0 ? amount : $"{amount} {unit}";
}
