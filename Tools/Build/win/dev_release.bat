@echo off

echo   ______ _______ ______ ______ _______ _______ 
echo  ^|      ^|   _   ^|   __ \   __ \       ^|    ^|  ^|
echo  ^|   ---^|       ^|      ^<   __ ^<   -   ^|       ^|
echo  ^|______^|___^|___^|___^|__^|______/_______^|__^|____^|
echo                         discord.gg/eXPcNKK4yd
echo.

set BASE=%~dp0

rem Get the base path of the script
pushd %BASE%..\..\..
set ROOT=%CD%
popd

rem Get the build target argument
if "%1" EQU "" (
	set TARGET=Debug
) else (
	set TARGET=%1
)

rem Build the solution
dotnet restore %ROOT%\Carbon.Core --nologo
dotnet   clean %ROOT%\Carbon.Core --configuration %TARGET% --nologo
dotnet   build %ROOT%\Carbon.Core --configuration %TARGET% --no-restore --no-incremental
dotnet   build %ROOT%\Carbon.Core --configuration %TARGET%Unix --no-restore --no-incremental

rem Update Assembly version
%ROOT%\Carbon.Core\Carbon.Patch\bin\%TARGET%\net48\Carbon.Patch.exe --path %ROOT% --versionupdate

rem Build the solution
dotnet   build %ROOT%\Carbon.Core --configuration %TARGET% --no-restore --no-incremental
dotnet   build %ROOT%\Carbon.Core --configuration %TARGET%Unix --no-restore --no-incremental

set CERT=%ROOT%\Tools\Humanlights.SignTool\Certificate\carbon
echo %PFXCERT%> %CERT%.pfx_base64
certutil -f -decode %CERT%.pfx_base64 %CERT%.pfx

%ROOT%\Tools\Humanlights.SignTool\Humanlights.SignTool.exe sign -folder "%ROOT%\Carbon.Core\Carbon\bin" -certificate "%CERT%.pfx" -altcertificate "%CERT%.cer" +password "%CERTPASS%" /da "sha256"

rem Create the patch file(s)
%ROOT%\Carbon.Core\Carbon.Patch\bin\%TARGET%\net48\Carbon.Patch.exe --path %ROOT% --configuration %TARGET%
