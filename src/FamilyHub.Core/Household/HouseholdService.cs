using FamilyHub.Core.Storage;
using Microsoft.Extensions.Logging;

namespace FamilyHub.Core.Household;

/// <summary>
/// Household settings shared by every screen. Changes made on one screen
/// are pushed to all others through <see cref="Changed"/>.
/// </summary>
public interface IHouseholdService
{
    HouseholdSettings Current { get; }

    /// <summary>Raised after a successful save. May be raised from any thread.</summary>
    event Action<HouseholdSettings>? Changed;

    /// <summary>Applies <paramref name="update"/>, validates, saves to disk and notifies all screens.</summary>
    Task<HouseholdSettings> UpdateAsync(Func<HouseholdSettings, HouseholdSettings> update, CancellationToken cancellationToken = default);
}

internal sealed class HouseholdService : IHouseholdService
{
    private readonly JsonFileStore<HouseholdSettings> store;
    private readonly ILogger<HouseholdService> logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private volatile HouseholdSettings current;

    public HouseholdService(IAppDataPaths paths, ILogger<HouseholdService> logger)
    {
        this.logger = logger;
        store = new JsonFileStore<HouseholdSettings>(paths.GetFilePath("household.json"), logger);
        current = (store.Load() ?? HouseholdSettings.Empty).Normalize();
    }

    public HouseholdSettings Current => current;

    public event Action<HouseholdSettings>? Changed;

    public async Task<HouseholdSettings> UpdateAsync(Func<HouseholdSettings, HouseholdSettings> update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        HouseholdSettings updated;
        await gate.WaitAsync(cancellationToken);
        try
        {
            updated = update(current).Normalize();
            await store.SaveAsync(updated, cancellationToken);
            current = updated;
        }
        finally
        {
            gate.Release();
        }

        Notify(updated);
        return updated;
    }

    private void Notify(HouseholdSettings settings)
    {
        if (Changed is not { } handlers)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action<HouseholdSettings>>())
        {
            try
            {
                handler(settings);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Household change subscriber failed");
            }
        }
    }
}
