# PepperDash Plugin - Display - CEC

This is a plugin repo for Display CEC control

## Device Specifc Information

-


## Example Configuration Object

```JSON
{
	"key": "display1",
	"uid": 1,
	"name": "Display 1",
	"type": "genericCecDisplay",
	"group": "plugin",
	"properties": {
		"id": "00",
		"control": {
			"method": "Cec",
			"controlPortDevKey": "dm1-rx1",
			"controlPortNumber": 1,
			"ControlPortName": "hdmiout"
		}
	}
}
```

### Display Plugin Bridge Object

```
{
	"key": "bridge",
	"uid": 4,
	"name": "eiscBridge Displays",
	"group": "api",
	"type": "eiscApiAdvanced",
	"properties": {
		"control": {
			"method": "ipidTcp",
			"ipid": "B0",
			"tcpSshProperties": {
				"address": "127.0.0.2",
				"port": 0
			}
		},
		"devices": [
			{
				"deviceKey": "display1",
				"joinStart": 1
			}
		]
	}
}
```

## Bridge Join Map

- Each display has 50 buttons available
- The I/O number will depend on the joinStart defined in the configuration file
  - Add the defined joinStart to the I/O number if not starting at 1

### Digitals

| Input                          | I/O | Output                     |
| ------------------------------ | --- | -------------------------- |
| Power Off                      | 1   | Power Off Fb               |
| Power On                       | 2   | Power On Fb                |
|                                | 3   | Is Two Display Fb          |
| Volume Up                      | 5   |                            |
| Volume Down                    | 6   |                            |
| Volume Mute Toggle             | 7   | Volume Mute On Fb          |
| Input 1 Select [HDMI 1]        | 11  | Input 1 Fb [HDMI 1]        |
| Input 2 Select [HDMI 2]        | 12  | Input 2 Fb [HDMI 2]        |
| Input 3 Select [HDMI 3]        | 13  | Input 3 Fb [HDMI 3]        |
| Input 4 Select [HDMI 4]        | 14  | Input 4 Fb [HDMI 4]        |
| Input 5 Select [DisplayPort 1] | 15  | Input 5 Fb [DisplayPort 1] |
| Input 6 Select [DisplayPort 2] | 16  | Input 6 Fb [DisplayPort 2] |
| Input 7 Select [DVI 1]         | 17  | Input 7 Fb [DVI 1]         |
| Input 8 Select [FUTURE]        | 18  | Input 8 Fb [FUTURE]        |
| Input 9 Select [FUTURE]        | 19  | Input 9 Fb [FUTURE]        |
| Input 10 Select [FUTURE]       | 20  | Input 10 Fb [FUTURE]       |
|                                | 40  | Button 1 Visibility Fb     |
|                                | 41  | Button 2 Visibility Fb     |
|                                | 42  | Button 3 Visibility Fb     |
|                                | 43  | Button 4 Visibility Fb     |
|                                | 44  | Button 5 Visibility Fb     |
|                                | 45  | Button 6 Visibility Fb     |
|                                | 46  | Button 7 Visibility Fb     |
|                                | 47  | Button 8 Visibility Fb     |
|                                | 48  | Button 9 Visibility Fb     |
|                                | 49  | Button 10 Visibility Fb    |
|                                | 50  | Display Online Fb          |

### Analogs

| Input                      | I/O | Output                 |
| -------------------------- | --- | ---------------------- |
| Volume Level Set           | 5   | Volume Level Fb        |
| Input Number Select [1-10] | 11  | Input Number Fb [1-10] |

### Serials

| Input | I/O | Output                       |
| ----- | --- | ---------------------------- |
|       | 1   | Display Name                 |
|       | 11  | Input 1 Name [HDMI 1]        |
|       | 12  | Input 2 Name [HDMI 2]        |
|       | 13  | Input 3 Name [HDMI 3]        |
|       | 14  | Input 4 Name [HDMI 4]        |
|       | 15  | Input 5 Name [DisplayPort 1] |
|       | 16  | Input 6 Name [DisplayPort 2] |
|       | 17  | Input 7 Name [DVI 1]         |
|       | 18  | Input 8 Name [FUTURE]        |
|       | 19  | Input 9 Name [FUTURE]        |
|       | 20  | Input 10 Name [FUTURE]       |
