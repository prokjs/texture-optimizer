# prokjs's VRChat World Optimizer — v1.2.0

[EN](README.md) · [한국어](README.ko.md) · [日本語](README.ja.md)

> **Unity 2022.3.22f1을 사용하는 VRChat 프로젝트를 위해 제작되었습니다. 다른 Unity 버전은 테스트하지 않았으며 호환되지 않을 수 있습니다.**

**텍스처 해상도 상한을 변경하기 전에 내용을 확인하세요.** prokjs가 독립적으로 개발한 Unity Editor 전용 도구로, 임포트된 텍스처를 찾고 GPU 할당량 추정치를 비교한 뒤 복원 가능한 임포터 해상도 상한 축소를 사용자가 검토하여 적용할 수 있습니다.

**Tools → prokjs → VRChat World Optimizer**에서 엽니다. **Textures**, **LOD & Analysis**, **Occlusion & Shadows** 탭을 사용하는 통합 Editor 창입니다. 창을 열거나 스캔하는 것만으로 변경이나 베이크가 자동 실행되지 않습니다.

**User guide** 옆 언어 드롭다운에서 **English**, **日本語**, **한국어**를 선택하세요. 기본값은 영어이며 선택은 EditorPrefs에 저장됩니다. 다시 스캔하거나 체크한 항목, 제안 상한, LOD 설정 또는 현재 미리 보기 계획을 지우지 않고 모든 탭, 표, 미리 보기 및 확인 창이 즉시 갱신됩니다. 가이드 버튼도 선택한 언어의 문서를 엽니다. 오브젝트 이름, 경로 및 원본 증거는 그대로 유지하며 알 수 없는 외부 예외 메시지는 영어로 남습니다. 아래 설명은 영어 컨트롤 이름을 기준으로 합니다.

- 로드된 씬, 선택한 오브젝트, Assets 아래 폴더 또는 Assets 아래 프로젝트 전체의 텍스처 에셋을 스캔합니다.
- 원본 크기, 현재 로드된 크기와 포맷, Default / Standalone / Android 상한 및 안전 제한을 한 표에서 확인합니다.
- 텍스처를 체크하고 더 작은 상한을 제안한 뒤, 적용 전에 에셋 이름이 모두 표시된 미리보기를 검토합니다.
- 프로젝트별 백업 기록을 보관하고, 마지막 배치에서 상태가 안전하게 일치하는 항목을 복원합니다.

## 요구 사항

- **Unity 2022.3.22f1**. 별도의 깨끗한 프로젝트에서 로컬 UPM 설치로 테스트했습니다.
- Unity UI (`com.unity.ugui`). UPM/VCC 설치에는 패키지 의존성으로 선언되어 있습니다. `.unitypackage`로 설치할 경우 Package Manager에서 Unity UI가 설치되어 있는지 확인하세요.
- 편집 가능한 텍스처 에셋과 해당 `.meta` 파일. 버전 관리 시스템이 파일을 잠그는 경우 먼저 체크아웃하세요.

VRChat 프로젝트를 대상으로 하지만 VRChat SDK에 의존하지 않습니다. 설치하거나 사용하는 데 VRChat SDK, Udon 또는 별도의 아바타/월드 도구가 필요하지 않습니다.

### 버전 및 업그레이드

**1.2.0**은 기존 World Optimizer에 3개 언어 UI를 추가합니다. 새 패키지가 아니며 저장소 `prokjs/texture-optimizer`, 패키지 `com.prokjs.texture-optimizer`, 네임스페이스 `Prokjs.TextureOptimizer`, 기존 텍스처 백업 경로와 최적화 안전 규칙을 유지합니다. 과거 1.0.0 텍스처 검증은 이후 LOD/조명/언어 기능의 검증을 의미하지 않습니다.

## 설치 — 한 가지 방법만 선택

`.unitypackage` 임포트와 UPM 또는 VCC 설치를 함께 사용하지 마세요. 기존에 독립형 최적화 스크립트를 프로젝트에 복사했다면 이 패키지를 설치하기 전에 제거하세요. 중복 타입이나 메뉴 항목으로 컴파일이 실패할 수 있습니다. 이전에 필요한 백업은 보존하세요.

### 방법 1: GitHub 릴리스 `.unitypackage`

1. [v1.2.0 릴리스](https://github.com/prokjs/texture-optimizer/releases/tag/v1.2.0)를 엽니다.
2. **Assets**에서 **`prokjs-texture-optimizer-1.2.0.unitypackage`**를 다운로드합니다. “Source code” ZIP/TAR는 Unity 임포트 패키지가 아닙니다.
3. Unity에서 **Assets → Import Package → Custom Package…**를 선택하고 다운로드한 `.unitypackage`를 열어 내용을 임포트합니다.
4. 컴파일 후 **Tools → prokjs → VRChat World Optimizer**를 엽니다.

### 방법 2: Unity Package Manager (Git)

Unity에서 **Window → Package Manager → + → Add package from git URL…**을 열고 다음을 입력합니다.

```text
https://github.com/prokjs/texture-optimizer.git#v1.2.0
```

`com.prokjs.texture-optimizer`를 v1.2.0으로 고정하여 설치합니다. Unity Git 설치에는 Git이 필요합니다.

### 방법 3: VRChat Creator Companion (VCC)

1. VCC의 **Settings → Packages → Add Repository**를 엽니다.
2. 다음 목록 URL을 추가합니다.

   ```text
   https://prokjs.github.io/texture-optimizer/index.json
   ```

3. 저장소를 활성화하고 **Manage Project**에서 **prokjs's VRChat World Optimizer** (`com.prokjs.texture-optimizer`) **1.2.0**을 추가합니다.
4. **Tools → prokjs → VRChat World Optimizer**를 엽니다.

VCC는 선택 가능한 설치 방법이며 런타임 의존성이 아닙니다.

## 첫 배치를 안전하게 적용하기

1. 텍스처 `.meta` 파일을 포함하여 **프로젝트를 커밋하거나 백업**합니다.
2. **Scope**, **Edit cap for** 대상 및 비활성 오브젝트 포함 여부를 선택하고 **Scan / Refresh**를 클릭합니다.
3. 표를 확인합니다. 편집 가능한 행을 개별적으로 체크하거나 필터링 후 **Select visible editable**을 사용합니다. 체크는 에셋을 선택할 뿐 임포터를 변경하지 않습니다.
4. 행마다 더 작은 **Proposed cap**을 선택하거나 배치 크기를 선택한 뒤 **Propose for checked**를 클릭합니다. 제안은 적용 전까지 에셋을 변경하지 않습니다.
5. **Preview Apply checked...**를 클릭합니다. 모든 에셋 이름, 상한 변경, 플랫폼 오버라이드 안내 및 백업 위치를 읽습니다.
6. 내용이 맞으면 **Apply these named resolution caps**를 클릭합니다. 결과와 프로젝트 내 텍스처 품질을 확인한 뒤 유지할 변경만 커밋합니다.
7. 마지막 최적화 배치를 되돌리려면 **Restore Last Batch...**를 누르고 미리보기를 확인한 뒤 **Restore safe matching entries**를 사용합니다. 이후 편집 내용과 충돌하는 항목은 덮어쓰지 않습니다.

스캔 범위, 추정치 해석, 백업 관리 및 문제 해결은 [전체 사용 설명서](Documentation~/user-guide.ko.md)를 참고하세요.

## LOD 및 씬 준비

**LOD & Analysis**의 스캔 권장 사항은 정적 분석이며 실제 프로파일러 측정이나 FPS 예측이 아닙니다. 기존 LOD 레벨은 Inspector에서 확인합니다. 자동 LOD는 적합한 고정 불투명 메시를 선택하고 명시적 미리보기를 검토한 후 적용합니다. LOD0을 보존하고 축소 메시를 새 에셋 캐시에 저장합니다. 감소율은 **제거할 정점의 비율**이며 삼각형 비율이나 정확한 결과를 보장하지 않습니다. 토폴로지 보존 때문에 목표에 도달하지 못할 수 있으므로 각 레벨과 전환을 직접 확인하세요.

원본 메시/UV/머티리얼/콜라이더를 다시 쓰지 않습니다. 픽업, Udon 제어 대상, 스킨드 메시, 머티리얼 오버라이드 및 베이크된 라이트맵 대체는 권장 자동화에서 제외합니다. 보수적 적격 판정도 시각적 적합성을 보장하지 않습니다.

**Occlusion & Shadows**는 미리보기 후 적합한 대상의 정적 플래그를 준비합니다. 플래그만으로 런타임 컬링이 실행되지 않으며 별도의 Unity 오클루전 베이크가 필요합니다. 바닥 준비는 이미 적합한 UV2/GI가 있어야 하며 UV2 생성이나 조명 변환을 하지 않습니다. 조명 베이크는 별도 확인이 필요한 **씬 전체** 작업이며 오래 걸릴 수 있습니다. 필요하면 취소하세요. 플래그 복원은 베이크된 조명 데이터를 복원하지 않습니다. 기존 런타임 타프 컨트롤러도 변경하지 않습니다.

씬 변경은 Undo 또는 해당 배치 복원을 사용합니다. 복원 후에도 생성된 LOD `.asset`은 Undo/저장된 참조 보호를 위해 보존합니다. 복원은 에셋 삭제가 아닙니다. 적용/베이크 전에 씬과 조명 데이터를 저장하고 백업하세요.

## 중요한 제한 사항과 안전 안내

> **공유 에셋을 변경합니다.** 상한을 변경하면 스캔한 오브젝트뿐 아니라 해당 텍스처를 사용하는 모든 씬, 머티리얼, 프리팹 및 기타 사용처에 영향을 줍니다.

- **임포트 원본 이미지 픽셀은 유지합니다.** 임포터 상한/선택 플랫폼 오버라이드 변경 후 재임포트합니다. 단, 지원하는 독립형 RGBA32/DXT1/DXT5 **네이티브 Texture2D `.asset`은 Default에서 픽셀 자체를 리샘플링**하며 모든 플랫폼에 공유됩니다. 쓰기 전 원본 바이트를 저장하고 GUID/로컬 ID, 포맷, 밉맵 유무, 샘플러 설정을 유지하되 밉맵을 다시 생성합니다. 사용 전에 [가이드의 적격/복원 조건](Documentation~/user-guide.ko.md)을 확인하세요.
- **FPS 향상을 보장하지 않습니다.** 해상도를 낮추면 디테일을 줄이는 대신 임포트된 텍스처의 할당량을 줄일 수 있지만, 프레임 속도 향상이나 모든 성능 병목 해결을 보장하지는 않습니다.
- **추정치는 실제 메모리 상주량 측정값이 아닙니다.** 현재 할당량 추정은 Editor에 로드된 텍스처 크기와 그래픽스 포맷을 사용합니다. 선택 대상/제안 값도 같은 포맷을 대용값으로 사용합니다. 스트리밍, 플랫폼 포맷, 기기 폴백, 아틀라스 패킹 및 임포터의 크기 반올림에 따라 달라질 수 있습니다. **Android 추정치는 대용값이며 Android 기기 측정값이 아닙니다.**
- **일부 에셋은 의도적으로 잠깁니다.** 스프라이트/아틀라스 입력, 생성된 조명/반사 에셋, 패키지·내장·임시·내장 하위 에셋, 지원하지 않는 비 2D 텍스처 및 안전하게 임포터를 편집할 수 없는 에셋이 해당됩니다. 잠긴 행은 정보 제공용이며 해당 에셋의 관리 절차를 우회하라는 뜻이 아닙니다.
- **씬/선택 스캔은 완전한 의존성 감사가 아닙니다.** 지원되는 컴포넌트/머티리얼 참조를 검사합니다. 특히 아바타 애니메이션 클립에서만 참조하는 텍스처나 머티리얼의 탐지는 **지원하거나 보장하지 않습니다.** 폴더/프로젝트 범위는 텍스처 에셋 목록을 보여줄 뿐 실제 사용 여부를 증명하지 않습니다.
- **텍스처 임포터 변경은 Unity Undo로 복원된다고 보장하지 않습니다.** 텍스처 배치 백업과 소스 관리를 사용하세요. LOD/정적 플래그에는 별도의 Undo/배치 복원이 있습니다.
- **텍스처 백업은 프로젝트별 로컬 데이터** `UserSettings/ProkjsTextureOptimizerBackups/`입니다. 임포트 이미지는 임포터 복원 정보만, 지원하는 네이티브 에셋은 원본 `.asset` 바이트도 저장하므로 모두 보존하세요. 네이티브 복원은 경로/GUID/메타데이터/적용 후 내용 해시가 일치해야 하며 이동/대체/이후 편집은 거부합니다. 텍스처 탭은 마지막 배치만 제공합니다.
- **도구를 제거해도 상한은 복원되지 않습니다.** 제거 전에 원하는 임포터 변경을 복원하거나 되돌리세요. 패키지 파일을 제거해도 로컬 백업 기록은 자동으로 삭제되지 않습니다.

## 출처 및 라이선스

**prokjs**가 [Thry's Avatar Performance Tools](https://github.com/Thryrallo/VRC-Avatar-Performance-Tools)의 **Thry's VRAM Checker**에서 영감을 받아 독립적으로 구현했습니다. 원본 구현을 역공학하거나 복사하지 않았습니다. Thryrallo 또는 원본 프로젝트와 제휴 관계가 없으며, 이들이 유지 관리하거나 보증하는 프로젝트가 아닙니다.

독립적으로 작성한 코드는 [MIT License](LICENSE), copyright © 2026 prokjs로 배포됩니다. 원본 MIT 라이선스 전문을 포함한 [서드 파티 고지](THIRD_PARTY_NOTICES.md)를 참고하세요.

## 지원

[소스 저장소](https://github.com/prokjs/texture-optimizer) · [문제 신고](https://github.com/prokjs/texture-optimizer/issues) · [변경 기록](CHANGELOG.md)

문제 신고 시 패키지 버전, 정확한 Unity 버전, 설치 방법, 선택한 범위/대상, 오류 메시지 및 재현 절차를 포함해 주세요. 비공개 프로젝트 에셋이나 백업 JSON은 내용을 확인하지 않고 첨부하지 마세요. 파일의 경로에 프로젝트를 식별할 수 있는 정보가 포함될 수 있습니다.
