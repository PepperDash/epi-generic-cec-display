using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Bridges;

namespace PepperDash.Plugin.Display.CecDisplayDriver
{
	public class CecDisplayDriverControllerJoinMap : DisplayControllerJoinMap
	{
	    /// <summary>
	    /// Analog join to report LED product monitor temperature feedback
	    /// </summary>
	    [JoinName("LedTemperatureCelsius")] public JoinDataComplete LedTemperatureCelsius =
	        new JoinDataComplete(new JoinData {JoinNumber = 21, JoinSpan = 1},
	            new JoinMetadata
	            {
	                Description = "Display Temp Celsius",
	                JoinCapabilities = eJoinCapabilities.ToSIMPL,
	                JoinType = eJoinType.Analog
	            });
		
		/// <summary>
		/// Analog join to report LED product monitor temperature feedback
		/// </summary>
        /// <summary>
        /// Analog join to report LED product monitor temperature feedback
        /// </summary>
        [JoinName("LedTemperatureFahrenheit")]
        public JoinDataComplete LedTemperatureFahrenheit =
            new JoinDataComplete(new JoinData { JoinNumber = 22, JoinSpan = 1 },
            new JoinMetadata
            {
                Description = "Display Temp Fahrenheit",
                JoinCapabilities = eJoinCapabilities.ToSIMPL,
                JoinType = eJoinType.Analog
            });

        [JoinName("Status")]
        public JoinDataComplete Status =
            new JoinDataComplete(new JoinData { JoinNumber = 50, JoinSpan = 1 },
            new JoinMetadata
            {
                Description = "Display Temp Celsius",
                JoinCapabilities = eJoinCapabilities.ToSIMPL,
                JoinType = eJoinType.Analog
            });


		/// <summary>
		/// Display controller join map
		/// Some specific adds for Samsung Temperature and Brightness control and feedback
		/// </summary>
		public CecDisplayDriverControllerJoinMap(uint joinStart) : base(joinStart, typeof(CecDisplayDriverControllerJoinMap))
		{
        }
	}
}