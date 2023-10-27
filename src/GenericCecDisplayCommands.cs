using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Crestron.SimplSharp;

namespace GenericCecDisplay
{
	public class CecCommands
	{
		/* https://groups.io/g/crestron/topic/35798610
		 * https://support.crestron.com/app/answers/detail/a_id/5633/kw/CEC
		 * https://community.crestron.com/s/article/id-5633
		 *	HDMI 1 \x4F\x82\x10\x00 tested
		 *	HDMI 2 \x4F\x82\x20\x00 tested
		 *	HDMI 3 \x4F\x82\x30\x00 tested
		 *	HDMI 4 \x4F\x82\x40\x00 tested
		 *	HDMI 5 \x4F\x82\x50\x00 not tested
		 *	HDMI 6 \x4F\x82\x60\x00 not tested
		 */

		public static byte[] PowerStatus = { 0x40, 0x8F };
		public static byte[] PowerToggle = { 0x40, 0x44, 0x6B };
		public static byte[] PowerOnCec1 = { 0x40, 0x44, 0x6D };
		public static byte[] PowerOffCec1 = { 0x40, 0x44, 0x6C };

		public static byte[] PowerOnCec2 = { 0x40, 0x04 };
		public static byte[] PowerOffCec2 = { 0x40, 0x36 };

		public static byte[] PowerOnCec2ReplyFb = { 0x0F, 0x04 };
		public static byte[] PowerOffCec2ReplyFb = { 0x0F, 0x36 };

		public static byte[] PowerOnFb = { 0x04, 0x90, 0x00 };
		public static byte[] PowerOffFb = { 0x04, 0x90, 0x01 };
		public static byte[] PowerWarmingFb = { 0x40, 0x90, 0x02 };
		public static byte[] PowerCoolingFb = { 0x40, 0x90, 0x03 };



		public static byte[] InputHdmi1 = { 0x4F, 0x82, 0x10, 0x00 };
		public static byte[] InputHdmi2 = { 0x4F, 0x82, 0x20, 0x00 };
		public static byte[] InputHdmi3 = { 0x4F, 0x82, 0x30, 0x00 };
		public static byte[] InputHdmi4 = { 0x4F, 0x82, 0x40, 0x00 };



		public static byte[] VolumeUp = { 0x40, 0x44, 0x41 };
		public static byte[] VolumeDown = { 0x40, 0x44, 0x42 };

		/// <summary>
		/// Volume mute on
		/// </summary>
		/// /// <remarks>
		/// https://community.crestron.com/s/article/id-5633
		/// Display -> MUTE_1
		/// Display -> MUTE_2 = \x40\x44\x65
		/// </remarks>
		public static byte[] MuteOn = { 0x40, 0x44, 0x43 };

		/// <summary>
		/// Volume mute off
		/// </summary>
		/// <remarks>
		/// https://community.crestron.com/s/article/id-5633
		/// Display -> RESTORE_VOLUME_FUNCTION
		/// </remarks>
		public static byte[] MuteOff = { 0x40, 0x44, 0x66 };
	}
}