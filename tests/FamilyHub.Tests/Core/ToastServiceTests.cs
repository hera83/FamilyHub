using FamilyHub.Core.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace FamilyHub.Tests.Core;

public class ToastServiceTests : IDisposable
{
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 9, 25, 8, 0, 0, TimeSpan.Zero));
    private readonly ToastService toasts;

    public ToastServiceTests() => toasts = new ToastService(time, NullLogger<ToastService>.Instance);

    public void Dispose() => toasts.Dispose();

    [Fact]
    public void Success_disappears_by_itself_after_the_standard_time()
    {
        toasts.Success("Gemt");

        time.Advance(ToastDefaults.Success - TimeSpan.FromMilliseconds(100));
        Assert.Single(toasts.Visible);

        time.Advance(TimeSpan.FromMilliseconds(200));
        Assert.Empty(toasts.Visible);
    }

    [Fact]
    public void Errors_stay_until_closed()
    {
        var error = toasts.Error("Kunne ikke gemme");

        time.Advance(TimeSpan.FromHours(1));
        Assert.Single(toasts.Visible);

        toasts.Dismiss(error.Id);
        Assert.Empty(toasts.Visible);
    }

    [Fact]
    public void Newest_toast_is_first()
    {
        toasts.Info("Første");
        toasts.Info("Anden");

        Assert.Equal(["Anden", "Første"], toasts.Visible.Select(t => t.Title));
    }

    [Fact]
    public void Repeated_message_is_counted_instead_of_stacked()
    {
        toasts.Warning("Ingen forbindelse");
        toasts.Warning("Ingen forbindelse");
        toasts.Warning("Ingen forbindelse");

        var toast = Assert.Single(toasts.Visible);
        Assert.Equal(3, toast.Occurrences);
    }

    [Fact]
    public void Repeating_a_message_restarts_its_countdown()
    {
        toasts.Success("Gemt");
        time.Advance(TimeSpan.FromSeconds(3));
        toasts.Success("Gemt");
        time.Advance(TimeSpan.FromSeconds(3));

        Assert.Single(toasts.Visible);
    }

    [Fact]
    public void Same_key_replaces_the_previous_toast()
    {
        toasts.Show(new ToastOptions { Title = "Synkroniserer …", Key = "sync" });
        toasts.Show(new ToastOptions { Title = "Synkroniseret", Level = ToastLevel.Success, Key = "sync" });

        var toast = Assert.Single(toasts.Visible);
        Assert.Equal("Synkroniseret", toast.Title);
    }

    [Fact]
    public void At_most_three_toasts_and_the_oldest_non_error_gives_way()
    {
        toasts.Error("Fejl 1");
        toasts.Info("Info 1");
        toasts.Info("Info 2");
        toasts.Info("Info 3");

        Assert.Equal(ToastDefaults.MaxVisible, toasts.Visible.Count);
        Assert.Equal(["Info 3", "Info 2", "Fejl 1"], toasts.Visible.Select(t => t.Title));
    }

    [Fact]
    public void A_new_toast_is_never_the_one_dropped_even_when_all_others_are_errors()
    {
        toasts.Error("Fejl 1");
        toasts.Error("Fejl 2");
        toasts.Error("Fejl 3");
        toasts.Success("Gemt");

        Assert.Equal(["Gemt", "Fejl 3", "Fejl 2"], toasts.Visible.Select(t => t.Title));
    }

    [Fact]
    public void Toast_with_an_action_gets_time_to_press_it()
    {
        var toast = toasts.Undoable("Mælk er fjernet", () => Task.CompletedTask);

        Assert.Equal("Fortryd", toast.Action!.Label);
        Assert.Equal(ToastDefaults.WithAction, toast.Duration);
    }

    [Fact]
    public void Paused_toast_does_not_expire_and_gets_a_little_time_after_resume()
    {
        var toast = toasts.Success("Gemt");
        time.Advance(TimeSpan.FromSeconds(3.5));

        toasts.Pause(toast.Id);
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Single(toasts.Visible);

        toasts.Resume(toast.Id);
        time.Advance(ToastDefaults.ResumeMinimum - TimeSpan.FromMilliseconds(100));
        Assert.Single(toasts.Visible);

        time.Advance(TimeSpan.FromMilliseconds(200));
        Assert.Empty(toasts.Visible);
    }

    [Fact]
    public async Task Action_runs_once_and_closes_the_toast()
    {
        var runs = 0;
        var toast = toasts.Undoable("Mælk er fjernet", () =>
        {
            runs++;
            return Task.CompletedTask;
        });

        await toasts.InvokeActionAsync(toast.Id);
        await toasts.InvokeActionAsync(toast.Id); // double tap

        Assert.Equal(1, runs);
        Assert.Empty(toasts.Visible);
    }

    [Fact]
    public async Task Failing_action_becomes_an_error_toast()
    {
        var toast = toasts.Undoable("Mælk er fjernet", () => throw new InvalidOperationException("disk full"));

        await toasts.InvokeActionAsync(toast.Id);

        var error = Assert.Single(toasts.Visible);
        Assert.Equal(ToastLevel.Error, error.Level);
    }

    [Fact]
    public void Changed_is_raised_on_show_and_expire()
    {
        var changes = 0;
        toasts.Changed += () => changes++;

        toasts.Info("Hej");
        time.Advance(ToastDefaults.Info + TimeSpan.FromSeconds(1));

        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task TryAsync_reports_failure_as_error_toast()
    {
        var ok = await toasts.TryAsync(() => throw new IOException("nope"), "Navnet blev ikke gemt");

        Assert.False(ok);
        Assert.Equal("Navnet blev ikke gemt", Assert.Single(toasts.Visible).Title);
    }
}
