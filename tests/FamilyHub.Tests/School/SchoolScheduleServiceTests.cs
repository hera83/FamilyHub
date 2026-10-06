using FamilyHub.Core.Configuration;
using FamilyHub.Core.Storage;
using FamilyHub.Modules.School.Schedules;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FamilyHub.Tests.School;

public sealed class SchoolScheduleServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "familyhub-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private SchoolScheduleService Create() =>
        new(new AppDataPaths(Options.Create(new FamilyHubOptions { DataDirectory = directory })), NullLogger<SchoolScheduleService>.Instance);

    [Fact]
    public async Task Schedules_survive_a_restart()
    {
        var schedule = await Create().AddAsync(ScheduleLogicTests.Sample());

        var reloaded = Create().Find(schedule.Id);

        Assert.NotNull(reloaded);
        Assert.Equal("6.a", reloaded.ClassName);
        Assert.Equal(schedule.Cells.Count, reloaded.Cells.Count);
        Assert.Equal(schedule.Periods.Select(p => (p.Start, p.End, p.Label)), reloaded.Periods.Select(p => (p.Start, p.End, p.Label)));
        Assert.True(File.Exists(Path.Combine(directory, "skole", "skemaer.json")));
    }

    [Fact]
    public async Task Changes_build_on_the_newest_version_and_notify_every_screen()
    {
        var service = Create();
        var schedule = await service.AddAsync(ScheduleLogicTests.Sample());
        var changes = 0;
        service.Changed += () => changes++;

        // Two screens change different things one after the other – neither overwrites the other.
        await service.UpdateAsync(schedule.Id, s => s with { Grade = 7 });
        await service.UpdateAsync(schedule.Id, s => s with { ClassLetter = "C" });

        Assert.Equal("7.c", service.Find(schedule.Id)!.ClassName);
        Assert.Equal(2, changes);
        Assert.Null(await service.UpdateAsync(Guid.NewGuid(), s => s));
    }

    [Fact]
    public void Older_files_without_a_kind_of_school_are_folkeskole()
    {
        var file = Path.Combine(directory, "skole", "skemaer.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, """{ "schedules": [ { "id": "11111111-1111-4111-8111-111111111111", "grade": 6, "classLetter": "A" } ] }""");

        var schedule = Assert.Single(Create().Schedules);

        Assert.Equal(SchoolLevel.PrimarySchool, schedule.Level);
        Assert.Equal("6.a", schedule.ClassName);
    }

    [Fact]
    public async Task A_removed_schedule_comes_back_in_its_place()
    {
        var service = Create();
        var first = await service.AddAsync(ScheduleLogicTests.Sample());
        var second = await service.AddAsync(ScheduleLogicTests.Sample());

        var removed = await service.RemoveAsync(first.Id);
        Assert.Equal([second.Id], service.Schedules.Select(s => s.Id));

        await service.RestoreAsync(removed!.Value.Schedule, removed.Value.Index);
        Assert.Equal([first.Id, second.Id], service.Schedules.Select(s => s.Id));
    }
}
