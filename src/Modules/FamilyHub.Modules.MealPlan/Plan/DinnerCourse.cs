namespace FamilyHub.Modules.MealPlan.Plan;

/// <summary>
/// Which part of the dinner a dish is. A day has at most one of each.
/// The values are stored in the database: <see cref="Main"/> is 0, so dinners planned before courses existed are main courses.
/// </summary>
public enum DinnerCourse
{
    Main = 0,
    Starter = 1,
    Dessert = 2,
}

/// <summary>Names and order of the courses – pure functions, unit-tested.</summary>
public static class DinnerCourses
{
    /// <summary>The order they are eaten – and shown: starter, main course, dessert.</summary>
    public static IReadOnlyList<DinnerCourse> MenuOrder { get; } = [DinnerCourse.Starter, DinnerCourse.Main, DinnerCourse.Dessert];

    /// <summary>"Forret", "Hovedret", "Dessert".</summary>
    public static string Label(this DinnerCourse course) => course switch
    {
        DinnerCourse.Starter => "Forret",
        DinnerCourse.Dessert => "Dessert",
        _ => "Hovedret",
    };

    /// <summary>Position in <see cref="MenuOrder"/>, for sorting.</summary>
    public static int Position(this DinnerCourse course) => course switch
    {
        DinnerCourse.Starter => 0,
        DinnerCourse.Main => 1,
        _ => 2,
    };
}
