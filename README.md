# ShotLog v0.8.0

변경 사항
- 클립마다 소리 싱크가 달라지는 문제를 줄이기 위해 오디오 저장 기준을 실제 영상 조각 시간 기준으로 변경
- 기존 고정 링버퍼 기준 저장 대신 오디오 청크 타임스탬프 기반 저장 적용
- 영상 길이 기준으로 오디오 병합 시간을 고정하여 클립마다 길이/싱크가 흔들리는 문제 완화
- v0.7.0 기능 유지

빌드
```cmd
cd /d C:\ShotLog
dotnet clean
dotnet restore
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true
```

GitHub Release 태그: v0.8.0
