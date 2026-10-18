using System.Collections;
using System.Reflection;
using Xunit;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ZTMZ.PacenoteTool.Avalonia.Views.GameSettings;

namespace ZTMZ.PacenoteTool.Avalonia.Tests;

public sealed class GameModCatalogPaneTest
{
    [AvaloniaTheory]
    [InlineData(GameModActionKind.Install)]
    [InlineData(GameModActionKind.Update)]
    [InlineData(GameModActionKind.Uninstall)]
    [InlineData(GameModActionKind.AddToExternalManager)]
    [InlineData(GameModActionKind.OpenExternalManager)]
    public Task ProgressAndResultsStayInsideTheTargetCard(GameModActionKind action) => RunAsync(async () =>
    {
        var manager = new ControlledManager();
        using var pane = CreatePane(manager);
        await Invoke(pane, "RefreshRowsAsync", false);
        var row = Row(pane, "first");
        var other = Row(pane, "second");
        var operation = Invoke(pane, "ExecuteActionAsync", Request(row, action));
        Assert.True(Visible(Get<ProgressBar>(row, "Progress")));
        Assert.True(Visible(Get<Button>(row, "Cancel")));
        Assert.True(Get<Button>(row, "Cancel").IsEnabled);
        Assert.False(Get<Panel>(row, "Actions").IsEnabled);
        Assert.False(Get<Panel>(other, "Actions").IsEnabled);
        Assert.False(Visible(Get<ProgressBar>(other, "Progress")));
        Assert.False(Visible(Field<ProgressBar>(pane, "_catalogProgress")));
        Assert.Equal(action, manager.Action);
        Assert.Equal("first", manager.ModId);

        manager.Progress!.Report(new GameModOperationProgress(.42, "gameMods.progress.downloading"));
        await Pump();
        Assert.Equal(.42, Get<ProgressBar>(row, "Progress").Value, 4);
        Assert.False(Get<ProgressBar>(row, "Progress").IsIndeterminate);
        Assert.Contains("42", Get<TextBlock>(row, "Message").Text);
        manager.Progress.Report(new GameModOperationProgress(null, "gameMods.progress.installing"));
        await Pump();
        Assert.True(Get<ProgressBar>(row, "Progress").IsIndeterminate);

        // A second action and refresh cannot replace a running card or its cancellation control.
        await Invoke(pane, "ExecuteActionAsync", Request(other, action));
        await Invoke(pane, "RefreshRowsAsync", false);
        Assert.Equal(1, manager.Executions);
        Assert.Same(row, Row(pane, "first"));
        manager.Completion.SetResult(new GameModOperationResult(true, "Operation finished", false));
        await operation;
        row = Row(pane, "first");
        Assert.Equal("Operation finished", Get<TextBlock>(row, "Message").Text);
        Assert.False(Visible(Get<ProgressBar>(row, "Progress")));
        Assert.False(Visible(Get<Button>(row, "Cancel")));
        Assert.True(Get<Panel>(row, "Actions").IsEnabled);
        Assert.False(Visible(Get<StackPanel>(Row(pane, "second"), "Operation")));
        // Late queued reports cannot overwrite the retained result, even after a refresh.
        manager.Progress.Report(new GameModOperationProgress(.9, "late-progress"));
        await Pump();
        await Invoke(pane, "RefreshRowsAsync", false);
        Assert.Equal("Operation finished", Get<TextBlock>(Row(pane, "first"), "Message").Text);
    });

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FailureOrCancellationRestoresActionsAndAllowsRetry(bool cancel) => RunAsync(async () =>
    {
        var manager = new ControlledManager();
        using var pane = CreatePane(manager);
        await Invoke(pane, "RefreshRowsAsync", false);
        var row = Row(pane, "first");
        var operation = Invoke(pane, "ExecuteActionAsync", Request(row, GameModActionKind.Install));
        if (cancel)
        {
            var button = Get<Button>(row, "Cancel");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(manager.Token.IsCancellationRequested);
            Assert.False(button.IsEnabled);
            manager.Progress!.Report(new GameModOperationProgress(.5, "late-progress"));
            await Pump();
            Assert.Equal(I18NLoader.Instance["gameMods.progress.cancelling"], Get<TextBlock>(row, "Message").Text);
            manager.Completion.SetException(new OperationCanceledException(manager.Token));
        }
        else
            manager.Completion.SetException(new IOException("Disk is full"));
        await operation;
        row = Row(pane, "first");
        Assert.Equal(cancel ? I18NLoader.Instance["gameMods.result.cancelled"] : "Disk is full",
            Get<TextBlock>(row, "Message").Text);
        Assert.True(Get<Panel>(row, "Actions").IsEnabled);
        Assert.False(Visible(Get<Button>(row, "Cancel")));
        var oldProgress = manager.Progress!;
        manager.Completion = new();
        var retry = Invoke(pane, "ExecuteActionAsync", Request(row, GameModActionKind.Install));
        oldProgress.Report(new GameModOperationProgress(.8, "old-operation"));
        await Pump();
        Assert.Equal(I18NLoader.Instance["gameMods.progress.preparing"], Get<TextBlock>(row, "Message").Text);
        manager.Completion.SetResult(new GameModOperationResult(true, "Retry finished", false));
        await retry;
        Assert.Equal("Retry finished", Get<TextBlock>(Row(pane, "first"), "Message").Text);
    });

    [AvaloniaFact]
    public Task DisposeCancelsWithoutInvalidatingTheRunningOperation() => RunAsync(async () =>
    {
        var manager = new ControlledManager();
        using var pane = CreatePane(manager);
        await Invoke(pane, "RefreshRowsAsync", false);
        var row = Row(pane, "first");
        var operation = Invoke(pane, "ExecuteActionAsync", Request(row, GameModActionKind.Install));
        pane.Dispose();
        Assert.True(manager.Token.IsCancellationRequested);
        manager.Completion.SetCanceled(manager.Token);
        await operation;
        Assert.False(Get<Panel>(row, "Actions").IsEnabled);
        Assert.False(Visible(Get<ProgressBar>(row, "Progress")));
    });

    private static GameModCatalogPane CreatePane(ControlledManager manager)
    {
        if (I18NLoader.Instance.Resources is null)
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "ZTMZ.PacenoteTool.I18N", "lang")))
                root = root.Parent;
            Assert.NotNull(root);
            I18NLoader.Instance.Initialize([Path.Combine(root.FullName, "src", "ZTMZ.PacenoteTool.I18N", "lang")]);
        }
        var game = DispatchProxy.Create<IGame, UnusedGame>();
        var pane = new GameModCatalogPane(manager);
        pane.InitializeWithGame(game);
        typeof(GameModCatalogPane).GetField("_mods", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(pane, new[] { Mod("first"), Mod("second") });
        return pane;
    }
    private static GameModDefinition Mod(string id) => new(id,
        new Dictionary<string, string> { ["en-us"] = id },
        new Dictionary<string, string> { ["en-us"] = "Test mod description" },
        "Test author", "1.0", new Uri("https://example.com/package.zip"),
        new Uri("https://example.com/release"), new string('0', 64), "");
    private static object Row(GameModCatalogPane pane, string id)
    {
        var row = Field<IDictionary>(pane, "_rows")[id];
        Assert.True(row is not null, Field<TextBlock>(pane, "_catalogStatus").Text);
        return row;
    }
    private static T Field<T>(object target, string name) => (T)target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static T Get<T>(object target, string name) => (T)target.GetType().GetProperty(name)!.GetValue(target)!;
    private static object Request(object row, GameModActionKind action) => Get<Panel>(row, "Actions")
        .Children.OfType<Button>().Select(button => button.Tag!)
        .Single(request => Get<GameModActionKind>(request, "Action") == action);
    private static Task Invoke(object target, string method, params object[] args) => (Task)target.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args)!;
    private static bool Visible(Control control) => control.IsVisible;
    private static async Task Pump() => await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    private static Task RunAsync(Func<Task> test) => test();

    public class UnusedGame : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            throw new InvalidOperationException("The fake mod manager does not read game state.");
    }

    private sealed class ControlledManager : IGameModManager, IGameWithModCatalog
    {
        public string ModCatalogGameId => "test";
        public Uri ModRepositoryUri => new("https://example.com/catalog.json");
        public IGameModManager ModManager => this;
        public TaskCompletionSource<GameModOperationResult> Completion = new();
        public IProgress<GameModOperationProgress>? Progress;
        public CancellationToken Token;
        public GameModActionKind Action;
        public string? ModId;
        public int Executions;
        public Task<GameModStatus> GetStatusAsync(GameModDefinition mod, IReadOnlyList<GameModDefinition> catalog,
            IGame game, CancellationToken token) => Task.FromResult(new GameModStatus(
                GameModInstallState.NotInstalled, "gameMods.status.notInstalled", EnabledActions:
                new HashSet<GameModActionKind>(Enum.GetValues<GameModActionKind>())));
        public Task<GameModOperationResult> ExecuteActionAsync(GameModDefinition mod,
            IReadOnlyList<GameModDefinition> catalog, GameModActionKind action, IGame game,
            IProgress<GameModOperationProgress>? progress, CancellationToken token)
        {
            Progress = progress;
            Token = token;
            Action = action;
            ModId = mod.Id;
            Executions++;
            return Completion.Task;
        }
    }
}
