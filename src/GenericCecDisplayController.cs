﻿using System;
using System.Collections.Generic;
using System.Linq;
using Crestron.SimplSharp;
using Crestron.SimplSharpPro.DeviceSupport;
using Newtonsoft.Json;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Bridges;
using PepperDash.Essentials.Core.Routing;
using PepperDash.Essentials.Devices.Displays;
using Feedback = PepperDash.Essentials.Core.Feedback;

namespace GenericCecDisplay
{
	public class GenericCecDisplayController : TwoWayDisplayBase, IBridgeAdvanced, ICommunicationMonitor,
		IInputHdmi1, IInputHdmi2, IInputHdmi3, IInputHdmi4, IBasicVolumeControls
	{
		public const int InputPowerOn = 101;
		public const int InputPowerOff = 102;
		public static List<string> InputKeys = new List<string>();

		private readonly long _pollIntervalMs;
		private readonly uint _warmingTimeMs;
		private readonly uint _coolingTimeMs;


		public List<BoolFeedback> InputFeedback;

		private RoutingInputPort _currentInputPort;

		private byte[] _incomingBuffer = { };

		public IntFeedback InputNumberFeedback;

		public int CurrentInputNumber
		{
			get { return _currentInputNumber; }
			private set
			{
				_currentInputNumber = value;
				InputNumberFeedback.FireUpdate();
				UpdateBooleanFeedback();
			}
		}

		private int _currentInputNumber;

		private bool _powerIsOn;
		private bool _isCoolingDown;
		private bool _isWarmingUp;
		private bool _isMuted;
		private bool _lastCommandSentWasVolume;
		private int _lastVolumeSent;


		/// <summary>
		/// Constructor for IBaseCommunication
		/// </summary>
		/// <param name="name"></param>
		/// <param name="config"></param>
		/// <param name="key"></param>
		/// <param name="comms"></param>
		//public SamsungMdcDisplayController(string key, string name, DeviceConfig config) : base(key, name)
		public GenericCecDisplayController(string key, string name, GenericCecDisplayPropertiesConfig config,
			IBasicCommunication comms)
			: base(key, name)
		{
			Communication = comms;
			Communication.BytesReceived += Communication_BytesReceived;

			var config1 = config;

			Id = config1.Id == null ? (byte)0x01 : Convert.ToByte(config1.Id, 16);

			_pollIntervalMs = config1.PollIntervalMs;
			_coolingTimeMs = config1.CoolingTimeMs;
			_warmingTimeMs = config1.WarmingTimeMs;

			ResetDebugLevels();

			Init();
		}

		public IBasicCommunication Communication { get; private set; }
		public byte Id { get; private set; }
		public IntFeedback StatusFeedback { get; set; }

		public int SetInput
		{
			get { return CurrentInputNumber; }
			set
			{
				if (value > 0 && value < InputPorts.Count)
				{
					ExecuteSwitch(InputPorts.ElementAt(value - 1).Selector);
					CurrentInputNumber = value;
				}
			}
		}


		protected override Func<bool> PowerIsOnFeedbackFunc
		{
			get { return () => _powerIsOn; }
		}

		protected override Func<bool> IsCoolingDownFeedbackFunc
		{
			get { return () => _isCoolingDown; }
		}

		protected override Func<bool> IsWarmingUpFeedbackFunc
		{
			get { return () => _isWarmingUp; }
		}

		protected override Func<string> CurrentInputFeedbackFunc
		{
			get { return () => _currentInputPort.Key; }
		}

		/// <summary>
		/// 
		/// </summary>
		public override FeedbackCollection<Feedback> Feedbacks
		{
			get
			{
				var list = base.Feedbacks;
				list.AddRange(new List<Feedback>
				{
					VolumeLevelFeedback,
					MuteFeedback,
					CurrentInputFeedback
				});
				return list;
			}
		}

		#region Command Constants

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

		/// <summary>
		/// QUERY_POWER_OSC	\x40\x8F
		/// </summary>
		public const string PowerStatusCmd = "\x40\x8F";

		/// <summary>
		/// Gets/sets the power state
		/// </summary>
		public const string PowerControlToggle = "\x40\x44\x6D";

		/// <summary>
		/// Power control on 
		/// </summary>
		public const string PowerControlOnCec1 = "\x40\x44\x6D";

		/// <summary>
		/// Alternate power control code
		/// </summary>
		public const string PowerControlOnCec2 = "\x40\x04";

		/// <summary>
		/// Power control off
		/// </summary>
		public const string PowerControlOffCec1 = "\x40\x44\x6C";

		/// <summary>
		/// Alternate power control off ocde
		/// </summary>
		public const string PowerControlOffCec2 = "\x40\x36";

		/// <summary>
		/// Input HDMI1
		/// </summary>
		public const string InputControlHdmi1 = "\x4F\x82\x10\x00";

		/// <summary>
		/// Input HDMI2
		/// </summary>
		public const string InputControlHdmi2 = "\x4F\x82\x20\x00";

		/// <summary>
		/// Input HDMI3
		/// </summary>
		public const string InputControlHdmi3 = "\x4F\x82\x30\x00";

		/// <summary>
		/// Input HDMI4
		/// </summary>
		public const string InputControlHdmi4 = "\x4F\x82\x40\x00";

		/// <summary>
		/// Volume up
		/// </summary>
		public const string VolumeAdjustUp = "\x40\x44\x41";

		/// <summary>
		/// Volume down
		/// </summary>
		public const string VolumeAdjustDown = "\x40\x44\x42";

		/// <summary>
		/// Volume mute on
		/// </summary>
		/// /// <remarks>
		/// https://community.crestron.com/s/article/id-5633
		/// Display -> MUTE_1
		/// Display -> MUTE_2 = \x40\x44\x65
		/// </remarks>
		public const string VolumeMuteControlOn = "\x40\x44\x43";

		/// <summary>
		/// Volume mute off
		/// </summary>
		/// <remarks>
		/// https://community.crestron.com/s/article/id-5633
		/// Display -> RESTORE_VOLUME_FUNCTION
		/// </remarks>
		public const string VolumeMuteControlOff = "\x40\x44\x66";


		public byte[] PowerOnFb = { 0x40, 0x90, 0x00 };
		public byte[] PowerOffFb = { 0x40, 0x90, 0x01 };
		public byte[] PowerWarmingFb = { 0x40, 0x90, 0x02 };
		public byte[] PowerCoolingFb = { 0x40, 0x90, 0x03 };

		#endregion


		#region IBasicVolumeWithFeedback Members

		/// <summary>
		/// Volume level feedback property
		/// </summary>
		public IntFeedback VolumeLevelFeedback { get; private set; }

		/// <summary>
		/// volume mte feedback property
		/// </summary>
		public BoolFeedback MuteFeedback { get; private set; }

		/// <summary>
		/// </summary>
		public void MuteOff()
		{
			SendText(VolumeMuteControlOff);
		}

		/// <summary>
		/// </summary>
		public void MuteOn()
		{
			SendText(VolumeMuteControlOn);
		}

		/// <summary>
		/// Mute toggle
		/// </summary>
		public void MuteToggle()
		{
			if (_isMuted)
			{
				MuteOff();
			}
			else
			{
				MuteOn();
			}
		}

		/// <summary>
		/// Volume down (decrement)
		/// </summary>
		/// <param name="pressRelease"></param>
		public void VolumeDown(bool pressRelease)
		{
			if (pressRelease)
			{
				// _volumeIncrementer.Stop(); 
			}
			else
			{
				//_volumeIncrementer.StartDown();
				SendText(VolumeAdjustDown);
			}
		}

		/// <summary>
		/// Volume up (increment)
		/// </summary>
		/// <param name="pressRelease"></param>
		public void VolumeUp(bool pressRelease)
		{
			if (pressRelease)
			{
				// _volumeDecrementer.Stop()
			}
			else
			{
				//_volumeIncrementer.StatUp();
				SendText(VolumeAdjustUp);
			}
		}

		#endregion

		#region IBridgeAdvanced Members

		/// <summary>
		/// LinkToApi (bridge method)
		/// </summary>
		/// <param name="trilist"></param>
		/// <param name="joinStart"></param>
		/// <param name="joinMapKey"></param>
		/// <param name="bridge"></param>
		public void LinkToApi(BasicTriList trilist, uint joinStart, string joinMapKey, EiscApiAdvanced bridge)
		{
			var joinMap = new GenericCecDisplayJoinMap(joinStart);

			var joinMapSerialized = JoinMapHelper.GetSerializedJoinMapForDevice(joinMapKey);

			if (!string.IsNullOrEmpty(joinMapSerialized))
			{
				joinMap = JsonConvert.DeserializeObject<GenericCecDisplayJoinMap>(joinMapSerialized);
			}

			Debug.Console(DebugNotice, "Linking to Trilist '{0}'", trilist.ID.ToString("X"));
			Debug.Console(DebugTrace, "Linking to Display: {0}", Name);

			trilist.StringInput[joinMap.Name.JoinNumber].StringValue = Name;

			CommunicationMonitor.IsOnlineFeedback.LinkInputSig(trilist.BooleanInput[joinMap.IsOnline.JoinNumber]);


			// input analog feedback
			InputNumberFeedback.LinkInputSig(trilist.UShortInput[joinMap.InputSelect.JoinNumber]);


			// Power Off
			trilist.SetSigTrueAction(joinMap.PowerOff.JoinNumber, () => PowerOff());

			PowerIsOnFeedback.LinkComplementInputSig(trilist.BooleanInput[joinMap.PowerOff.JoinNumber]);

			// PowerOn
			trilist.SetSigTrueAction(joinMap.PowerOn.JoinNumber, PowerOn);
			PowerIsOnFeedback.LinkInputSig(trilist.BooleanInput[joinMap.PowerOn.JoinNumber]);

			// Input digitals
			var count = 0;

			foreach (var input in InputPorts)
			{
				var i = count;
				trilist.SetSigTrueAction((ushort)(joinMap.InputSelectOffset.JoinNumber + count),
					() => SetInput = i + 1);

				trilist.StringInput[(ushort)(joinMap.InputNamesOffset.JoinNumber + count)].StringValue = input.Key;

				InputFeedback[count].LinkInputSig(
					trilist.BooleanInput[joinMap.InputSelectOffset.JoinNumber + (uint)count]);
				count++;
			}


			// Input analog
			trilist.SetUShortSigAction(joinMap.InputSelect.JoinNumber, a =>
			{
				if (a == 0)
				{
					PowerOff();
				}
				else if (a > 0 && a < InputPorts.Count)
				{
					SetInput = a;

				}
				else if (a == 102)
				{
					PowerToggle();
				}
				Debug.Console(DebugVerbose, this, "InputChange {0}", a);
			});

			// Volume
			trilist.SetBoolSigAction(joinMap.VolumeUp.JoinNumber, VolumeUp);
			trilist.SetBoolSigAction(joinMap.VolumeDown.JoinNumber, VolumeDown);
			trilist.SetSigTrueAction(joinMap.VolumeMute.JoinNumber, MuteToggle);
			trilist.SetSigTrueAction(joinMap.VolumeMuteOn.JoinNumber, MuteOn);
			trilist.SetSigTrueAction(joinMap.VolumeMuteOff.JoinNumber, MuteOff);
		}

		#endregion

		#region ICommunicationMonitor Members

		public StatusMonitorBase CommunicationMonitor { get; private set; }

		#endregion

		// Add routing input port 
		private void AddRoutingInputPort(RoutingInputPort port)
		{
			InputPorts.Add(port);
		}

		// Initialize 
		private void Init()
		{
			WarmupTime = _warmingTimeMs > 0 ? _warmingTimeMs : 10000;
			CooldownTime = _coolingTimeMs > 0 ? _coolingTimeMs : 8000;

			InitCommMonitor();

			InitInputPortsAndFeedbacks();

			StatusGet();
		}



		private void InitCommMonitor()
		{
			var pollInterval = _pollIntervalMs > 0 ? _pollIntervalMs : 30000;

			CommunicationMonitor = new GenericCommunicationMonitor(this, Communication, pollInterval, 180000, 300000,
				StatusGet);

			DeviceManager.AddDevice(CommunicationMonitor);

			StatusFeedback = new IntFeedback(() => (int)CommunicationMonitor.Status);

			CommunicationMonitor.StatusChange += (sender, args) =>
			{
				Debug.Console(DebugVerbose, this, "Device status: {0}", CommunicationMonitor.Status);
				StatusFeedback.FireUpdate();
			};
		}

		private void InitInputPortsAndFeedbacks()
		{
			//_InputFeedback = new List<bool>();
			InputFeedback = new List<BoolFeedback>();

			AddRoutingInputPort(
				new RoutingInputPort(RoutingPortNames.HdmiIn1, eRoutingSignalType.Audio | eRoutingSignalType.Video,
					eRoutingPortConnectionType.Hdmi, new Action(InputHdmi1), this));

			AddRoutingInputPort(
				new RoutingInputPort(RoutingPortNames.HdmiIn2, eRoutingSignalType.Audio | eRoutingSignalType.Video,
					eRoutingPortConnectionType.Hdmi, new Action(InputHdmi2), this));

			AddRoutingInputPort(
				new RoutingInputPort(RoutingPortNames.HdmiIn3, eRoutingSignalType.Audio | eRoutingSignalType.Video,
					eRoutingPortConnectionType.Hdmi, new Action(InputHdmi3), this));

			AddRoutingInputPort(
				new RoutingInputPort(RoutingPortNames.HdmiIn4, eRoutingSignalType.Audio | eRoutingSignalType.Video,
					eRoutingPortConnectionType.Hdmi, new Action(InputHdmi4), this));


			for (var i = 0; i < InputPorts.Count; i++)
			{
				var j = i;

				InputFeedback.Add(new BoolFeedback(() => CurrentInputNumber == j + 1));
			}

			InputNumberFeedback = new IntFeedback(() => CurrentInputNumber);
		}


		/// <summary>
		/// Custom activate
		/// </summary>
		/// <returns></returns>
		public override bool CustomActivate()
		{
			Communication.Connect();
			CommunicationMonitor.StatusChange +=
				(o, a) => Debug.Console(DebugVerbose, this, "Communication monitor state: {0}", CommunicationMonitor.Status);
			CommunicationMonitor.Start();
			return true;
		}

		// Communication bytes recieved
		private void Communication_BytesReceived(object sender, GenericCommMethodReceiveBytesArgs e)
		{
			try
			{
				Debug.Console(DebugVerbose, this, "Communication_BytesReceived: '{0}'", ComTextHelper.GetEscapedText(e.Bytes));

				ParseMessage(e.Bytes);

				// Append the incoming bytes with whatever is in the buffer
				var newBytes = new byte[_incomingBuffer.Length + e.Bytes.Length];
				_incomingBuffer.CopyTo(newBytes, 0);
				e.Bytes.CopyTo(newBytes, _incomingBuffer.Length);
				
				if (Debug.Level == DebugVerbose)
				{
					// This check is here to prevent
					// following string format from building unnecessarily on level 0 or 1
					Debug.Console(DebugVerbose, this, "Communication_BytesReceived: new bytes-'{0}'", ComTextHelper.GetEscapedText(newBytes));
				}				
			}
			catch (Exception ex)
			{
				Debug.LogError(Debug.ErrorLogLevel.Warning, String.Format("Exception parsing feedback: {0}", ex.Message));
				Debug.LogError(Debug.ErrorLogLevel.Warning, String.Format("Stack trace: {0}", ex.StackTrace));
			}
		}

		private void ParseMessage(byte[] message)
		{
			// This check is here to prevent following string format from building unnecessarily on level 0 or 1
			Debug.Console(DebugVerbose, this, "ParseMessage: '{0}'", ComTextHelper.GetEscapedText(message));

			// power response
			if (message[0] != 0x40 && message[1] != 0x90) return;

			switch (message[2])
			{
				case 0x00:
					UpdatePowerFb("on");
					break;

				case 0x01:
					{
						UpdatePowerFb("off");
						break;
					}
				case 0x02:
					{
						UpdatePowerFb("warming");
						break;
					}
				case 0x03:
					{
						UpdatePowerFb("cooling");
						break;
					}
				default:
					{
						Debug.Console(DebugNotice, this, "ParseMessge: unknown power response '{0}'", ComTextHelper.GetEscapedText(message));
						break;
					}
			}
		}

		public void SendText(string cmd)
		{
			if (string.IsNullOrEmpty(cmd))
			{
				Debug.Console(DebugNotice, this, "SendText: cmd is null or empty, verify cmd");
				return;
			}

			Debug.Console(DebugVerbose, this, "SendText: '{0}'", ComTextHelper.GetEscapedText(cmd));

			if (!Communication.IsConnected)
			{
				Communication.Connect();
			}

			Communication.SendText(cmd);
		}


		// Power feedback
		private void UpdatePowerFb(string state)
		{
			if (string.IsNullOrEmpty(state)) return;

			Debug.Console(DebugVerbose, this, "UpdatePowerFb: state-'{0}'", state);

			switch (state)
			{
				case "off":
					{
						_powerIsOn = false;
						_isCoolingDown = false;
						_isWarmingUp = false;

						PowerIsOnFeedback.FireUpdate();

						break;
					}
				case "on":
					{
						_powerIsOn = true;
						_isCoolingDown = false;
						_isWarmingUp = false;

						PowerIsOnFeedback.FireUpdate();

						break;
					}
				case "cooling":
					{
						_isCoolingDown = true;
						_isWarmingUp = false;

						IsCoolingDownFeedback.FireUpdate();

						break;
					}
				case "warming":
					{
						_isCoolingDown = false;
						_isWarmingUp = true;

						IsWarmingUpFeedback.FireUpdate();

						break;
					}
				default:
					{
						break;
					}
			}
		}

		// Volume feedback
		private void UpdateVolumeFb(byte volmeByte)
		{
			throw new NotImplementedException("UpdateVolumeFb not implemented");
		}

		// Mute feedback
		private void UpdateMuteFb(byte b)
		{
			var newMute = b == 1;

			if (newMute == _isMuted)
			{
				return;
			}
			_isMuted = newMute;
			MuteFeedback.FireUpdate();
		}

		/// <summary>
		/// Input feedback
		/// </summary>
		private void UpdateInputFb(byte b)
		{
			var newInput = InputPorts.FirstOrDefault(i => i.FeedbackMatchObject.Equals(b));
			if (newInput != null && _powerIsOn)
			{
				_currentInputPort = newInput;
				CurrentInputFeedback.FireUpdate();
				var key = newInput.Key;
				switch (key)
				{
					case "hdmiIn1":
						CurrentInputNumber = 1;
						break;
					case "hdmiIn2":
						CurrentInputNumber = 2;
						break;
					case "hdmiIn3":
						CurrentInputNumber = 3;
						break;
					case "hdmiIn4":
						CurrentInputNumber = 4;
						break;
					case "displayPortIn1":
						CurrentInputNumber = 5;
						break;
					case "displayPortIn2":
						CurrentInputNumber = 6;
						break;
					case "dviIn":
						CurrentInputNumber = 7;
						break;
				}
				InputNumberFeedback.FireUpdate();
			}
		}


		/// <summary>
		/// </summary>
		public void StatusGet()
		{
			SendText("\x40\x8F");
		}

		/// <summary>
		/// Power on (Cmd: 0x11) pdf page 42 
		/// Set: [HEADER=0xAA][Cmd=0x11][ID][DATA_LEN=0x01][DATA-1=0x01][CS=0x00]
		/// </summary>
		public override void PowerOn()
		{
			Debug.Console(DebugVerbose, this, "CallingPowerOn");
			//SendText(PowerControlOnCec1);
			SendText(PowerControlOnCec2);

			if (PowerIsOnFeedback.BoolValue || _isWarmingUp || _isCoolingDown)
			{
				return;
			}
			_isWarmingUp = true;
			IsWarmingUpFeedback.FireUpdate();
			// Fake power-up cycle
			WarmupTimer = new CTimer(o =>
			{
				_isWarmingUp = false;
				_powerIsOn = true;
				IsWarmingUpFeedback.FireUpdate();
				PowerIsOnFeedback.FireUpdate();
			}, WarmupTime);
		}

		/// <summary>
		/// Power off (Cmd: 0x11) pdf page 42 
		/// Set: [HEADER=0xAA][Cmd=0x11][ID][DATA_LEN=0x01][DATA-1=0x00][CS=0x00]
		/// </summary>
		public override void PowerOff()
		{
			Debug.Console(DebugVerbose, this, "CallingPowerOff");
			// If a display has unreliable-power off feedback, just override this and
			// remove this check.
			if (!_isWarmingUp && !_isCoolingDown) // PowerIsOnFeedback.BoolValue &&
			{
				//SendText(PowerControlOffCec1);
				SendText(PowerControlOffCec2);
				_isCoolingDown = true;
				_powerIsOn = false;
				CurrentInputNumber = 0;

				InputNumberFeedback.FireUpdate();
				PowerIsOnFeedback.FireUpdate();
				IsCoolingDownFeedback.FireUpdate();
				// Fake cool-down cycle
				CooldownTimer = new CTimer(o =>
				{
					_isCoolingDown = false;
					IsCoolingDownFeedback.FireUpdate();
				}, CooldownTime);
			}
		}


		private void UpdateBooleanFeedback()
		{
			try
			{
				foreach (var item in InputFeedback)
				{
					item.FireUpdate();
				}
			}
			catch (Exception e)
			{
				Debug.Console(DebugTrace, this, "Exception Here - {0}", e.Message);
			}
		}


		/// <summary>
		/// PowerToggle
		/// </summary>
		public override void PowerToggle()
		{
			if (PowerIsOnFeedback.BoolValue && !IsWarmingUpFeedback.BoolValue)
			{
				PowerOff();
			}
			else if (!PowerIsOnFeedback.BoolValue && !IsCoolingDownFeedback.BoolValue)
			{
				PowerOn();
			}
		}

		/// <summary>
		/// Input hdmi 1
		/// </summary>
		public void InputHdmi1()
		{
			SendText(InputControlHdmi1);
		}

		/// <summary>
		/// Input hdmi 2
		/// </summary>
		public void InputHdmi2()
		{
			SendText(InputControlHdmi2);
		}

		/// <summary>
		/// Input hdmi 3
		/// </summary>
		public void InputHdmi3()
		{
			SendText(InputControlHdmi3);
		}

		/// <summary>

		/// </summary>
		public void InputHdmi4()
		{
			SendText(InputControlHdmi4);
		}

		/// <summary>
		/// Executes a switch, turning on display if necessary.
		/// </summary>
		/// <param name="selector"></param>
		public override void ExecuteSwitch(object selector)
		{
			//if (!(selector is Action))
			//    Debug.Console(DebugNotice, this, "WARNING: ExecuteSwitch cannot handle type {0}", selector.GetType());

			if (_powerIsOn)
			{
				var action = selector as Action;
				if (action != null)
				{
					action();
				}
			}
			else // if power is off, wait until we get on FB to send it. 
			{
				// One-time event handler to wait for power on before executing switch
				EventHandler<FeedbackEventArgs> handler = null; // necessary to allow reference inside lambda to handler
				handler = (o, a) =>
				{
					if (_isWarmingUp)
					{
						return;
					}

					IsWarmingUpFeedback.OutputChange -= handler;
					var action = selector as Action;
					if (action != null)
					{
						action();
					}
				};
				IsWarmingUpFeedback.OutputChange += handler; // attach and wait for on FB
				PowerOn();
			}
		}


		#region DebugLevels

		private uint DebugTrace { get; set; }
		private uint DebugNotice { get; set; }
		private uint DebugVerbose { get; set; }


		public void ResetDebugLevels()
		{
			DebugTrace = 0;
			DebugNotice = 1;
			DebugVerbose = 2;
		}


		public void SetDebugLevels(uint level)
		{
			DebugTrace = level;
			DebugNotice = level;
			DebugVerbose = level;
		}

		#endregion

	}
}