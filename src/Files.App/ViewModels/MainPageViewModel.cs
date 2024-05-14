// Copyright (c) 2024 Files Community
// Licensed under the MIT License. See the LICENSE.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System.Windows.Input;
using Windows.System;
using Files.App.Helpers;

namespace Files.App.ViewModels
{
	/// <summary>
	/// Represents ViewModel of <see cref="MainPage"/>.
	/// </summary>
	public sealed class MainPageViewModel : ObservableObject
	{
		// Dependency injections

		private IAppearanceSettingsService AppearanceSettingsService { get; } = Ioc.Default.GetRequiredService<IAppearanceSettingsService>();
		private INetworkDrivesService NetworkDrivesService { get; } = Ioc.Default.GetRequiredService<INetworkDrivesService>();
		private IUserSettingsService UserSettingsService { get; } = Ioc.Default.GetRequiredService<IUserSettingsService>();
		private IResourcesService ResourcesService { get; } = Ioc.Default.GetRequiredService<IResourcesService>();
		private DrivesViewModel DrivesViewModel { get; } = Ioc.Default.GetRequiredService<DrivesViewModel>();

		// Properties

		public static ObservableCollection<TabBarItem> AppInstances { get; private set; } = [];

		public List<ITabBar> MultitaskingControls { get; } = [];

		public ITabBar? MultitaskingControl { get; set; }

		private TabBarItem? selectedTabItem;
		public TabBarItem? SelectedTabItem
		{
			get => selectedTabItem;
			set => SetProperty(ref selectedTabItem, value);
		}

		private bool shouldViewControlBeDisplayed;
		public bool ShouldViewControlBeDisplayed
		{
			get => shouldViewControlBeDisplayed;
			set => SetProperty(ref shouldViewControlBeDisplayed, value);
		}

		private bool shouldPreviewPaneBeActive;
		public bool ShouldPreviewPaneBeActive
		{
			get => shouldPreviewPaneBeActive;
			set => SetProperty(ref shouldPreviewPaneBeActive, value);
		}

		private bool shouldPreviewPaneBeDisplayed;
		public bool ShouldPreviewPaneBeDisplayed
		{
			get => shouldPreviewPaneBeDisplayed;
			set => SetProperty(ref shouldPreviewPaneBeDisplayed, value);
		}

		public Stretch AppThemeBackgroundImageFit
			=> AppearanceSettingsService.AppThemeBackgroundImageFit;

		public float AppThemeBackgroundImageOpacity
			=> AppearanceSettingsService.AppThemeBackgroundImageOpacity;

		public ImageSource? AppThemeBackgroundImageSource =>
			string.IsNullOrEmpty(AppearanceSettingsService.AppThemeBackgroundImageSource)
				? null
				: new BitmapImage(new Uri(AppearanceSettingsService.AppThemeBackgroundImageSource, UriKind.RelativeOrAbsolute));

		public VerticalAlignment AppThemeBackgroundImageVerticalAlignment
			=> AppearanceSettingsService.AppThemeBackgroundImageVerticalAlignment;

		public HorizontalAlignment AppThemeBackgroundImageHorizontalAlignment
			=> AppearanceSettingsService.AppThemeBackgroundImageHorizontalAlignment;


		// Commands

		public ICommand NavigateToNumberedTabKeyboardAcceleratorCommand { get; }

		// Constructor

		public MainPageViewModel()
		{
			NavigateToNumberedTabKeyboardAcceleratorCommand = new RelayCommand<KeyboardAcceleratorInvokedEventArgs>(ExecuteNavigateToNumberedTabKeyboardAcceleratorCommand);

			AppearanceSettingsService.PropertyChanged += (s, e) =>
			{
				switch (e.PropertyName)
				{
					case nameof(AppearanceSettingsService.AppThemeBackgroundImageSource):
						OnPropertyChanged(nameof(AppThemeBackgroundImageSource));
						break;
					case nameof(AppearanceSettingsService.AppThemeBackgroundImageOpacity):
						OnPropertyChanged(nameof(AppThemeBackgroundImageOpacity));
						break;
					case nameof(AppearanceSettingsService.AppThemeBackgroundImageFit):
						OnPropertyChanged(nameof(AppThemeBackgroundImageFit));
						break;
					case nameof(AppearanceSettingsService.AppThemeBackgroundImageVerticalAlignment):
						OnPropertyChanged(nameof(AppThemeBackgroundImageVerticalAlignment));
						break;
					case nameof(AppearanceSettingsService.AppThemeBackgroundImageHorizontalAlignment):
						OnPropertyChanged(nameof(AppThemeBackgroundImageHorizontalAlignment));
						break;
				}
			};
		}

		// Methods

		public async Task OnNavigatedToAsync(NavigationEventArgs e)
		{
			if (e.NavigationMode == NavigationMode.Back)
				return;

			var parameter = e.Parameter;
			var ignoreStartupSettings = false;
			if (parameter is MainPageNavigationArguments mainPageNavigationArguments)
			{
				parameter = mainPageNavigationArguments.Parameter;
				ignoreStartupSettings = mainPageNavigationArguments.IgnoreStartupSettings;
			}

			if (parameter is null || (parameter is string eventStr && string.IsNullOrEmpty(eventStr)))
			{
				try
				{
					// add last session tabs to closed tabs stack if those tabs are not about to be opened
					if (!UserSettingsService.AppSettingsService.RestoreTabsOnStartup && !UserSettingsService.GeneralSettingsService.ContinueLastSessionOnStartUp && UserSettingsService.GeneralSettingsService.LastSessionTabList != null)
					{
						var items = new CustomTabViewItemParameter[UserSettingsService.GeneralSettingsService.LastSessionTabList.Count];
						for (int i = 0; i < items.Length; i++)
							items[i] = CustomTabViewItemParameter.Deserialize(UserSettingsService.GeneralSettingsService.LastSessionTabList[i]);

						BaseTabBar.PushRecentTab(items);
					}

					if (UserSettingsService.AppSettingsService.RestoreTabsOnStartup)
					{
						UserSettingsService.AppSettingsService.RestoreTabsOnStartup = false;
						if (UserSettingsService.GeneralSettingsService.LastSessionTabList is not null)
						{
							foreach (string tabArgsString in UserSettingsService.GeneralSettingsService.LastSessionTabList)
							{
								var tabArgs = CustomTabViewItemParameter.Deserialize(tabArgsString);
								await NavigationHelpers.AddNewTabByParamAsync(tabArgs.InitialPageType, tabArgs.NavigationParameter);
							}

							if (!UserSettingsService.GeneralSettingsService.ContinueLastSessionOnStartUp)
								UserSettingsService.GeneralSettingsService.LastSessionTabList = null;
						}
					}
					else if (UserSettingsService.GeneralSettingsService.OpenSpecificPageOnStartup &&
						UserSettingsService.GeneralSettingsService.TabsOnStartupList is not null)
					{
						foreach (string path in UserSettingsService.GeneralSettingsService.TabsOnStartupList)
							await NavigationHelpers.AddNewTabByPathAsync(typeof(PaneHolderPage), path, true);
					}
					else if (UserSettingsService.GeneralSettingsService.ContinueLastSessionOnStartUp &&
						(UserSettingsService.GeneralSettingsService.LastSessionTabList is not null ||
						UserSettingsService.GeneralSettingsService.LastAppsTabsWithIDList is not null)
						)
					{
						var isRestored = TabsManageHelper.RestoreLastAppsTabs(this);
						var lastSessionTabList = UserSettingsService.GeneralSettingsService.LastSessionTabList.ToList();
						var defaultArg = new CustomTabViewItemParameter() { InitialPageType = typeof(PaneHolderPage), NavigationParameter = "Home" };
						var defaultArgStrList = new List<string>() { defaultArg.Serialize() };
						if (isRestored && !lastSessionTabList.SequenceEqual(defaultArgStrList))
						{
							await NavigationHelpers.OpenTabsInNewWindowAsync(lastSessionTabList);
						}
						else if (!isRestored)
						{
							foreach (string tabArgsString in UserSettingsService.GeneralSettingsService.LastSessionTabList)
							{
								var tabArgs = CustomTabViewItemParameter.Deserialize(tabArgsString);
								await NavigationHelpers.AddNewTabByParamAsync(tabArgs.InitialPageType, tabArgs.NavigationParameter);
							}
						}
						UserSettingsService.GeneralSettingsService.LastSessionTabList = new List<string> { defaultArg.Serialize() };
					}
					else
					{
						await NavigationHelpers.AddNewTabAsync();
					}
				}
				catch
				{
					await NavigationHelpers.AddNewTabAsync();
				}
			}
			else
			{
				if (!ignoreStartupSettings)
				{
					try
					{
						if (UserSettingsService.GeneralSettingsService.OpenSpecificPageOnStartup &&
								UserSettingsService.GeneralSettingsService.TabsOnStartupList is not null)
						{
							foreach (string path in UserSettingsService.GeneralSettingsService.TabsOnStartupList)
								await NavigationHelpers.AddNewTabByPathAsync(typeof(PaneHolderPage), path, true);
						}
						else if (UserSettingsService.GeneralSettingsService.ContinueLastSessionOnStartUp &&
							UserSettingsService.GeneralSettingsService.LastSessionTabList is not null &&
							AppInstances.Count == 0)
						{
							foreach (string tabArgsString in UserSettingsService.GeneralSettingsService.LastSessionTabList)
							{
								var tabArgs = CustomTabViewItemParameter.Deserialize(tabArgsString);
								await NavigationHelpers.AddNewTabByParamAsync(tabArgs.InitialPageType, tabArgs.NavigationParameter);
							}
						}
					}
					catch { }
				}

				if (parameter is string navArgs)
					await NavigationHelpers.AddNewTabByPathAsync(typeof(PaneHolderPage), navArgs, true);
				else if (parameter is PaneNavigationArguments paneArgs)
					await NavigationHelpers.AddNewTabByParamAsync(typeof(PaneHolderPage), paneArgs);
				else if (parameter is CustomTabViewItemParameter tabArgs)
					await NavigationHelpers.AddNewTabByParamAsync(tabArgs.InitialPageType, tabArgs.NavigationParameter);
			}

			// Load the app theme resources
			ResourcesService.LoadAppResources(AppearanceSettingsService);

			await Task.WhenAll(
				DrivesViewModel.UpdateDrivesAsync(),
				NetworkDrivesService.UpdateDrivesAsync());
		}

		// Command methods

		private void ExecuteNavigateToNumberedTabKeyboardAcceleratorCommand(KeyboardAcceleratorInvokedEventArgs? e)
		{
			int indexToSelect = e!.KeyboardAccelerator.Key switch
			{
				VirtualKey.Number1 => 0,
				VirtualKey.Number2 => 1,
				VirtualKey.Number3 => 2,
				VirtualKey.Number4 => 3,
				VirtualKey.Number5 => 4,
				VirtualKey.Number6 => 5,
				VirtualKey.Number7 => 6,
				VirtualKey.Number8 => 7,
				VirtualKey.Number9 => AppInstances.Count - 1,
				_ => AppInstances.Count - 1,
			};

			// Only select the tab if it is in the list
			if (indexToSelect < AppInstances.Count)
				App.AppModel.TabStripSelectedIndex = indexToSelect;

			e.Handled = true;
		}

	}
}
