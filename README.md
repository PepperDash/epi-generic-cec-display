# PepperDash Essentials Plugin - CEC Display Controller

## Device Configuration

```json
{
	"key": "display1",
	"uid": 1,
	"name": "Display",
	"type": "GenericCecDisplay",
	"group": "plugin",
	"properties": {
		"id": "00",
		"control": {
			"method": "cec",
			"controlPortDevKey": "processor",
			"controlPortNumber": 1,
			"controlPortName": "hdmiOut1"
		}
	}
}
```

## Bridge Configuration

```
{
	"key": "eiscBridge-Displays",
	"uid": 4,
	"name": "eiscBridge Displays",
	"group": "api",
	"type": "eiscApi",
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

| Input                   | I/O | Output                 |
| ----------------------- | --- | ---------------------- |
| Power Off               | 1   | Power Off Fb           |
| Power On                | 2   | Power On Fb            |
|                         | 3   | Is Two Display Fb      |
| Volume Up               | 5   |                        |
| Volume Down             | 6   |                        |
| Volume Mute Toggle      | 7   | Volume Mute On Fb      |
| Input 1 Select [HDMI 1] | 11  | Input 1 Fb [HDMI 1]    |
| Input 2 Select [HDMI 2] | 12  | Input 2 Fb [HDMI 2]    |
| Input 3 Select [HDMI 3] | 13  | Input 3 Fb [HDMI 3]    |
| Input 4 Select [HDMI 4] | 14  | Input 4 Fb [HDMI 4]    |
|                         | 40  | Button 1 Visibility Fb |
|                         | 41  | Button 2 Visibility Fb |
|                         | 42  | Button 3 Visibility Fb |
|                         | 43  | Button 4 Visibility Fb |
|                         | 50  | Display Online Fb      |

### Analogs

| Input            | I/O | Output          |
| ---------------- | --- | --------------- |
| Volume Level Set | 5   | Volume Level Fb |

### Serials

| Input | I/O | Output                |
| ----- | --- | --------------------- |
|       | 1   | Display Name          |
|       | 11  | Input 1 Name [HDMI 1] |
|       | 12  | Input 2 Name [HDMI 2] |
|       | 13  | Input 3 Name [HDMI 3] |
|       | 14  | Input 4 Name [HDMI 4] |
