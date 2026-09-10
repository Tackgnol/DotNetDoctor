using System;
using System.Threading;
using System.Threading.Tasks;

namespace CodeQualityGaps;

public sealed class AsyncProblems
{
    public AsyncProblems()
    {
        Clicked += ButtonClicked;
    }

    public event EventHandler? Clicked;

    public async void FireAndForget()
    {
        await Task.Delay(1);
    }

    public Task MissingSuffix()
    {
        return Task.CompletedTask;
    }

    public Task CorrectAsync()
    {
        return Task.CompletedTask;
    }

    public async Task DelayAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken);
    }

    public void Register()
    {
        Action callback = async () => await Task.Delay(1);
        callback();
    }

    public Func<Task> RegisterAsync()
    {
        return async () => await Task.Delay(1);
    }

    private async void ButtonClicked(object? sender, EventArgs args)
    {
        await Task.Delay(1);
    }
}
