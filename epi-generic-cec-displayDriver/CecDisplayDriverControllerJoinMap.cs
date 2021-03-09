using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Bridges;

namespace PepperDash.Plugin.Display.CecDisplayDriver
{
	public class CecDisplayDriverControllerJoinMap : DisplayControllerJoinMap
	{



		/// <summary>
		/// Display controller join map
		/// Some specific adds for Samsung Temperature and Brightness control and feedback
		/// </summary>
		public CecDisplayDriverControllerJoinMap(uint joinStart) : base(joinStart, typeof(CecDisplayDriverControllerJoinMap))
		{
        }
	}
}