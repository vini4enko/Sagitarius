using Avalonia.Controls;
using System;

namespace PhotoOrganizer.Viewer.Views;

public partial class ShowManagerWindow : Window
{
    public ShowManagerWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is PhotoOrganizer.Viewer.ViewModels.ShowManagerViewModel vm)
            _ = vm.LoadAsync();
    }
}