# TandoraTCP
This a Windows Pandora client that runs as a system service and listens on a TCP port to take in commands and return responses.  This would generally be useful in a headless home automation A/V setup to bring Pandora to your sound system.

## Install Steps
1. Install the TandoraTCP service by running `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\InstallUtil.exe TandoraTCP.exe`
1. Modify `App.config` to suit your needs.  Supply at least pandoraUsername, and pandoraPassword.
1. Remember to open TCP port 1561 on any relevant firewalls
1. Once the service is running you can send the below commands to control it.  These are just sent as raw ASCII via TCP.  You'll get a response back with the current status of TandoraTCP.

From here you can use whatever language you want that can work with TCP sockets to interace with TandoraTCP.
	
## Commands
* `update`  have TandoraTCP return the current song, station, play time and whether or not playback is active and a list of your Pandora stations.
* `playpause`  toggle playing and pausing music playback.
* `next`  play next song.
* `change station:<station name>`  change the current station.

## JSON Response
`{"IsPlaying":false,
"BASSState":"BASS_ACTIVE_PAUSED",
"CurrentStation":"123",
"CurrentSong":"Beautiful",
"CurrentArtist":"Gordon Lightfoot",
"SongElapsed":6.0,"SongDuration":204.0,
"AlbumArtURL":"https://ladeda.com/album.jpg",
"StationList":{
	"AC/DC Radio":"123",
	"Aerosmith Radio":"456",
	"Bruce Hornsby Radio":"789"
}}`


## Credits
This project was made entirely possible by the great work done by the following...
* Pandorian (https://github.com/dj-nitehawk/Pandorian)
* Pandora Music Box (https://code.google.com/archive/p/pandora-musicbox/)
* BASS audio engine (https://www.un4seen.com/)
	
## Author
Todd Nelson  
https://toddnelson.net