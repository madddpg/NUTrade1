@echo off
rem Builds the app and installs it on the Android phone connected over adb (USB or
rem wireless debugging). Close NUTrade on the phone first; this replaces it.
cd /d "%~dp0.."
dotnet build NUTrade1\NUTrade1.csproj -f net10.0-android -c Debug -t:Install
