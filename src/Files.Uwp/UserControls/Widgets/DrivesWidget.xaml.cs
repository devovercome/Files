﻿using Files.DataModels.NavigationControlItems;
using Files.Helpers;
using Files.Backend.Services.Settings;
using Files.ViewModels;
using Files.ViewModels.Widgets;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Toolkit.Uwp;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.Foundation.Collections;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Windows.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.Core;
using Files.Uwp.UserControls.Widgets;

namespace Files.UserControls.Widgets
{
    public class DriveCardItem : ObservableObject, IWidgetCardItem<DriveItem>
    {
        private BitmapImage thumbnail;
        private byte[] thumbnailData;

        public DriveItem Item { get; private set; }
        public bool HasThumbnail => thumbnail != null && thumbnailData != null;
        public BitmapImage Thumbnail
        {
            get => thumbnail;
            set => SetProperty(ref thumbnail, value);
        }

        public DriveCardItem(DriveItem item)
        {
            this.Item = item;
        }

        public async Task LoadCardThumbnailAsync(int overrideThumbnailSize = 32)
        {
            if (thumbnailData == null || thumbnailData.Length == 0)
            {
                thumbnailData = await FileThumbnailHelper.LoadIconFromPathAsync(Item.Path, Convert.ToUInt32(overrideThumbnailSize), Windows.Storage.FileProperties.ThumbnailMode.ListView);
                if (thumbnailData != null && thumbnailData.Length > 0)
                {
                    Thumbnail = await CoreApplication.MainView.DispatcherQueue.EnqueueAsync(() => thumbnailData.ToBitmapAsync(overrideThumbnailSize));
                }
            }
        }
    }

    public sealed partial class DrivesWidget : UserControl, IWidgetItemModel, INotifyPropertyChanged
    {
        private IUserSettingsService UserSettingsService { get; } = Ioc.Default.GetService<IUserSettingsService>();

        public delegate void DrivesWidgetInvokedEventHandler(object sender, DrivesWidgetInvokedEventArgs e);

        public event DrivesWidgetInvokedEventHandler DrivesWidgetInvoked;

        public delegate void DrivesWidgetNewPaneInvokedEventHandler(object sender, DrivesWidgetInvokedEventArgs e);

        public event DrivesWidgetNewPaneInvokedEventHandler DrivesWidgetNewPaneInvoked;

        public event PropertyChangedEventHandler PropertyChanged;

        public static ObservableCollection<DriveCardItem> ItemsAdded = new ObservableCollection<DriveCardItem>();

        private IShellPage associatedInstance;

        public IShellPage AppInstance
        {
            get => associatedInstance;
            set
            {
                if (value != associatedInstance)
                {
                    associatedInstance = value;
                    NotifyPropertyChanged(nameof(AppInstance));
                }
            }
        }

        public string WidgetName => nameof(DrivesWidget);

        public string AutomationProperties => "DrivesWidgetAutomationProperties/Name".GetLocalized();

        public string WidgetHeader => "Drives".GetLocalized();

        public bool IsWidgetSettingEnabled => UserSettingsService.WidgetsSettingsService.ShowDrivesWidget;

        public DrivesWidget()
        {
            InitializeComponent();
        }

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async void EjectDevice_Click(object sender, RoutedEventArgs e)
        {
            var item = ((MenuFlyoutItem)sender).DataContext as DriveItem;
            await DriveHelpers.EjectDeviceAsync(item.Path);
        }

        private async void OpenInNewTab_Click(object sender, RoutedEventArgs e)
        {
            var item = ((MenuFlyoutItem)sender).DataContext as DriveItem;
            if (await CheckEmptyDrive(item.Path))
            {
                return;
            }
            await NavigationHelpers.OpenPathInNewTab(item.Path);
        }

        private async void OpenInNewWindow_Click(object sender, RoutedEventArgs e)
        {
            var item = ((MenuFlyoutItem)sender).DataContext as DriveItem;
            if (await CheckEmptyDrive(item.Path))
            {
                return;
            }
            await NavigationHelpers.OpenPathInNewWindowAsync(item.Path);
        }

        private async void OpenDriveProperties_Click(object sender, RoutedEventArgs e)
        {
            var item = ((MenuFlyoutItem)sender).DataContext as DriveItem;
            await FilePropertiesHelpers.OpenPropertiesWindowAsync(item, associatedInstance);
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            string ClickedCard = (sender as Button).Tag.ToString();
            string NavigationPath = ClickedCard; // path to navigate

            if (await CheckEmptyDrive(NavigationPath))
            {
                return;
            }

            var ctrlPressed = Window.Current.CoreWindow.GetKeyState(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
            if (ctrlPressed)
            {
                await NavigationHelpers.OpenPathInNewTab(NavigationPath);
                return;
            }

            DrivesWidgetInvoked?.Invoke(this, new DrivesWidgetInvokedEventArgs()
            {
                Path = NavigationPath
            });
        }

        private async void Button_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (e.GetCurrentPoint(null).Properties.IsMiddleButtonPressed) // check middle click
            {
                string navigationPath = (sender as Button).Tag.ToString();
                if (await CheckEmptyDrive(navigationPath))
                {
                    return;
                }
                await NavigationHelpers.OpenPathInNewTab(navigationPath);
            }
        }

        public class DrivesWidgetInvokedEventArgs : EventArgs
        {
            public string Path { get; set; }
        }

        public bool ShowMultiPaneControls
        {
            get => AppInstance.PaneHolder?.IsMultiPaneEnabled ?? false;
        }

        private async void OpenInNewPane_Click(object sender, RoutedEventArgs e)
        {
            var item = ((MenuFlyoutItem)sender).DataContext as DriveItem;
            if (await CheckEmptyDrive(item.Path))
            {
                return;
            }
            DrivesWidgetNewPaneInvoked?.Invoke(this, new DrivesWidgetInvokedEventArgs()
            {
                Path = item.Path
            });
        }

        private void MenuFlyout_Opening(object sender, object e)
        {
            var newPaneMenuItem = (sender as MenuFlyout).Items.Single(x => x.Name == "OpenInNewPane");
            newPaneMenuItem.Visibility = ShowMultiPaneControls ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void MapNetworkDrive_Click(object sender, RoutedEventArgs e)
        {
            var connection = await AppServiceConnectionHelper.Instance;
            if (connection != null)
            {
                await connection.SendMessageAsync(new ValueSet()
                {
                    { "Arguments", "NetworkDriveOperation" },
                    { "netdriveop", "OpenMapNetworkDriveDialog" },
                    { "HWND", NativeWinApiHelper.CoreWindowHandle.ToInt64() }
                });
            }
        }

        private async void DisconnectNetworkDrive_Click(object sender, RoutedEventArgs e)
        {
            var item = ((MenuFlyoutItem)sender).DataContext as DriveItem;
            var connection = await AppServiceConnectionHelper.Instance;
            if (connection != null)
            {
                await connection.SendMessageAsync(new ValueSet()
                {
                    { "Arguments", "NetworkDriveOperation" },
                    { "netdriveop", "DisconnectNetworkDrive" },
                    { "drive", item.Path }
                });
            }
        }

        private void GoToStorageSense_Click(object sender, RoutedEventArgs e)
        {
            string clickedCard = (sender as Button).Tag.ToString();
            StorageSenseHelper.OpenStorageSense(clickedCard);
        }

        private async Task<bool> CheckEmptyDrive(string drivePath)
        {
            if (drivePath is not null)
            {
                var matchingDrive = App.DrivesManager.Drives.FirstOrDefault(x => drivePath.StartsWith(x.Path, StringComparison.Ordinal));
                if (matchingDrive != null && matchingDrive.Type == DriveType.CDRom && matchingDrive.MaxSpace == ByteSizeLib.ByteSize.FromBytes(0))
                {
                    bool ejectButton = await DialogDisplayHelper.ShowDialogAsync("InsertDiscDialog/Title".GetLocalized(), string.Format("InsertDiscDialog/Text".GetLocalized(), matchingDrive.Path), "InsertDiscDialog/OpenDriveButton".GetLocalized(), "Close".GetLocalized());
                    if (ejectButton)
                    {
                        await DriveHelpers.EjectDeviceAsync(matchingDrive.Path);
                    }
                    return true;
                }
            }
            return false;
        }

        public void Dispose()
        {

        }
    }
}