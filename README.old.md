# ShotLog v0.11.0

## 변경 내용

- FFmpeg의 WASAPI 입력을 제거하고, 시스템 소리는 NAudio WASAPI Loopback으로 별도 녹음하도록 변경했습니다.
- `Error splitting the argument list: Option not found` 오류를 유발하던 `-loopback` 옵션을 제거했습니다.
- DDA 캡처가 실행 중 종료되면 자동으로 GDI 캡처로 재시도합니다.
- 클립 저장 시 영상 조각을 먼저 합친 뒤, 시스템 소리 WAV를 병합합니다.
- GitHub 기본 소유자를 `nyexgs`로 변경했습니다.
- 버전을 `0.11.0`으로 올렸습니다.

## 빌드

```cmd
dotnet restore
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true
```

완성 파일:

```text
bin\Release\net8.0-windows\win-x64\publish\ShotLog.exe
```

## 권장 테스트 설정

- 캡처 방식: 자동
- FPS: 60
- 비트레이트: 20Mbps
- 시스템 소리 포함: 켜기
- 발로란트 화면 모드: 창 전체화면 권장

DDA가 불안정하면 자동으로 GDI로 재시도합니다.
