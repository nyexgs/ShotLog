# ShotLog v0.6.0

## 변경점

- 전역 단축키 방식을 `RegisterHotKey`에서 저수준 키보드 후킹 방식으로 변경했습니다.
- 롤/게임 화면에서 단축키 입력이 ShotLog까지 잘 전달되도록 개선했습니다.
- 단축키 설정을 표에서 고르는 방식이 아니라 `단축키 녹화` 버튼 방식으로 변경했습니다.
- 일반 키 입력을 단축키로 사용할 수 있습니다.
  - 예: C
  - 예: Ctrl + J
  - 예: Alt + C
  - 예: Shift + F8
- 기존 v0.5.0 기능은 유지했습니다.

## 사용 방법

1. ShotLog 실행
2. `단축키 녹화` 버튼 클릭
3. 원하는 단축키 입력
4. 로그에 `단축키를 적용했습니다`가 뜨면 저장 완료

## 롤에서 단축키가 안 먹을 때

롤이 관리자 권한으로 실행 중이거나 게임/보안 프로그램이 입력을 강하게 잡고 있으면 ShotLog도 관리자 권한으로 실행해야 할 수 있습니다.

1. ShotLog.exe 우클릭
2. `관리자 권한으로 실행`
3. 롤 인게임에서 다시 테스트

## 빌드

```cmd
cd /d C:\ShotLog
dotnet clean
dotnet restore
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true
```

완성 파일:

```text
C:\ShotLog\bin\Release\net8.0-windows\win-x64\publish\ShotLog.exe
```

## 릴리즈 태그

```text
v0.6.0
```
