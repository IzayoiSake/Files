using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Files.App.Data.Parameters
{
	//	CustomTabViewItemParameter is sealed and cannot be inherited
	public class TabItemWithIDArguments
	{
		public string instanceId { get; set; }
		private static readonly KnownTypesConverter typesConverter = new KnownTypesConverter();
		public string customTabItemParameterStr { get; set; }

		public TabItemWithIDArguments()
		{
			instanceId = Process.GetCurrentProcess().Id.ToString();
			var defaultArg = new CustomTabViewItemParameter() { InitialPageType = typeof(PaneHolderPage), NavigationParameter = "Home" };
			customTabItemParameterStr = defaultArg.Serialize();
		}

		public string Serialize()
		{
			return JsonSerializer.Serialize(this, typesConverter.Options);
		}

		public static TabItemWithIDArguments Deserialize(string obj)
		{
			var tabArgs = new TabItemWithIDArguments();
			var tempArgs = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(obj);

			tabArgs.instanceId = tempArgs.ContainsKey("instanceId") ? tempArgs["instanceId"].GetString() : Process.GetCurrentProcess().Id.ToString();
			// Handle customTabItemParameterStr separately
			tabArgs.customTabItemParameterStr = tempArgs["customTabItemParameterStr"].GetString();
			return tabArgs;
		}

		public static TabItemWithIDArguments CreateFromTabItemArg(CustomTabViewItemParameter tabItemArg)
		{
			var tabItemWithIDArg = new TabItemWithIDArguments();
			tabItemWithIDArg.instanceId = Process.GetCurrentProcess().Id.ToString();
			// Serialize CustomTabViewItemParameter and store the JSON string
			tabItemWithIDArg.customTabItemParameterStr = tabItemArg.Serialize();
			return tabItemWithIDArg;
		}

		public CustomTabViewItemParameter ExportToTabItemArg()
		{
			if (!string.IsNullOrWhiteSpace(customTabItemParameterStr))
			{
				// Deserialize and return CustomTabViewItemParameter
				return CustomTabViewItemParameter.Deserialize(customTabItemParameterStr);
			}
			return null;
		}
	}
}
