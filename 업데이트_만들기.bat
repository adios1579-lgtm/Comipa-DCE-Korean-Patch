@echo off
chcp 65001 >nul
cd /d "%~dp0"
set /p "PATCH_VERSION=내부 버전 (예: 1.1.0 = 표시 1.01): "
set /p "PATCH_NOTES=변경 설명: "
py -3 -B -X utf8 build_release.py --version "%PATCH_VERSION%" --notes "%PATCH_NOTES%"
if errorlevel 1 exit /b 1
echo releases 폴더의 새 파일을 검증한 뒤 GitHub 릴리스 초안에 첨부하세요.
pause
