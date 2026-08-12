using CommunityToolkit.Mvvm.ComponentModel;
using VegaBeamTool.CameraUI;

namespace VegaBeamTool
{

    partial class MainWindowViewModel : ObservableObject
    {
        public MainWindowViewModel()
        {
            CameraShowView = new CameraShowView() { DataContext = new CameraShowViewModel() };
            if (CameraShowView.DataContext is CameraShowViewModel cameraShowViewModel)
            {
                cameraShowViewModel.RegisterUpdateImage(CameraShowView.UpdateBitmap);
            }
        }

        public void Shutdown()
        {
            if (CameraShowView.DataContext is CameraShowViewModel cameraShowViewModel)
            {
                cameraShowViewModel.Shutdown();
            }
        }

        [ObservableProperty]
        private CameraShowView _cameraShowView;

    }
}
