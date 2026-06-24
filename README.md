# ShotLog v0.13.0

## 변경 사항

- 오디오 출력 장치 선택 기능 추가
- 출력 장치 여러 개 동시 선택 가능
- 마이크 녹음 기능 추가
- 마이크 여러 개 동시 선택 가능
- 소리 동기 보정(ms) 설정 추가
  - 소리가 영상보다 빠르면 값을 올려 보세요.
  - 기본값은 1500ms입니다.
- 선택된 여러 오디오를 클립 저장 시 하나로 믹싱하도록 수정
- GitHub 기본 소유자: nyexgs

## 빌드

```cmd
dotnet restore
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true
```

완성 파일:

```text
bin\Release\net8.0-windows\win-x64\publish\ShotLog.exe
```

## 주의

기존 설정 때문에 장치 목록이나 동기 보정값이 이상하면 아래 파일을 삭제한 뒤 다시 실행하세요.

```text
%APPDATA%\ShotLog\settings.json
```
