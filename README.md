# MIRIM PAY Kiosk

WPF 기반 키오스크 애플리케이션입니다. WPF는 Windows 전용이므로 macOS에서는 빌드 검증 일부는 가능하지만 애플리케이션 실행과 GUI QA를 할 수 없습니다.

## Windows 준비 및 실행

```powershell
git clone https://github.com/3x-haust/mirim-pay-kiosk.git
cd mirim-pay-kiosk
dotnet restore .\KioskProject.sln
dotnet build .\KioskProject.sln -c Release --no-restore
dotnet test .\KioskProject.sln -c Release --no-build
dotnet run --project KioskProject/KioskProject.csproj
```

## Windows 게시 및 QA

```powershell
dotnet publish .\KioskProject\KioskProject.csproj -c Release -r win-x64 --self-contained false -o .\artifacts\publish
powershell -ExecutionPolicy Bypass -File .\script\qa\KioskUiQa.ps1 -ExePath .\artifacts\publish\KioskProject.exe -EvidenceDir .\.omo\evidence\manual-qa
```
