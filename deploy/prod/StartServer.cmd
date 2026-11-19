@echo off
rem RiftWard production server (zheng shi fu): same programs as the test server, its own ports and its own
rem server-data next to this file. Keep this file ASCII only: cmd misreads UTF-8 batch files.
cd /d "%~dp0"
title RiftWard production server (24780 / 24781)
set RIFTWARD_CHANNEL=prod
set RIFTWARD_API_PORT=24781
set RIFTWARD_GAME_PORT=24780
rem Absolute path keeps "\prod\" in every process command line, so the test-server release never stops this one.
py -3 "%~dp0server\run_server.py"
pause
