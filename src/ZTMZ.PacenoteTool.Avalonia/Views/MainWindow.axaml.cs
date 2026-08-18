using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Windowing;
using ZTMZ.PacenoteTool.Avalonia.ViewModels;

namespace ZTMZ.PacenoteTool.Avalonia.Views;

public sealed partial class MainWindow : FAAppWindow
{
    private readonly ListBox _navigationList;
    private readonly ContentControl _pageHost;
    private readonly IReadOnlyDictionary<string, Control> _pages;
    private MainWindowViewModel? _viewModel;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        var customTitleBar = this.FindControl<Grid>("CustomTitleBar") ??
            throw new InvalidOperationException("Custom title bar was not created.");
        TitleBar.ExtendsContentIntoTitleBar = true;
        customTitleBar.IsVisible = true;

        _navigationList = this.FindControl<ListBox>("NavigationList") ??
            throw new InvalidOperationException("Navigation list was not created.");
        _pageHost = this.FindControl<ContentControl>("PageHost") ??
            throw new InvalidOperationException("Page host was not created.");
        var homeNavigationItem = this.FindControl<ListBoxItem>("HomeNavigationItem") ??
            throw new InvalidOperationException("Home navigation item was not created.");

        _pages = new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase)
        {
            ["home"] = new HomeView(),
            ["codriver"] = new CoDriverView(),
            ["dashboard"] = new DashboardView()
        };

        _navigationList.SelectionChanged += OnNavigationSelectionChanged;
        DataContextChanged += OnDataContextChanged;
        Closed += (_, _) => DetachViewModel();
        _navigationList.SelectedItem = homeNavigationItem;
        _pageHost.Content = _pages["home"];
    }

    private void OnNavigationSelectionChanged(
        object? sender,
        SelectionChangedEventArgs eventArgs)
    {
        if (_navigationList.SelectedItem is ListBoxItem item &&
            item.Tag is string pageKey &&
            _pages.TryGetValue(pageKey, out var page))
        {
            _pageHost.Content = page;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs eventArgs)
    {
        DetachViewModel();
        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel != null)
        {
            _viewModel.NavigationRequested += OnNavigationRequested;
        }
    }

    private void DetachViewModel()
    {
        if (_viewModel != null)
        {
            _viewModel.NavigationRequested -= OnNavigationRequested;
            _viewModel = null;
        }
    }

    private void OnNavigationRequested(object? sender, string pageKey)
    {
        var item = _navigationList.Items
            .OfType<ListBoxItem>()
            .FirstOrDefault(candidate => string.Equals(
                candidate.Tag as string,
                pageKey,
                StringComparison.OrdinalIgnoreCase));
        if (item != null)
        {
            _navigationList.SelectedItem = item;
        }
    }
}
