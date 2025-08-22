@echo off

@REM Clear out any existing tailscale serves
call tailscale serve reset

@REM Edit the port to point to your Streamer.bot Websocket Server
call tailscale serve --bg 8080

@REM This points to the node server on port 3000
call tailscale serve --bg --set-path /irl 3000

@REM This command runs the node server to serve audio files in ./sounds and ./tts
call npm start
