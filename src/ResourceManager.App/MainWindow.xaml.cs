using System.Windows;
using ResourceManager.App.ViewModels;

namespace ResourceManager.App;

public partial class MainWindow : Window
{
    public MainWindow(BatchMappingViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DataContext is BatchMappingViewModel { IsIdle: true } && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not BatchMappingViewModel { IsIdle: true } viewModel || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files) return;
        viewModel.SetInput(files[0]);
        e.Handled = true;
    }
}
