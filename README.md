# ShotLog v0.12.0

## 핵심 변경
- 영상 세그먼트를 MKV에서 MPEG-TS(ts) 방식으로 변경했습니다.
- 저장 시 concat demuxer 대신 TS 조각을 직접 이어붙인 뒤 MP4로 변환합니다.
- `Invalid data found when processing input` 오류를 줄이기 위해 완성된 조각만 사용합니다.
- GitHub 기본 소유자: nyexgs

## 빌드
```cmd
dotnet restore
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true
```

완성 파일:
`bin\Release\net8.0-windows\win-x64\publish\ShotLog.exe`
