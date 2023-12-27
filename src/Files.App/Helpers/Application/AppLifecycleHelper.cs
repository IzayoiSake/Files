// Copyright (c) 2023 Files Community
// Licensed under the MIT License. See the LICENSE.

using CommunityToolkit.WinUI.Notifications;
using Files.App.Services.DateTimeFormatter;
using Files.App.Services.Settings;
using Files.App.Storage.FtpStorage;
using Files.App.Storage.NativeStorage;
using Files.App.ViewModels.Settings;
using Files.Core.Services.SizeProvider;
using Files.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Text.Json;
using Windows.Storage;
using Windows.System;
using Windows.UI.Notifications;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Files.App.Helpers
{
	/// <summary>
	/// Provides static helper to manage app lifecycle.
	/// </summary>
	public static class AppLifecycleHelper
	{
		/// <summary>
		/// Initializes the app components.
		/// </summary>
		public static async Task InitializeAppComponentsAsync()
		{
			var userSettingsService = Ioc.Default.GetRequiredService<IUserSettingsService>();
			var addItemService = Ioc.Default.GetRequiredService<IAddItemService>();
			var generalSettingsService = userSettingsService.GeneralSettingsService;

			// Start off a list of tasks we need to run before we can continue startup
			await Task.WhenAll(
				OptionalTaskAsync(CloudDrivesManager.UpdateDrivesAsync(), generalSettingsService.ShowCloudDrivesSection),
				App.LibraryManager.UpdateLibrariesAsync(),
				OptionalTaskAsync(WSLDistroManager.UpdateDrivesAsync(), generalSettingsService.ShowWslSection),
				OptionalTaskAsync(App.FileTagsManager.UpdateFileTagsAsync(), generalSettingsService.ShowFileTagsSection),
				App.QuickAccessManager.InitializeAsync()
			);

			await Task.WhenAll(
				JumpListHelper.InitializeUpdatesAsync(),
				addItemService.InitializeAsync(),
				ContextMenu.WarmUpQueryContextMenuAsync()
			);

			FileTagsHelper.UpdateTagsDb();

			await CheckAppUpdate();

			static Task OptionalTaskAsync(Task task, bool condition)
			{
				if (condition)
					return task;

				return Task.CompletedTask;
			}
		}

		/// <summary>
		/// Checks application updates and download if available.
		/// </summary>
		public static async Task CheckAppUpdate()
		{
			var updateService = Ioc.Default.GetRequiredService<IUpdateService>();

			await updateService.CheckForUpdatesAsync();
			await updateService.DownloadMandatoryUpdatesAsync();
			await updateService.CheckAndUpdateFilesLauncherAsync();
			await updateService.CheckLatestReleaseNotesAsync();
		}

		/// <summary>
		/// Configures AppCenter service, such as Analytics and Crash Report.
		/// </summary>
		public static void ConfigureAppCenter()
		{
			try
			{
				if (!Microsoft.AppCenter.AppCenter.Configured)
				{
					Microsoft.AppCenter.AppCenter.Start(
						Constants.AutomatedWorkflowInjectionKeys.AppCenterSecret,
						typeof(Microsoft.AppCenter.Analytics.Analytics),
						typeof(Microsoft.AppCenter.Crashes.Crashes));
				}
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Failed to start AppCenter service.");
			}
		}

		/// <summary>
		/// Configures DI (dependency injection) container.
		/// </summary>
		public static IHost ConfigureHost()
		{
			return Host.CreateDefaultBuilder()
				.UseEnvironment(ApplicationService.AppEnvironment.ToString())
				.ConfigureLogging(builder => builder
					.AddProvider(new FileLoggerProvider(Path.Combine(ApplicationData.Current.LocalFolder.Path, "debug.log")))
					.SetMinimumLevel(LogLevel.Information))
				.ConfigureServices(services => services
					// Settings services
					.AddSingleton<IUserSettingsService, UserSettingsService>()
					.AddSingleton<IAppearanceSettingsService, AppearanceSettingsService>(sp => new AppearanceSettingsService(((UserSettingsService)sp.GetRequiredService<IUserSettingsService>()).GetSharingContext()))
					.AddSingleton<IGeneralSettingsService, GeneralSettingsService>(sp => new GeneralSettingsService(((UserSettingsService)sp.GetRequiredService<IUserSettingsService>()).GetSharingContext()))
					.AddSingleton<IFoldersSettingsService, FoldersSettingsService>(sp => new FoldersSettingsService(((UserSettingsService)sp.GetRequiredService<IUserSettingsService>()).GetSharingContext()))
					.AddSingleton<IApplicationSettingsService, ApplicationSettingsService>(sp => new ApplicationSettingsService(((UserSettingsService)sp.GetRequiredService<IUserSettingsService>()).GetSharingContext()))
					.AddSingleton<IInfoPaneSettingsService, InfoPaneSettingsService>(sp => new InfoPaneSettingsService(((UserSettingsService)sp.GetRequiredService<IUserSettingsService>()).GetSharingContext()))
					.AddSingleton<ILayoutSettingsService, LayoutSettingsService>(sp => new LayoutSettingsService(((UserSettingsService)sp.GetRequiredService<IUserSettingsService>()).GetSharingContext()))
					.AddSingleton<IAppSettingsService, AppSettingsService>(sp => new AppSettingsService(((UserSettingsService)sp.GetRequiredService<IUserSettingsService>()).GetSharingContext()))
					.AddSingleton<IFileTagsSettingsService, FileTagsSettingsService>()
					// Contexts
					.AddSingleton<IPageContext, PageContext>()
					.AddSingleton<IContentPageContext, ContentPageContext>()
					.AddSingleton<IDisplayPageContext, DisplayPageContext>()
					.AddSingleton<IWindowContext, WindowContext>()
					.AddSingleton<IMultitaskingContext, MultitaskingContext>()
					.AddSingleton<ITagsContext, TagsContext>()
					// Services
					.AddSingleton<IDialogService, DialogService>()
					.AddSingleton<IImageService, ImagingService>()
					.AddSingleton<IThreadingService, ThreadingService>()
					.AddSingleton<ILocalizationService, LocalizationService>()
					.AddSingleton<ICloudDetector, CloudDetector>()
					.AddSingleton<IFileTagsService, FileTagsService>()
					.AddSingleton<ICommandManager, CommandManager>()
					.AddSingleton<IModifiableCommandManager, ModifiableCommandManager>()
					.AddSingleton<IApplicationService, ApplicationService>()
					.AddSingleton<IStorageService, NativeStorageService>()
					.AddSingleton<IFtpStorageService, FtpStorageService>()
					.AddSingleton<IAddItemService, AddItemService>()
#if STABLE || PREVIEW
					.AddSingleton<IUpdateService, SideloadUpdateService>()
#else
					.AddSingleton<IUpdateService, UpdateService>()
#endif
					.AddSingleton<IPreviewPopupService, PreviewPopupService>()
					.AddSingleton<IDateTimeFormatterFactory, DateTimeFormatterFactory>()
					.AddSingleton<IDateTimeFormatter, UserDateTimeFormatter>()
					.AddSingleton<IVolumeInfoFactory, VolumeInfoFactory>()
					.AddSingleton<ISizeProvider, UserSizeProvider>()
					.AddSingleton<IQuickAccessService, QuickAccessService>()
					.AddSingleton<IResourcesService, ResourcesService>()
					.AddSingleton<IJumpListService, JumpListService>()
					.AddSingleton<IRemovableDrivesService, RemovableDrivesService>()
					.AddSingleton<INetworkDrivesService, NetworkDrivesService>()
					.AddSingleton<IStartMenuService, StartMenuService>()
					// ViewModels
					.AddSingleton<MainPageViewModel>()
					.AddSingleton<InfoPaneViewModel>()
					.AddSingleton<SidebarViewModel>()
					.AddSingleton<SettingsViewModel>()
					.AddSingleton<DrivesViewModel>()
					.AddSingleton<NetworkDrivesViewModel>()
					.AddSingleton<StatusCenterViewModel>()
					.AddSingleton<AppearanceViewModel>()
					.AddTransient<HomeViewModel>()
					// Utilities
					.AddSingleton<QuickAccessManager>()
					.AddSingleton<StorageHistoryWrapper>()
					.AddSingleton<FileTagsManager>()
					.AddSingleton<RecentItems>()
					.AddSingleton<LibraryManager>()
					.AddSingleton<AppModel>()
				).Build();
		}

		/// <summary>
		/// Saves saves all opened tabs to the app cache.
		/// </summary>
		public static void SaveSessionTabs()
		{
			var userSettingsService = Ioc.Default.GetRequiredService<IUserSettingsService>();

			userSettingsService.GeneralSettingsService.LastSessionTabList = MainPageViewModel.AppInstances.DefaultIfEmpty().Select(tab =>
			{
				if (tab is not null && tab.NavigationParameter is not null)
				{
					return tab.NavigationParameter.Serialize();
				}
				else
				{
					var defaultArg = new CustomTabViewItemParameter()
					{
						InitialPageType = typeof(PaneHolderPage),
						NavigationParameter = "Home"
					};

					return defaultArg.Serialize();
				}
			})
			.ToList();
			RemoveThisInstanceTabs();
		}

		/// <summary>
		/// Shows exception on the Debug Output and sends Toast Notification to the Windows Notification Center.
		/// </summary>
		public static void HandleAppUnhandledException(Exception? ex, bool showToastNotification)
		{
			StringBuilder formattedException = new()
			{
				Capacity = 200
			};

			formattedException.AppendLine("--------- UNHANDLED EXCEPTION ---------");

			if (ex is not null)
			{
				formattedException.AppendLine($">>>> HRESULT: {ex.HResult}");

				if (ex.Message is not null)
				{
					formattedException.AppendLine("--- MESSAGE ---");
					formattedException.AppendLine(ex.Message);
				}
				if (ex.StackTrace is not null)
				{
					formattedException.AppendLine("--- STACKTRACE ---");
					formattedException.AppendLine(ex.StackTrace);
				}
				if (ex.Source is not null)
				{
					formattedException.AppendLine("--- SOURCE ---");
					formattedException.AppendLine(ex.Source);
				}
				if (ex.InnerException is not null)
				{
					formattedException.AppendLine("--- INNER ---");
					formattedException.AppendLine(ex.InnerException.ToString());
				}
			}
			else
			{
				formattedException.AppendLine("Exception data is not available.");
			}

			formattedException.AppendLine("---------------------------------------");

			Debug.WriteLine(formattedException.ToString());

			// Please check "Output Window" for exception details (View -> Output Window) (CTRL + ALT + O)
			Debugger.Break();

			SaveSessionTabs();
			App.Logger.LogError(ex, ex?.Message ?? "An unhandled error occurred.");

			if (!showToastNotification)
				return;

			var toastContent = new ToastContent()
			{
				Visual = new()
				{
					BindingGeneric = new ToastBindingGeneric()
					{
						Children =
						{
							new AdaptiveText()
							{
								Text = "ExceptionNotificationHeader".GetLocalizedResource()
							},
							new AdaptiveText()
							{
								Text = "ExceptionNotificationBody".GetLocalizedResource()
							}
						},
						AppLogoOverride = new()
						{
							Source = "ms-appx:///Assets/error.png"
						}
					}
				},
				Actions = new ToastActionsCustom()
				{
					Buttons =
					{
						new ToastButton("ExceptionNotificationReportButton".GetLocalizedResource(), Constants.GitHub.BugReportUrl)
						{
							ActivationType = ToastActivationType.Protocol
						}
					}
				},
				ActivationType = ToastActivationType.Protocol
			};

			// Create the toast notification
			var toastNotification = new ToastNotification(toastContent.GetXml());

			// And send the notification
			ToastNotificationManager.CreateToastNotifier().Show(toastNotification);

			// Restart the app
			var userSettingsService = Ioc.Default.GetRequiredService<IUserSettingsService>();
			var lastSessionTabList = userSettingsService.GeneralSettingsService.LastSessionTabList;

			if (userSettingsService.GeneralSettingsService.LastCrashedTabList?.SequenceEqual(lastSessionTabList) ?? false)
			{
				// Avoid infinite restart loop
				userSettingsService.GeneralSettingsService.LastSessionTabList = null;
			}
			else
			{
				userSettingsService.AppSettingsService.RestoreTabsOnStartup = true;
				userSettingsService.GeneralSettingsService.LastCrashedTabList = lastSessionTabList;

				// Try to re-launch and start over
				MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(async () =>
				{
					await Launcher.LaunchUriAsync(new Uri("files-uwp:"));
				})
				.Wait(100);
			}
			Process.GetCurrentProcess().Kill();
		}

		/// <summary>
		/// Variable definitions of shared memory, used to record multi-window Tabs
		/// </summary>
		private const long defaultBufferSize = 1024;

		private const string sharedMemoryHeaderName = "FilesAppTabsWithID";
		private static MemoryMappedFile? sharedMemoryHeader;

		private static long sharedMemoryNameDefaultSuffix = 0;
		private static string defaultSharedMemoryName = sharedMemoryHeaderName + sharedMemoryNameDefaultSuffix.ToString();
		private static long sharedMemoryNameSuffix = sharedMemoryNameDefaultSuffix;
		private static long sharedMemoryNameSuffixStep = 1;
		private static string sharedMemoryName = defaultSharedMemoryName;
		public static string instanceId = Process.GetCurrentProcess().Id.ToString();
		private static MemoryMappedFile? sharedMemory;

		private static IUserSettingsService userSettingsService = Ioc.Default.GetRequiredService<IUserSettingsService>();
		private static List<TabItemWithIDArguments> tabsWithIdArgList = new List<TabItemWithIDArguments>();

		/// <summary>
		/// Methods for basic operations on shared memory
		/// </summary>
		private static bool WriteMemory(MemoryMappedFile memory, string dataBuffer)
		{
			try
			{
				var accessor = memory.CreateViewAccessor();
				byte[] buffer = Encoding.UTF8.GetBytes(dataBuffer);
				if (buffer.Length > accessor.Capacity)
					return false;
				accessor.WriteArray(0, buffer, 0, buffer.Length);
				accessor.Write(buffer.Length, (byte)'\0');
				return true;
			}
			catch
			{
				return false;
			}
		}

		private static string ReadMemory(MemoryMappedFile memory)
		{
			var accessor = memory.CreateViewAccessor();
			var buffer = new byte[accessor.Capacity];
			accessor.ReadArray(0, buffer, 0, buffer.Length);
			var nullIndex = Array.IndexOf(buffer, (byte)'\0');
			var truncatedBuffer = new byte[nullIndex];
			Array.Copy(buffer, 0, truncatedBuffer, 0, truncatedBuffer.Length);
			return Encoding.UTF8.GetString(truncatedBuffer);
		}

		private static bool CheckMemory(string memoryName)
		{
			try
			{
				var memory = MemoryMappedFile.OpenExisting(memoryName);
				return true;
			}
			catch (FileNotFoundException)
			{
				return false;
			}
		}

		private static long GetSharedMemoryNameSuffix()
		{
			sharedMemoryNameSuffix = long.Parse(sharedMemoryName.Substring(sharedMemoryHeaderName.Length));
			return sharedMemoryNameSuffix;
		}

		/// <summary>
		/// Get sharedMemoryName from sharedMemoryHeader. If sharedMemoryHeader doesn't exist, create it.
		/// </summary>
		private static string GetSharedMemoryName()
		{
			try
			{
				sharedMemoryHeader = MemoryMappedFile.OpenExisting(sharedMemoryHeaderName);
				sharedMemoryName = ReadMemory(sharedMemoryHeader);
				sharedMemoryNameSuffix = GetSharedMemoryNameSuffix();
			}
			catch (FileNotFoundException)
			{
				sharedMemoryName = defaultSharedMemoryName;
				sharedMemoryNameSuffix = sharedMemoryNameDefaultSuffix;
				WriteSharedMemoryName(sharedMemoryName);
			}
			return sharedMemoryName;
		}

		/// <summary>
		/// Write sharedMemoryName to sharedMemoryHeader.
		/// </summary>
		private static void WriteSharedMemoryName(string sharedMemoryNameIn)
		{
			sharedMemoryHeader = MemoryMappedFile.CreateOrOpen(sharedMemoryHeaderName, defaultBufferSize);
			WriteMemory(sharedMemoryHeader, sharedMemoryNameIn);
		}

		/// <summary>
		/// Get current used sharedMemory reference. If current sharedMemory is disposed, try the previous one. 
		/// </summary>
		private static MemoryMappedFile GetSharedMemory()
		{
			sharedMemoryName = GetSharedMemoryName();
			var isDone = false;
			for (var i = sharedMemoryNameSuffix; i >= sharedMemoryNameDefaultSuffix; i--)
			{
				sharedMemoryName = sharedMemoryHeaderName + i.ToString();
				if (CheckMemory(sharedMemoryName))
				{
					sharedMemoryName = sharedMemoryHeaderName + i.ToString();
					sharedMemoryNameSuffix = i;
					isDone = true;
					break;
				}
			}
			if (!isDone)
			{
				sharedMemoryName = defaultSharedMemoryName;
				sharedMemoryNameSuffix = sharedMemoryNameDefaultSuffix;
			}
			WriteSharedMemoryName(sharedMemoryName);
			sharedMemory = MemoryMappedFile.CreateOrOpen(sharedMemoryName, defaultBufferSize);
			return sharedMemory;
		}

		/// <summary>
		/// Increase the size of sharedMemory.
		/// For dynamic expansion, a new sharedMemory with a unique name must be created. Transfer data from the old sharedMemory to the new one, and attempt to clear the old sharedMemory.
		/// Note that the old sharedMemory may still be in use by other instances. Therefore, if this instance creates a new sharedMemory, its record will be removed from the old sharedMemory.
		/// </summary>
		private static MemoryMappedFile ExtendSharedMemory(long newBufferSize)
		{
			sharedMemoryName = GetSharedMemoryName();
			sharedMemory = MemoryMappedFile.OpenExisting(sharedMemoryName);
			var memoryData = ReadMemory(sharedMemory);
			var tabsWithId = JsonSerializer.Deserialize<List<string>>(memoryData).Select(x => TabItemWithIDArguments.Deserialize(x)).ToList();
			var otherTabsWithId = RemoveTabsWithID(tabsWithId);
			var otherTabsWithIdStr = otherTabsWithId.Select(x => x.Serialize()).ToList();
			WriteMemory(sharedMemory, JsonSerializer.Serialize(otherTabsWithIdStr));
			sharedMemory.Dispose();
			sharedMemoryNameSuffix = sharedMemoryNameSuffix + sharedMemoryNameSuffixStep;
			sharedMemoryName = sharedMemoryHeaderName + sharedMemoryNameSuffix.ToString();
			sharedMemory = MemoryMappedFile.CreateOrOpen(sharedMemoryName, newBufferSize);
			WriteMemory(sharedMemory, memoryData);
			WriteSharedMemoryName(sharedMemoryName);
			return sharedMemory;
		}

		private static bool CheckMemorySize(MemoryMappedFile memory, long size)
		{
			var accessor = memory.CreateViewAccessor();
			if (accessor.Capacity < size)
			{
				return false;
			}
			return true;
		}

		/// <summary>
		/// Read tabsWithIdArgList from sharedMemory
		/// </summary>
		private static async Task ReadSharedMemory()
		{
			try
			{
				sharedMemory = GetSharedMemory();
				var bufferStr = ReadMemory(sharedMemory);
				if (string.IsNullOrEmpty(bufferStr))
				{
					tabsWithIdArgList = new List<TabItemWithIDArguments>();
					return;
				}
				tabsWithIdArgList = JsonSerializer.Deserialize<List<string>>(bufferStr).Select(x => TabItemWithIDArguments.Deserialize(x)).ToList();
			}
			finally
			{
				await Task.CompletedTask;
			}
		}

		/// <summary>
		/// Write tabsWithIdArgList to sharedMemory
		/// </summary>
		private static async Task WriteSharedMemory()
		{
			try
			{
				var tabsWithIDArgStrList = tabsWithIdArgList.Select(x => x.Serialize()).ToList();
				string bufferStr = JsonSerializer.Serialize(tabsWithIDArgStrList);
				sharedMemory = GetSharedMemory();
				if (!CheckMemorySize(sharedMemory, bufferStr.Length))
				{
					sharedMemory = ExtendSharedMemory(bufferStr.Length);
				}
				WriteMemory(sharedMemory, bufferStr);
			}
			finally
			{
				await Task.CompletedTask;
			}
		}

		private static List<TabItemWithIDArguments> AddTabsWithID()
		{
			var otherTabsWithIdArgList = tabsWithIdArgList.FindAll(x => x.instanceId != instanceId).ToList();
			var thisInstanceTabsStr = MainPageViewModel.AppInstances.DefaultIfEmpty().Select(x => x.NavigationParameter.Serialize()).ToList();
			var thisInstanceTabsWithIdArgList = thisInstanceTabsStr.Select(x => TabItemWithIDArguments.CreateFromTabItemArg(CustomTabViewItemParameter.Deserialize(x))).ToList();
			var newTabsWithIDArgList = otherTabsWithIdArgList.ToList();
			newTabsWithIDArgList.AddRange(thisInstanceTabsWithIdArgList);
			return newTabsWithIDArgList;
		}

		private static List<TabItemWithIDArguments> RemoveTabsWithID(List<TabItemWithIDArguments> tabItemsList)
		{
			var otherTabsWithIDArgList = tabItemsList.FindAll(x => x.instanceId != instanceId).ToList();
			return otherTabsWithIDArgList;
		}

		/// <summary>
		/// Update the tabsWithIdArgList stored in sharedMemory and userSettingsService.GeneralSettingsService.LastAppsTabsWithIDList.
		/// Should be executed once when a tab is changed.
		/// </summary>
		public static async Task UpDate()
		{
			await ReadSharedMemory();
			tabsWithIdArgList = AddTabsWithID();
			await WriteSharedMemory();
			await ReadSharedMemory();
			userSettingsService.GeneralSettingsService.LastAppsTabsWithIDList = tabsWithIdArgList.Select(x => x.Serialize()).ToList();
		}

		/// <summary>
		/// Remove the tabs of the current instance from tabsWithIdArgList.
		/// Should be executed once when closing the current instance.
		/// </summary>
		public static async void RemoveThisInstanceTabs()
		{
			await ReadSharedMemory();
			tabsWithIdArgList = RemoveTabsWithID(tabsWithIdArgList).ToList();
			await WriteSharedMemory();
			userSettingsService.GeneralSettingsService.LastAppsTabsWithIDList = tabsWithIdArgList.Select(x => x.Serialize()).ToList();
		}

		/// <summary>
		/// Compare tabsWithIdArgList and userSettingsService.GeneralSettingsService.LastAppsTabsWithIDList in sharedMemory, and restore tabs that were not closed normally (direct shutdown, etc.).
		/// Should be executed once when starting a new instance.
		/// </summary>
		public static bool RestoreLastAppsTabs(MainPageViewModel mainPageViewModel)
		{
			ReadSharedMemory();
			if (userSettingsService.GeneralSettingsService.LastAppsTabsWithIDList is null)
			{
				return false;
			}
			// Compare LastAppsTabsWithIDList with tabsWithIdArgList (running instances) to identify Tabs records that are not currently running, and restore them.
			var lastAppsTabsWithIdArgList = userSettingsService.GeneralSettingsService.LastAppsTabsWithIDList
				.Select(x => TabItemWithIDArguments.Deserialize(x))
				.ToList();
			var tabsIdList = tabsWithIdArgList
				.Select(x => x.instanceId)
				.Distinct()
				.ToList();
			var tabsWithIdToBeRestored = lastAppsTabsWithIdArgList
				.Where(x => !tabsIdList.Contains(x.instanceId))
				.ToList();
			if (tabsWithIdToBeRestored.Count == 0)
			{
				return false;
			}
			var instanceIdList = tabsWithIdToBeRestored
				.Select(x => x.instanceId)
				.Distinct()
				.ToList();
			// Classify Tabs by instanceId and open Tabs with the same instanceId in the same window
			for (int i = 0; i < instanceIdList.Count; i++)
			{
				string instanceId = instanceIdList[i];
				var tabsWithThisIdToBeRestored = tabsWithIdToBeRestored
					.Where(x => x.instanceId == instanceId)
					.ToList();
				var tabsToBeRestored = tabsWithThisIdToBeRestored
					.Select(x => x.ExportToTabItemArg())
					.ToList();
				var tabsToBeRestoredStr = tabsToBeRestored
					.Select(x => x.Serialize())
					.ToList();
				// Place the Tabs for the first instanceId in this window; create new windows for the others
				if (i == 0)
				{
					foreach (var tabArgs in tabsToBeRestored)
					{
						NavigationHelpers.AddNewTabByParamAsync(tabArgs.InitialPageType, tabArgs.NavigationParameter);
					}
				}
				else
				{
					NavigationHelpers.OpenTabsInNewWindowAsync(tabsToBeRestoredStr);
				}
			}
			return true;
		}
	}
}
