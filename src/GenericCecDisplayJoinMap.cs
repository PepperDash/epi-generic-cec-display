using PepperDash.Essentials.Core.Bridges;

namespace GenericCecDisplay
{
	public class GenericCecDisplayJoinMap : DisplayControllerJoinMap
	{
		/// <summary>
		/// Display controller join map		
		/// </summary>
		public GenericCecDisplayJoinMap(uint joinStart) : base(joinStart, typeof(GenericCecDisplayJoinMap))
		{
        }
	}
}