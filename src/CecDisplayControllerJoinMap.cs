using PepperDash.Essentials.Core.Bridges;

namespace PepperDash.Plugin.Display.CecDisplayDriver
{
	public class CecDisplayControllerJoinMap : DisplayControllerJoinMap
	{
		/// <summary>
		/// Display controller join map		
		/// </summary>
		public CecDisplayControllerJoinMap(uint joinStart) : base(joinStart, typeof(CecDisplayControllerJoinMap))
		{
        }
	}
}