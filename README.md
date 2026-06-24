# ShotLog v0.5.0

- v13/v14 기준 기능 유지
- 내부 프로그램 버전을 `0.5.0`으로 수정
- 업데이트 확인 로그에 현재 버전 / 최신 릴리즈 태그 표시 추가
- GitHub 기본 소유자: `nyexgs`

## 빌드

```cmd
dotnet clean
dotnet restore
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true
```

완성 파일:

```text
bin\Release\net8.0-windows\win-x64\publish\ShotLog.exe
```

## GitHub Release

이번 버전 태그는 `v0.5.0`으로 올려 주세요.
