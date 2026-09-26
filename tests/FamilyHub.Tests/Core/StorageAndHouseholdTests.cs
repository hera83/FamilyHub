using FamilyHub.Core.Configuration;
using FamilyHub.Core.Household;
using FamilyHub.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FamilyHub.Tests.Core;

public sealed class StorageAndHouseholdTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "familyhub-tests", Guid.NewGuid().ToString("N"));

    public StorageAndHouseholdTests() => Directory.CreateDirectory(directory);

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private AppDataPaths Paths => new(Options.Create(new FamilyHubOptions { DataDirectory = directory }));

    [Fact]
    public async Task Json_store_round_trips_and_leaves_no_temp_file()
    {
        var store = new JsonFileStore<HouseholdSettings>(Path.Combine(directory, "h.json"), NullLogger.Instance);
        var settings = new HouseholdSettings { Name = "Familien Hansen", Members = [new FamilyMember { Name = "Charlotte" }] };

        await store.SaveAsync(settings);
        var loaded = store.Load();

        Assert.Equal("Familien Hansen", loaded!.Name);
        Assert.Equal("Charlotte", Assert.Single(loaded.Members).Name);
        Assert.False(File.Exists(store.FilePath + ".tmp"));
    }

    [Fact]
    public void Missing_file_loads_as_null()
    {
        var store = new JsonFileStore<HouseholdSettings>(Path.Combine(directory, "none.json"), NullLogger.Instance);

        Assert.Null(store.Load());
    }

    [Fact]
    public void Corrupt_file_is_moved_aside_so_the_app_can_start()
    {
        var path = Path.Combine(directory, "broken.json");
        File.WriteAllText(path, "{ this is not json");
        var store = new JsonFileStore<HouseholdSettings>(path, NullLogger.Instance);

        Assert.Null(store.Load());
        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(directory, "broken.json.corrupt-*"));
    }

    [Fact]
    public void Data_paths_cannot_escape_the_data_folder()
    {
        Assert.Throws<ArgumentException>(() => Paths.GetFilePath("../outside.json"));
        Assert.StartsWith(directory, Paths.GetDirectory("madplan"));
        Assert.True(Directory.Exists(Path.Combine(directory, "madplan")));
    }

    [Fact]
    public async Task Household_changes_are_saved_and_survive_a_restart()
    {
        var first = new HouseholdService(Paths, NullLogger<HouseholdService>.Instance);
        await first.UpdateAsync(h => h with { Name = "  Familien Hansen  ", Members = [new FamilyMember { Name = "Charlotte", Color = MemberColor.Rose }] });

        var afterRestart = new HouseholdService(Paths, NullLogger<HouseholdService>.Instance);

        Assert.Equal("Familien Hansen", afterRestart.Current.Name);
        Assert.Equal(MemberColor.Rose, Assert.Single(afterRestart.Current.Members).Color);
    }

    [Fact]
    public async Task All_screens_are_notified_even_if_one_subscriber_fails()
    {
        var service = new HouseholdService(Paths, NullLogger<HouseholdService>.Instance);
        HouseholdSettings? seen = null;
        service.Changed += _ => throw new InvalidOperationException("broken screen");
        service.Changed += s => seen = s;

        await service.UpdateAsync(h => h with { Name = "Familien" });

        Assert.Equal("Familien", seen?.Name);
    }

    [Fact]
    public void Normalize_cleans_up_members()
    {
        var id = Guid.NewGuid();
        var messy = new HouseholdSettings
        {
            Name = new string('x', 100),
            Members =
            [
                new FamilyMember { Id = id, Name = "  Anne Marie " },
                new FamilyMember { Id = id, Name = "Dublet" },
                new FamilyMember { Name = "   " },
                new FamilyMember { Name = "Kim", Color = (MemberColor)99 },
            ],
        };

        var clean = messy.Normalize();

        Assert.Equal(HouseholdSettings.MaxNameLength, clean.Name.Length);
        Assert.Equal(["Anne Marie", "Kim"], clean.Members.Select(m => m.Name));
        Assert.Equal(MemberColor.Sage, clean.Members[1].Color);
    }

    [Theory]
    [InlineData("Charlotte", "C")]
    [InlineData("anne marie", "AM")]
    [InlineData("Karl Johan Nielsen", "KJ")]
    [InlineData("", "?")]
    public void Initials_are_taken_from_the_first_two_names(string name, string initials)
    {
        Assert.Equal(initials, new FamilyMember { Name = name }.Initials);
    }

    [Fact]
    public void Next_free_color_skips_colors_in_use()
    {
        var household = new HouseholdSettings
        {
            Members = [new FamilyMember { Name = "A", Color = MemberColor.Sage }, new FamilyMember { Name = "B", Color = MemberColor.Sky }],
        };

        Assert.Equal(MemberColor.Terracotta, household.NextFreeColor());
    }
}
