using FamilyHub.UI.Components;
using FamilyHub.UI.Modules;

namespace FamilyHub.Modules.MealPlan;

/// <summary>The week's meal plan and the shopping list generated from it.</summary>
public sealed class MealPlanModule : HubModule
{
    public const string Path = "/madplan";

    public override string Id => "madplan";

    public override string Title => "Madplan";

    public override string Description => "Ugens retter og indkøbsliste";

    public override IconName Icon => IconName.Utensils;

    public override string Route => Path;

    public override int Order => 20;
}
