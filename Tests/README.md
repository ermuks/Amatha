# 알림 중지 기능 검증

.NET Framework 4.8 콘솔 테스트로 외부 ERP·실제 알림·사용자 설정 파일을 변경하지 않고 검증합니다. 추가 테스트 패키지는 필요하지 않습니다.

```powershell
dotnet build Tests/Amaranth10API.Tests.csproj -c Release
& .\Tests\bin\Release\net48\Amaranth10API.Tests.exe
```

주입한 UTC 시계를 움직여 2시간 직전·정확한 만료 시각·해제·재체크를 검증합니다. 실제 카드 재생성과 알림 집계를 실행하고, WPF 양방향 바인딩 및 체크박스 Click 이벤트가 보고서 버튼으로 전달되지 않는지도 확인합니다. 저장 검증 파일은 무시되는 테스트 빌드 폴더 안에 생성합니다.
