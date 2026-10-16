using Avalonia.Controls;
using Avalonia.Input;
using PhotoOrganizer.Viewer.ViewModels;

namespace PhotoOrganizer.Viewer.Views;

public partial class SlideshowPreviewWindow : Window
{
    public SlideshowPreviewWindow()
    {
        InitializeComponent();

        KeyDown += (_, e) =>
        {
            if (DataContext is not SlideshowPreviewViewModel vm) return;

            switch (e.Key)
            {
                case Key.Escape:
                    vm.CloseCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.Space:
                    vm.TogglePauseCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.Left:
                    vm.PreviousCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.Right:
                    vm.NextCommand.Execute(null);
                    e.Handled = true;
                    break;
            }
        };
    }
}