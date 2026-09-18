@echo off
chcp 65001 >nul
setlocal

echo ========================================
echo   Vancat.OllamaCloudUsage 构建工具
echo ========================================
echo.

rem 查找 MSBuild（优先 VS2026，其次 VS2022）
set "MSBUILD="
for %%P in (
    "C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe"
    "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe"
    "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
    "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"
    "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
) do (
    if exist %%P if not defined MSBUILD set "MSBUILD=%%~P"
)

if not defined MSBUILD (
    echo [错误] 未找到 MSBuild，请确认已安装 Visual Studio 2022/2026。
    pause
    exit /b 1
)

echo [信息] 使用 MSBuild: %MSBUILD%
echo.

set "PROJ=%~dp0Vancat.OllamaCloudUsage\Vancat.OllamaCloudUsage.csproj"
set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Debug"

echo [信息] 配置: %CONFIG%
echo.

"%MSBUILD%" "%PROJ%" /t:Rebuild /p:Configuration=%CONFIG% /v:m /nologo

if errorlevel 1 (
    echo.
    echo [失败] 构建出错，请检查上方日志。
    pause
    exit /b 1
)

set "VSIX=%~dp0Vancat.OllamaCloudUsage\bin\%CONFIG%\net472\Vancat.OllamaCloudUsage.vsix"

echo.
if exist "%VSIX%" (
    echo [成功] VSIX 已生成:
    echo        %VSIX%
    echo.
    choice /c YN /m "是否立即安装到 Visual Studio"
    if errorlevel 2 goto :skip_install
    echo.
    echo [信息] 正在安装...
    start "" "%VSIX%"
    goto :done
)

:skip_install
echo [提示] 可双击 VSIX 文件安装，或在 Visual Studio 中通过
echo        「扩展 > 管理扩展 > 从 VSIX 安装」进行安装。

:done
echo.
pause
