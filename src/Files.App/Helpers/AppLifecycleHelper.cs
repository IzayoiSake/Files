﻿using Files.App.Services.Settings;
using Microsoft.Windows.AppLifecycle;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using Files.App.Helpers;

namespace Files.App.Helpers
{
	public static class AppLifecycleHelper
	{
		private const long defaultBufferSize = 1024;

		private const string sharedMemoryHeaderName = "FilesAppTabsWithID";
		private static MemoryMappedFile? sharedMemoryHeader;

		private static string defaultSharedMemoryName = sharedMemoryHeaderName + defaultBufferSize.ToString();
		private static string sharedMemoryName = defaultSharedMemoryName;
		public static string instanceId = Process.GetCurrentProcess().Id.ToString();
		private static MemoryMappedFile? sharedMemory;

		private static IUserSettingsService userSettingsService = Ioc.Default.GetRequiredService<IUserSettingsService>();
		private static List<TabItemWithIDArguments> tabsWithIdArgList = new List<TabItemWithIDArguments>();
	
		// Add sharedMemoryHeaderName because sharedMemory can't be released instantly and create a new one with the same name.
		// To dynamically expand the size of a sharedMemory, a new sharedMemory with a new name needs to be created.
		// Using sharedMemoryHeader, sharedMemoryName is saved and shared between all the Files instance.
		// Read or create a sharedMemory(sharedMemoryHeaderName) to save and share the sharedMemoryName
		// The sharedMemoryName is used to identify the sharedMemory
		// The sharedMemory is used to save and share the TabItemWithIDArguments
		private static MemoryMappedFile GetSharedMemoryNameHeader()
		{
			try
			{
				sharedMemoryHeader = MemoryMappedFile.OpenExisting(sharedMemoryHeaderName);
				using (var accessor = sharedMemoryHeader.CreateViewAccessor())
				{
					var buffer = new byte[accessor.Capacity];
					accessor.ReadArray(0, buffer, 0, buffer.Length);
					var nullIndex = Array.IndexOf(buffer, (byte)'\0');
					if (nullIndex > 0)
					{
						var truncatedBuffer = new byte[nullIndex];
						Array.Copy(buffer, 0, truncatedBuffer, 0, truncatedBuffer.Length);
						sharedMemoryName = Encoding.UTF8.GetString(truncatedBuffer);
					}
					else
					{
						sharedMemoryName = defaultSharedMemoryName;
					}
				}
			}
			catch (FileNotFoundException)
			{
				sharedMemoryName = defaultSharedMemoryName;
				sharedMemoryHeader = MemoryMappedFile.CreateOrOpen(sharedMemoryHeaderName, defaultBufferSize);
				using (var accessor = sharedMemoryHeader.CreateViewAccessor())
				{
					byte[] buffer = Encoding.UTF8.GetBytes(sharedMemoryName);
					accessor.WriteArray(0, buffer, 0, buffer.Length);
				}
			}
			return sharedMemoryHeader;
		}
		/// <summary>
		/// Check if the sharedMemory exists, if not create it
		/// </summary>
		private static MemoryMappedFile CheckSharedMemory()
		{
			GetSharedMemoryNameHeader();
			try
			{
				sharedMemory = MemoryMappedFile.OpenExisting(sharedMemoryName);
			}
			catch (FileNotFoundException)
			{
				sharedMemory = MemoryMappedFile.CreateOrOpen(sharedMemoryName, defaultBufferSize);
			}
			return sharedMemory;
		}

		/// <summary>
		/// Check if sharedMemory exists, and if its capacity is sufficient. If not, create a new one.
		/// </summary>
		private static MemoryMappedFile CheckSharedMemory(int BufferSize)
		{
			sharedMemory = CheckSharedMemory();
			var BufferSizeIn = BufferSize;
			using (var accessor0 = sharedMemory.CreateViewAccessor())
			{
				var length = accessor0.Capacity;
				if (length > BufferSizeIn)
					return sharedMemory;
				sharedMemory.Dispose();
				var newBufferSize = ((BufferSizeIn / defaultBufferSize) + 1) * defaultBufferSize;
				sharedMemoryName = sharedMemoryHeaderName + newBufferSize.ToString();
				sharedMemory = MemoryMappedFile.CreateOrOpen(sharedMemoryName, newBufferSize);
				sharedMemoryHeader = MemoryMappedFile.CreateOrOpen(sharedMemoryHeaderName, defaultBufferSize);
				using (var accessor1 = sharedMemoryHeader.CreateViewAccessor())
				{
					byte[] buffer = Encoding.UTF8.GetBytes(sharedMemoryName);
					accessor1.WriteArray(0, buffer, 0, buffer.Length);
				}
			}
			return sharedMemory;
		}

		/// <summary>
		/// Read tabsWithIdArgList from sharedMemory
		/// </summary>
		private static async Task ReadSharedMemory()
		{
			try
			{
				sharedMemory = CheckSharedMemory();
				using (var accessor = sharedMemory.CreateViewAccessor())
				{
					var buffer = new byte[accessor.Capacity];
					accessor.ReadArray(0, buffer, 0, buffer.Length);
					var nullIndex = Array.IndexOf(buffer, (byte)'\0');
					if (nullIndex > 0)
					{
						var truncatedBuffer = new byte[nullIndex];
						Array.Copy(buffer, 0, truncatedBuffer, 0, truncatedBuffer.Length);
						string bufferStr = Encoding.UTF8.GetString(truncatedBuffer);
						tabsWithIdArgList = JsonSerializer.Deserialize<List<string>>(bufferStr).Select(x => TabItemWithIDArguments.Deserialize(x)).ToList();
					}
					else
					{
						tabsWithIdArgList = new List<TabItemWithIDArguments>();
					}
				}
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
				byte[] buffer = Encoding.UTF8.GetBytes(bufferStr);
				sharedMemory = CheckSharedMemory(buffer.Length);
				using (var accessor = sharedMemory.CreateViewAccessor())
				{
					byte[] bufferClear = new byte[accessor.Capacity];
					accessor.WriteArray(0, bufferClear, 0, bufferClear.Length);
					accessor.WriteArray(0, buffer, 0, buffer.Length);
				}
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

		private static List<TabItemWithIDArguments> RemoveTabsWithID()
		{
			var otherTabsWithIDArgList = tabsWithIdArgList.FindAll(x => x.instanceId != instanceId).ToList();
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
			tabsWithIdArgList = RemoveTabsWithID().ToList();
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
			for(int i = 0; i < instanceIdList.Count; i++)
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
						mainPageViewModel.AddNewTabByParam(tabArgs.InitialPageType, tabArgs.NavigationParameter);
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
