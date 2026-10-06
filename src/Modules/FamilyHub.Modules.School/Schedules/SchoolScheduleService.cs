using FamilyHub.Core.Storage;
using Microsoft.Extensions.Logging;

namespace FamilyHub.Modules.School.Schedules;

/// <summary>
/// The children's timetables, shared by every screen (<c>skole/skemaer.json</c> – a few kilobytes, so a JSON file is plenty).
/// Every change is a function of the newest version, so two screens editing different lessons never overwrite each other.
/// Screens subscribe to <see cref="Changed"/>.
/// </summary>
public sealed class SchoolScheduleService
{
    private readonly JsonFileStore<SchoolData> store;
    private readonly ILogger<SchoolScheduleService> logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private volatile SchoolData data;

    public SchoolScheduleService(IAppDataPaths paths, ILogger<SchoolScheduleService> logger)
    {
        this.logger = logger;
        store = new JsonFileStore<SchoolData>(paths.GetFilePath("skole/skemaer.json"), logger);
        data = (store.Load() ?? SchoolData.Empty).Normalized();
    }

    /// <summary>Raised after a successful save. May be raised from any thread – components use <c>InvokeAsync</c>.</summary>
    public event Action? Changed;

    public IReadOnlyList<SchoolSchedule> Schedules => data.Schedules;

    public SchoolSchedule? Find(Guid id) => data.Schedules.FirstOrDefault(s => s.Id == id);

    public async Task<SchoolSchedule> AddAsync(SchoolSchedule schedule, CancellationToken cancellationToken = default)
    {
        var added = schedule.Normalized();
        await SaveAsync(current =>
        {
            if (current.Schedules.Count >= SchoolData.MaxSchedules)
            {
                throw new InvalidOperationException("Der er ikke plads til flere skemaer.");
            }

            return current with { Schedules = [.. current.Schedules, added] };
        }, cancellationToken);
        return added;
    }

    /// <summary>Changes one schedule. Returns the saved version – null when it no longer exists (removed on another screen).</summary>
    public async Task<SchoolSchedule?> UpdateAsync(Guid id, Func<SchoolSchedule, SchoolSchedule> update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        SchoolSchedule? saved = null;
        await SaveAsync(current =>
        {
            if (current.Schedules.FirstOrDefault(s => s.Id == id) is not { } schedule)
            {
                return current;
            }

            saved = update(schedule).Normalized() with { Id = id };
            return current with { Schedules = [.. current.Schedules.Select(s => s.Id == id ? saved : s)] };
        }, cancellationToken);
        return saved;
    }

    /// <summary>Removes a schedule and returns it with its position, so "Fortryd" can put it back.</summary>
    public async Task<(SchoolSchedule Schedule, int Index)?> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        (SchoolSchedule, int)? removed = null;
        await SaveAsync(current =>
        {
            var index = current.Schedules.ToList().FindIndex(s => s.Id == id);
            if (index < 0)
            {
                return current;
            }

            removed = (current.Schedules[index], index);
            return current with { Schedules = [.. current.Schedules.Where(s => s.Id != id)] };
        }, cancellationToken);
        return removed;
    }

    /// <summary>Puts a removed schedule back where it was ("Fortryd").</summary>
    public Task RestoreAsync(SchoolSchedule schedule, int index, CancellationToken cancellationToken = default) =>
        SaveAsync(current =>
        {
            if (current.Schedules.Any(s => s.Id == schedule.Id))
            {
                return current;
            }

            var list = current.Schedules.ToList();
            list.Insert(Math.Clamp(index, 0, list.Count), schedule);
            return current with { Schedules = list };
        }, cancellationToken);

    private async Task SaveAsync(Func<SchoolData, SchoolData> change, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var updated = change(data).Normalized();
            await store.SaveAsync(updated, cancellationToken);
            data = updated;
        }
        finally
        {
            gate.Release();
        }

        Notify();
    }

    private void Notify()
    {
        if (Changed is not { } handlers)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "School schedule subscriber failed");
            }
        }
    }
}
