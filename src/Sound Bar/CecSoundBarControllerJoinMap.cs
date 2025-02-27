using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Bridges;

namespace PepperDash.Plugin.Display.CecDisplayDriver
{
	public class CecSoundBarControllerJoinMap : DisplayControllerJoinMap
	{



		/// <summary>
		/// Display controller join map
		/// Some specific adds for Samsung Temperature and Brightness control and feedback
		/// </summary>
		public CecSoundBarControllerJoinMap(uint joinStart) : base(joinStart, typeof(CecDisplayDriverControllerJoinMap))
		{
        }
	}
}