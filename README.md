# ShotLog v10

ShotLog v10은 기존 스크린샷 프레임 저장 방식이 아니라 FFmpeg 세그먼트 버퍼 방식으로 동작합니다.

## 핵심 변경점

- 캡처 엔진 변경: DDA/Desktop Duplication 기반 FFmpeg 캡처 우선 사용
- 실패 시 GDI 캡처로 자동 fallback
- 시스템 소리 포함 녹화 시도
- 1초 단위 영상 조각을 임시 저장하고 최근 N초만 유지
- 단축키를 누르면 최근 N초 조각을 합쳐서 MP4 클립 저장
- 상시녹화 시작 후 30초가 안 지났으면 쌓인 만큼만 저장
- GitHub Releases 기반 자동 업데이트 추가
- 기본 FPS 60
- FPS 30~144 선택 가능
- 기본 GitHub 저장소: angae1423 / ShotLog

## 빌드 방법

프로젝트 폴더에서 CMD를 열고 아래 명령어를 입력하세요.

```cmd
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true
```

완성 파일은 아래 위치에 생성됩니다.

```text
bin\Release\net8.0-windows\win-x64\publish\ShotLog.exe
```

친구에게 보낼 때는 우선 `publish` 폴더 안의 `ShotLog.exe` 하나만 복사해서 새 폴더에서 실행 테스트해 보세요. exe 하나만으로 실행과 저장이 되면 그 파일 하나만 보내면 됩니다.

## GitHub 자동 업데이트 사용법

1. GitHub에서 `ShotLog` 저장소를 만듭니다.
2. Releases 탭으로 들어갑니다.
3. 새 릴리즈를 만듭니다.
4. 태그는 현재 프로그램보다 높은 버전으로 만듭니다. 예: `v0.10.1`, `v0.11.0`
5. 릴리즈 파일에는 `ShotLog.exe` 또는 `ShotLog.zip`을 업로드합니다.
6. `ShotLog.zip`을 올리는 경우 압축 파일 안에 반드시 `ShotLog.exe`가 있어야 합니다.
7. 프로그램에서 GitHub 소유자와 저장소 이름을 입력한 뒤 업데이트 확인을 누릅니다.

## 권장 발로란트 설정

- 가장 안정적인 설정: 창 전체화면
- 독점 전체화면은 DDA 방식에서 가능한 환경도 있지만 100% 보장되지는 않습니다.

## 오류 로그

```text
%APPDATA%\ShotLog\error.log
```

## 임시 버퍼 위치

```text
%LOCALAPPDATA%\ShotLog\buffer
```

임시 버퍼는 최근 설정한 저장 가능 시간 + 여유분만 남기고 자동 삭제됩니다.
