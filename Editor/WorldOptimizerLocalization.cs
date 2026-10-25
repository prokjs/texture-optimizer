using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Prokjs.TextureOptimizer
{
/// <summary>Presentation-only localization. Stored plans, evidence, names and paths stay canonical.</summary>
internal static class WorldOptimizerLocalization
{
    private const string PreferenceKey = "Prokjs.WorldOptimizer.Language";
    internal static readonly string[] LanguageNames = { "English", "日本語", "한국어" };
    private static int language = Mathf.Clamp(EditorPrefs.GetInt(PreferenceKey, 0), 0, 2);
    internal static event Action LanguageChanged;
    private static readonly Dictionary<string, string[]> Text = new Dictionary<string, string[]>(StringComparer.Ordinal);
    private static readonly List<Template> Templates = new List<Template>();
    private static readonly Dictionary<string, string> Messages = new Dictionary<string, string>(StringComparer.Ordinal);
    private static readonly Dictionary<string[], string[]> Arrays = new Dictionary<string[], string[]>();
    private sealed class Template
    {
        internal Regex pattern;
        internal string[] translations;
        internal int[] arguments;
        internal bool[] translateArguments;
        internal int specificity;
    }

    static WorldOptimizerLocalization()
    {
        foreach (string line in Catalog.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] cells = line.TrimEnd('\r').Split('\t');
            if (cells.Length != 3) throw new InvalidOperationException("Invalid optimizer translation catalog row.");
            for (int i = 0; i < cells.Length; i++) cells[i] = cells[i].Replace("\\n", "\n");
            var arguments = new List<int>();
            var translate = new List<bool>();
            string pattern = "\\A";
            int start = 0;
            foreach (Match slot in Regex.Matches(cells[0], @"\{(\d+)(:t)?\}"))
            {
                pattern += Regex.Escape(cells[0].Substring(start, slot.Index - start)) + (cells[0].IndexOf('\n') >= 0 ? "(.*?)" : "([^\\r\\n]*?)");
                arguments.Add(int.Parse(slot.Groups[1].Value, CultureInfo.InvariantCulture));
                translate.Add(slot.Groups[2].Success);
                start = slot.Index + slot.Length;
            }
            if (arguments.Count == 0) Text.Add(cells[0], new[] { cells[1], cells[2] });
            else
            {
                pattern += Regex.Escape(cells[0].Substring(start)) + "\\z";
                Templates.Add(new Template { pattern = new Regex(pattern, RegexOptions.Singleline | RegexOptions.CultureInvariant),
                    translations = new[] { cells[1], cells[2] }, arguments = arguments.ToArray(), translateArguments = translate.ToArray(),
                    specificity = cells[0].Length - arguments.Count * 5 });
            }
        }
        Templates.Sort((a, b) => b.specificity.CompareTo(a.specificity));
    }

    internal static int Language
    {
        get { return language; }
        set
        {
            value = Mathf.Clamp(value, 0, 2);
            if (language == value) return;
            language = value;
            EditorPrefs.SetInt(PreferenceKey, value);
            Messages.Clear();
            Arrays.Clear();
            if (LanguageChanged != null) LanguageChanged();
        }
    }
    internal static string GuideFile { get { return language == 1 ? "user-guide.ja.md" : language == 2 ? "user-guide.ko.md" : "user-guide.md"; } }
    internal static string[] Tr(string[] values)
    {
        if (language == 0 || values == null) return values;
        string[] translated;
        if (Arrays.TryGetValue(values, out translated)) return translated;
        translated = Array.ConvertAll(values, value => Tr(value));
        if (Arrays.Count >= 64) Arrays.Clear();
        Arrays.Add(values, translated);
        return translated;
    }
    internal static string Tr(string english, params object[] arguments)
    {
        return string.Format(CultureInfo.CurrentCulture, Tr(english), arguments);
    }
    internal static string Tr(string english)
    {
        if (language == 0 || string.IsNullOrEmpty(english)) return english;
        string[] translated;
        if (Text.TryGetValue(english, out translated)) return translated[language - 1];
        string cached;
        if (Messages.TryGetValue(english, out cached)) return cached;
        cached = TranslateMessage(english);
        if (Messages.Count >= 4096) Messages.Clear();
        Messages.Add(english, cached);
        return cached;
    }
    private static string TranslateMessage(string english)
    {
        foreach (Template template in Templates)
        {
            Match match = template.pattern.Match(english);
            if (!match.Success) continue;
            int count = 0;
            foreach (int index in template.arguments) count = Math.Max(count, index + 1);
            var arguments = new object[count];
            for (int i = 0; i < template.arguments.Length; i++)
            {
                string argument = match.Groups[i + 1].Value;
                arguments[template.arguments[i]] = template.translateArguments[i] ? Tr(argument) : argument;
            }
            return string.Format(CultureInfo.CurrentCulture, template.translations[language - 1], arguments);
        }
        // Multi-line reports consist of canonical message lines and untouched source evidence.
        if (english.IndexOf('\n') >= 0) return string.Join("\n", Array.ConvertAll(english.Split('\n'), line => Tr(line)));
        // Unknown Unity/OS/third-party exception text is evidence, not guessed translation.
        return english;
    }

    // Explicit full-message templates capture variable names/paths verbatim; never substring-replace evidence.
    private const string Catalog = @"Textures	テクスチャ	텍스처
LOD & Analysis	LOD と分析	LOD 및 분석
Occlusion & Shadows	オクルージョンと影	오클루전 및 그림자
World Optimizer	ワールド最適化	월드 최적화
v1.2.0  |  Editor only	v1.2.0  |  エディター専用	v1.2.0  |  에디터 전용
User guide	ユーザーガイド	사용자 가이드
Report issue	不具合を報告	문제 보고
Getting started	はじめに	시작하기
About, attribution & safety	概要・クレジット・安全性	정보, 출처 및 안전
GitHub / license	GitHub / ライセンス	GitHub / 라이선스
Thry's original VRAM Checker	Thry のオリジナル VRAM Checker	Thry의 원본 VRAM Checker
Open texture backup folder	テクスチャのバックアップを開く	텍스처 백업 폴더 열기
Texture Resolution	テクスチャ解像度	텍스처 해상도
Scope	対象範囲	범위
Edit cap for	上限を変更する対象	상한을 변경할 대상
LoadedScenes	読み込まれたシーン	로드된 씬
SelectedObjects	選択オブジェクト	선택한 오브젝트
ProjectAssets	プロジェクトのアセット	프로젝트 에셋
Folder	フォルダー	폴더
Default	デフォルト	기본값
Standalone	PC (Standalone)	PC (Standalone)
Android	Android	Android
Include inactive objects	非アクティブなオブジェクトも含む	비활성 오브젝트 포함
Assets folder	Assets 内のフォルダー	Assets 폴더
Scan / Refresh	スキャン / 更新	스캔 / 새로 고침
No scan snapshot.	スキャン結果はありません。	스캔 스냅샷이 없습니다.
Select visible editable	表示中の編集可能な行を選択	표시된 편집 가능 항목 선택
Clear selection	選択を解除	선택 해제
Propose for checked	チェックした行に提案	체크한 항목에 제안
Preview Apply checked...	チェックした変更をプレビュー…	체크한 변경 적용 미리 보기...
Restore Last Batch...	前回のバッチを復元…	마지막 일괄 작업 복원...
Apply	適用	적용
Texture / source dimensions	テクスチャ / 元の寸法	텍스처 / 원본 크기
Asset path	アセットのパス	에셋 경로
Loaded now	現在の読み込み状態	현재 로드 상태
Caps: D / S / A	上限: D / S / A	상한: D / S / A
Chosen target now	選択した対象の現在値	선택 대상의 현재 값
Current GPU est.	現在の GPU 推定量	현재 GPU 추정치
Proposed cap	提案する上限	제안 상한
Proposed GPU est.	提案後の GPU 推定量	제안 GPU 추정치
Savings est.	削減量の推定	절감 추정치
Safety / precedence	安全性 / 優先設定	안전 / 우선순위
Ping texture in Project	Project 内のテクスチャを強調表示	Project에서 텍스처 강조 표시
Actual loaded editor dimensions and graphics format, not the chosen target's imported output.	エディターで実際に読み込まれた寸法と形式です。選択した対象のインポート結果ではありません。	에디터에 실제로 로드된 크기와 그래픽 형식이며 선택 대상의 임포트 결과가 아닙니다.
Chosen target's effective cap. Default is masked on platforms with an explicit override.	選択した対象で有効な上限です。明示的なオーバーライドがあるプラットフォームでは Default は適用されません。	선택 대상의 유효 상한입니다. 명시적 오버라이드가 있는 플랫폼에서는 기본값이 적용되지 않습니다.
Editable; explicit confirmation required	編集可能・明示的な確認が必要	편집 가능; 명시적 확인 필요
Ping asset	アセットを強調表示	에셋 강조 표시
Select in Inspector	Inspector で選択	Inspector에서 선택
Copy asset path	アセットのパスをコピー	에셋 경로 복사
Unknown	不明	알 수 없음
Restore texture batch	テクスチャのバッチを復元	텍스처 일괄 작업 복원
Confirm texture caps	テクスチャ上限の確認	텍스처 상한 확인
Restore	復元	복원
Cancel — no changes	キャンセル — 変更しない	취소 — 변경 없음
Restore safe matching entries	安全に一致する項目を復元	안전하게 일치하는 항목 복원
Apply these named resolution caps	表示した解像度上限を適用	표시된 해상도 상한 적용
No saved optimizer batch exists.	保存された最適化バッチはありません。	저장된 최적화 일괄 작업이 없습니다.
Occlusion & Baked Floor Shadows	オクルージョンとベイクした床の影	오클루전 및 베이크된 바닥 그림자
LOD & Static Cost Analysis	LOD と静的コスト分析	LOD 및 정적 비용 분석
Analyze scope	分析対象の範囲	분석 범위
Analyze	分析	분석
Select recommended	推奨行を選択	권장 항목 선택
Inspected row / existing LODs	選択行 / 既存の LOD	검사 항목 / 기존 LOD
Object	オブジェクト	오브젝트
Scene root	シーンのルート	씬 루트
Ping	強調表示	강조 표시
Inspect	詳細を見る	검사
Original cost	元のジオメトリコスト	원본 비용
Eligibility	編集可否	편집 적합성
Safe candidate — recommended by static geometry threshold	安全な候補 — 静的ジオメトリの基準により推奨	안전한 후보 — 정적 지오메트리 기준으로 권장
Safe manual candidate — not automatically recommended	安全な手動候補 — 自動推奨の対象外	안전한 수동 후보 — 자동 권장 대상 아님
Inspect only — excluded from automatic and manual edits	参照のみ — 自動・手動編集の対象外	검사 전용 — 자동 및 수동 편집 제외
No existing LOD coverage reported.	既存の LOD は報告されていません。	기존 LOD 범위가 보고되지 않았습니다.
Existing LODGroup	既存の LODGroup	기존 LODGroup
Original	元のメッシュ	원본
Lower-detail geometry	低詳細ジオメトリ	저세부 지오메트리
Generated lower levels	生成する低詳細レベル数	생성할 저세부 단계 수
When to switch detail	詳細レベルの切り替え	세부 단계 전환 시점
Last LOD → culled below (%)	最後の LOD → これ未満で非表示 (%)	마지막 LOD → 이 값 미만에서 컬링 (%)
Occlusion — let a static opaque object block what is behind it	オクルージョン — 静的な不透明オブジェクトで背後を遮る	오클루전 — 정적 불투명 오브젝트로 뒤쪽 가리기
Selected scene object	選択したシーンオブジェクト	선택한 씬 오브젝트
Analyze Occluder	遮蔽物を分析	차폐물 분석
Mark as Occluder…	遮蔽物として設定…	차폐물로 표시…
Unmark Occluder…	遮蔽物設定を解除…	차폐물 표시 해제…
Mark as Occluder	遮蔽物として設定	차폐물로 표시
Unmark Occluder	遮蔽物設定を解除	차폐물 표시 해제
Mark as Occluder?	遮蔽物として設定しますか？	차폐물로 표시할까요?
Unmark Occluder?	遮蔽物設定を解除しますか？	차폐물 표시를 해제할까요?
Restore Last Occlusion / Floor Setup…	前回のオクルージョン / 床設定を復元…	마지막 오클루전 / 바닥 설정 복원…
Open Unity Occlusion / Bake	Unity の Occlusion / Bake を開く	Unity Occlusion / Bake 열기
Cancel	キャンセル	취소
Restore last occlusion / floor setup?	前回のオクルージョン / 床設定を復元しますか？	마지막 오클루전 / 바닥 설정을 복원할까요?
Restore last setup change	前回の設定変更を復元	마지막 설정 변경 복원
Floor shadows — prepare a receiver and explicitly bake lighting	床の影 — 受け取り側を準備し、明示的にライティングをベイク	바닥 그림자 — 수신 오브젝트 준비 후 명시적으로 조명 베이크
Selected floor object	選択した床オブジェクト	선택한 바닥 오브젝트
Analyze Floor	床を分析	바닥 분석
Prepare selected floor…	選択した床を準備…	선택한 바닥 준비…
Bake scene lighting / floor shadows…	シーン照明 / 床の影をベイク…	씬 조명 / 바닥 그림자 베이크…
Cancel Lighting Bake	ライティングのベイクを中止	조명 베이크 취소
Prepare selected floor for baked shadows?	選択した床をベイクした影用に準備しますか？	선택한 바닥을 베이크된 그림자용으로 준비할까요?
Prepare selected floor	選択した床を準備	선택한 바닥 준비
Start scene-wide lighting bake?	シーン全体のライティングをベイクしますか？	씬 전체 조명 베이크를 시작할까요?
Start scene-wide lighting bake	シーン全体のライティングをベイク	씬 전체 조명 베이크 시작
Auto LOD recommended → Preview	推奨対象の自動 LOD → プレビュー	권장 자동 LOD → 미리 보기
Generate Preview for checked	チェックした行のプレビューを生成	체크한 항목 미리 보기 생성
Restore Last Batch…	前回のバッチを復元…	마지막 일괄 작업 복원…
Check at least one safe editable row first.	まず安全に編集できる行を 1 つ以上チェックしてください。	먼저 안전하게 편집 가능한 항목을 하나 이상 체크하세요.
The backend did not return a preview plan.	バックエンドからプレビュープランが返されませんでした。	백엔드가 미리 보기 계획을 반환하지 않았습니다.
Read-only generated preview	読み取り専用の生成プレビュー	읽기 전용 생성 미리 보기
Apply checked preview…	チェックしたプレビューを適用…	체크한 미리 보기 적용…
Discard preview	プレビューを破棄	미리 보기 폐기
Preview mesh / level	プレビューするメッシュ / レベル	미리 보기 메시 / 단계
Preview zoom	プレビューのズーム	미리 보기 확대
Reset view	表示をリセット	보기 초기화
Original — LOD0	元のメッシュ — LOD0	원본 — LOD0
All generated levels: requested versus actual	生成された全レベル: 要求値と実測数	생성된 모든 단계: 요청 대비 실제
Preview notices / skipped candidates	プレビューの注意 / スキップした候補	미리 보기 알림 / 건너뛴 후보
No mesh available	メッシュがありません	사용 가능한 메시 없음
No preview shader found.	プレビュー用シェーダーが見つかりません。	미리 보기 셰이더를 찾을 수 없습니다.
Apply checked LOD preview?	チェックした LOD プレビューを適用しますか？	체크한 LOD 미리 보기를 적용할까요?
Apply checked preview	チェックしたプレビューを適用	체크한 미리 보기 적용
Restore last LOD optimizer batch?	前回の LOD 最適化バッチを復元しますか？	마지막 LOD 최적화 일괄 작업을 복원할까요?
Restore last batch	前回のバッチを復元	마지막 일괄 작업 복원
Use	使用	사용
Vertices	頂点数	정점 수
Triangles	三角形数	삼각형 수
LOD coverage	LOD の構成	LOD 범위
Safety / recommendation	安全性 / 推奨	안전 / 권장
Hierarchy path	階層パス	계층 경로
No existing LOD levels	既存の LOD レベルなし	기존 LOD 단계 없음
RECOMMENDED	推奨	권장
Manual candidate	手動候補	수동 후보
READ ONLY	読み取り専用	읽기 전용
Geometry	ジオメトリ	지오메트리
{0} textures | {1} checked | {2} changed caps | estimated target saving {3}	{0} テクスチャ | {1} チェック | {2} 上限変更 | 対象の推定削減量 {3}	텍스처 {0}개 | 체크 {1}개 | 상한 변경 {2}개 | 대상 예상 절감량 {3}
{0} vertices / {1} triangles	{0} 頂点 / {1} 三角形	정점 {0}개 / 삼각형 {1}개
{0} rows | {1} recommended | {2} checked | checked original geometry: {3} vertices / {4} triangles	{0} 行 | {1} 推奨 | {2} チェック | 選択した元のジオメトリ: {3} 頂点 / {4} 三角形	항목 {0}개 | 권장 {1}개 | 체크 {2}개 | 체크한 원본 지오메트리: 정점 {3}개 / 삼각형 {4}개
LOD{0} vertices removed (%)	LOD{0} の頂点削減率 (%)	LOD{0} 정점 제거율 (%)
LOD{0} → LOD{1} below (%)	LOD{0} → LOD{1} の切り替え基準 (%)	LOD{0} → LOD{1} 전환 기준 (%)
Generated — LOD{0}	生成結果 — LOD{0}	생성됨 — LOD{0}
{0} / LOD{1} / {2} vertices	{0} / LOD{1} / {2} 頂点	{0} / LOD{1} / 정점 {2}개
Other possible costs — separate static findings ({0})	その他のコスト候補 — 個別の静的検出 ({0})	기타 잠재 비용 — 별도 정적 분석 결과 ({0})
Existing LOD{0} — {1} renderer slots; screen height {2}%	既存 LOD{0} — {1} レンダラースロット、画面占有高さ {2}%	기존 LOD{0} — 렌더러 슬롯 {1}개; 화면 높이 {2}%
LOD{0}: {1} renderer(s), {2} triangles, transition {3}	LOD{0}: {1} レンダラー、{2} 三角形、遷移値 {3}	LOD{0}: 렌더러 {1}개, 삼각형 {2}개, 전환 {3}
Original mesh: {0}	元のメッシュ: {0}	원본 메시: {0}
LOD{0} mesh: {1}	LOD{0} メッシュ: {1}	LOD{0} 메시: {1}
• {0:t}	• {0}	• {0}
LOCKED: {0:t}	ロック: {0}	잠김: {0}
{0:t} — {1:t}	{0} — {1}	{0} — {1}
{0}: {1:t}	{0}: {1}	{0}: {1}
Backup: {0}	バックアップ: {0}	백업: {0}
Receipt: {0}	復元記録: {0}	복원 기록: {0}
Scan failed: {0:t}	スキャン失敗: {0}	스캔 실패: {0}
Preview refused: {0:t}	プレビュー拒否: {0}	미리 보기 거부: {0}
Restore preview failed: {0:t}	復元プレビュー失敗: {0}	복원 미리 보기 실패: {0}
Analysis failed: {0:t}	分析失敗: {0}	분석 실패: {0}
Preview failed: {0:t}	プレビュー失敗: {0}	미리 보기 실패: {0}
Visual preview unavailable: {0:t}	表示プレビューを利用できません: {0}	시각적 미리 보기 사용 불가: {0}
Apply failed: {0:t}	適用失敗: {0}	적용 실패: {0}
Restore refused or failed: {0:t}	復元拒否または失敗: {0}	복원 거부 또는 실패: {0}
Could not open Unity Occlusion: {0:t}	Unity Occlusion を開けません: {0}	Unity Occlusion을 열 수 없음: {0}
Occluder analysis failed: {0:t}	遮蔽物の分析失敗: {0}	차폐물 분석 실패: {0}
Occlusion change refused or failed: {0:t}	オクルージョン変更の拒否または失敗: {0}	오클루전 변경 거부 또는 실패: {0}
Occlusion restore refused or failed: {0:t}	オクルージョン復元の拒否または失敗: {0}	오클루전 복원 거부 또는 실패: {0}
Floor analysis failed: {0:t}	床の分析失敗: {0}	바닥 분석 실패: {0}
Cancel lighting bake failed: {0:t}	ライティングベイクの中止失敗: {0}	조명 베이크 취소 실패: {0}
Floor preparation refused or failed: {0:t}	床の準備の拒否または失敗: {0}	바닥 준비 거부 또는 실패: {0}
Setup restoration refused or failed: {0:t}	設定復元の拒否または失敗: {0}	설정 복원 거부 또는 실패: {0}
Lighting bake refused or failed: {0:t}	ライティングベイクの拒否または失敗: {0}	조명 베이크 거부 또는 실패: {0}
Made for VRChat projects using Unity 2022.3.22f1. Other Unity versions have not been tested and may be incompatible.	Unity 2022.3.22f1 を使用する VRChat プロジェクト向けです。他の Unity バージョンは未検証で、互換性がない場合があります。	Unity 2022.3.22f1을 사용하는 VRChat 프로젝트용입니다. 다른 Unity 버전은 테스트되지 않았으며 호환되지 않을 수 있습니다.
Back up or commit your project first. Textures: Scan, propose lower caps, then review Preview Apply checked. LOD & Analysis: Analyze, inspect safe candidates, generate a read-only mesh preview, then explicitly Apply. Occlusion & Shadows: inspect selected geometry before changing scene flags. Bakes are separate, deliberate scene-wide operations. Opening this tool never scans, saves or starts a bake.	最初にプロジェクトをバックアップまたはコミットしてください。テクスチャ: スキャン後に低い上限を提案し、チェックした変更のプレビューを確認します。LOD と分析: 分析して安全な候補を確認し、読み取り専用プレビューを生成してから明示的に適用します。オクルージョンと影: シーンのフラグを変える前に選択した形状を確認します。ベイクは別の明示的なシーン全体の操作です。このツールを開くだけではスキャン、保存、ベイクを開始しません。	먼저 프로젝트를 백업하거나 커밋하세요. 텍스처: 스캔 후 낮은 상한을 제안하고 체크한 변경 적용 미리 보기를 확인하세요. LOD 및 분석: 분석하고 안전한 후보를 검사한 뒤 읽기 전용 메시 미리 보기를 생성하고 명시적으로 적용하세요. 오클루전 및 그림자: 씬 플래그 변경 전에 선택한 지오메트리를 검사하세요. 베이크는 별도로 명시적으로 실행하는 씬 전체 작업입니다. 도구를 열기만 해서는 스캔, 저장 또는 베이크가 시작되지 않습니다.
Developed by prokjs. Texture inventory independently implemented, inspired by Thry's VRAM Checker in Avatar Performance Tools. Not a reverse-engineered copy; not affiliated with or endorsed by Thry or VRChat. MIT licensed, provided as-is without warranty. Static estimates are not measured profiling or FPS predictions. Keep project backups; local recovery journals are not a substitute.	prokjs が開発。テクスチャ一覧は Avatar Performance Tools の Thry's VRAM Checker に着想を得た独自実装です。リバースエンジニアリングによるコピーではなく、Thry や VRChat との提携・公認はありません。MIT ライセンス、現状のまま無保証で提供します。静的推定は実測プロファイリングや FPS 予測ではありません。ローカル復元記録はバックアップの代わりにならないため、プロジェクトをバックアップしてください。	prokjs 개발. 텍스처 목록은 Avatar Performance Tools의 Thry's VRAM Checker에서 영감을 받아 독립적으로 구현했습니다. 역공학 복제품이 아니며 Thry 또는 VRChat과 제휴하거나 승인을 받지 않았습니다. MIT 라이선스에 따라 보증 없이 있는 그대로 제공됩니다. 정적 추정치는 실측 프로파일링이나 FPS 예측이 아닙니다. 로컬 복구 기록은 백업을 대신하지 않으므로 프로젝트를 백업하세요.
Shared-asset edit: a cap affects every scene, prefab and material using that texture. Imported sources are untouched. Native RGBA32/DXT1/DXT5 .asset pixels are resampled in place under Default only, preserving their format with an exact original-byte backup. Only confirmed Apply/Restore writes assets or import settings. No FPS claim.	共有アセットの編集: 上限はそのテクスチャを使う全シーン、プレハブ、マテリアルに影響します。インポート元は変更しません。ネイティブ RGBA32/DXT1/DXT5 .asset のピクセルは Default のみで同じアセット内に再サンプリングされ、形式を維持して元のバイト列を正確にバックアップします。確認済みの適用・復元のみがアセットやインポート設定を書き換えます。FPS 向上は保証しません。	공유 에셋 편집: 상한은 해당 텍스처를 사용하는 모든 씬, 프리팹 및 머티리얼에 영향을 줍니다. 임포트 원본은 변경하지 않습니다. 네이티브 RGBA32/DXT1/DXT5 .asset 픽셀은 Default에서만 원래 형식을 유지하며 제자리 재샘플링되고 정확한 원본 바이트를 백업합니다. 확인된 적용/복원만 에셋이나 임포트 설정을 변경합니다. FPS 향상을 주장하지 않습니다.
Loaded scenes: renderer/skybox materials, terrain layers, UI and light cookies. Project/folder: imported and native texture assets.	シーン: レンダラー / スカイボックスのマテリアル、地形レイヤー、UI、ライトクッキー。プロジェクト / フォルダー: インポート済み・ネイティブのテクスチャアセット。	로드된 씬: 렌더러/스카이박스 머티리얼, 지형 레이어, UI 및 라이트 쿠키. 프로젝트/폴더: 임포트 및 네이티브 텍스처 에셋.
Scope/target changed or a batch completed. Scan again before previewing another batch. Table remains the last explicit snapshot.	範囲・対象が変わったかバッチが完了しました。次のバッチをプレビューする前に再スキャンしてください。表には直前の明示的なスキャン結果が残っています。	범위/대상이 변경되었거나 일괄 작업이 완료되었습니다. 다른 일괄 작업을 미리 보기 전에 다시 스캔하세요. 표는 마지막으로 명시적으로 생성한 스냅샷을 유지합니다.
All memory numbers are full GPU allocation ESTIMATES, not measured residency. Current uses the loaded editor format/dimensions. Target/proposed use that SAME format as a proxy; Android formats/device fallback, streaming, atlas packing and importer rounding can differ. D/S/A = Default/Standalone/Android; * = explicit override (takes precedence).	全メモリ値は GPU 全体割り当ての推定で、実測の常駐量ではありません。現在値はエディターで読み込まれた形式・寸法です。対象値と提案値も同じ形式による近似です。Android 形式、端末のフォールバック、ストリーミング、アトラス、インポート時の丸めで異なる場合があります。D/S/A = Default/Standalone/Android、* = 優先される明示的なオーバーライド。	모든 메모리 수치는 전체 GPU 할당량 추정치이며 실측 상주 메모리가 아닙니다. 현재 값은 에디터에 로드된 형식/크기를 사용합니다. 대상/제안 값도 동일한 형식을 근사치로 사용하므로 Android 형식/기기 대체 형식, 스트리밍, 아틀라스 패킹 및 임포터 반올림과 다를 수 있습니다. D/S/A = Default/Standalone/Android; * = 우선 적용되는 명시적 오버라이드.
Choose a scope and target, then Scan. Opening this window never scans or changes assets.	範囲と対象を選び、スキャンしてください。このウィンドウを開くだけではスキャンもアセット変更もしません。	범위와 대상을 선택한 뒤 스캔하세요. 창을 열기만 해서는 스캔하거나 에셋을 변경하지 않습니다.
Source {0} x {1}	元の寸法 {0} x {1}	원본 {0} x {1}
{0} x {1} est.\nEffective cap {2}	{0} x {1} 推定\n有効な上限 {2}	{0} x {1} 추정\n유효 상한 {2}
Current-format proxy	現在の形式での近似	현재 형식 근사치
Inventory: {0} deduplicated textures. {1:t}	一覧: 重複を除く {0} テクスチャ。{1}	목록: 중복 제거된 텍스처 {0}개. {1}
PARTIAL/CANCELED inventory: {0} deduplicated textures. {1:t}	部分的 / 中止された一覧: 重複を除く {0} テクスチャ。{1}	부분/취소된 목록: 중복 제거된 텍스처 {0}개. {1}
{0:t}; inactive={1}; chosen cap target={2:t}; loaded editor platform={3}	{0}; 非アクティブ={1}; 上限の対象={2}; エディターの読み込みプラットフォーム={3}	{0}; 비활성={1}; 선택 상한 대상={2}; 에디터 로드 플랫폼={3}
No changed caps selected. No import or backup created.	変更する上限が選択されていません。インポートもバックアップ作成も行っていません。	변경된 상한이 선택되지 않았습니다. 임포트나 백업을 생성하지 않았습니다.
Proposed {0} for {1} checked rows. Skipped {2} rows where it would exceed the source/effective cap. NO importer changed. Review rows and Preview Apply.	チェックした {1} 行に {0} を提案しました。元の寸法または有効な上限を超える {2} 行はスキップしました。インポーターは変更していません。各行と適用プレビューを確認してください。	체크한 {1}개 항목에 {0}을 제안했습니다. 원본/유효 상한을 초과하는 {2}개 항목은 건너뛰었습니다. 임포터는 변경하지 않았습니다. 항목과 적용 미리 보기를 확인하세요.
Preview lost after domain reload. Close and rebuild the preview; no implicit apply occurs.	ドメイン再読み込みでプレビューが失われました。閉じて再作成してください。自動適用は行いません。	도메인 리로드 후 미리 보기가 유실되었습니다. 닫고 다시 생성하세요. 자동 적용은 발생하지 않습니다.
{0:t} — {1:t} — {2} named textures	{0} — {1} — {2} 件の対象テクスチャ	{0} — {1} — 지정된 텍스처 {2}개
Restore only unchanged owned asset/importer states. Source, native pixel, metadata or importer edits since Apply are conflicts and are NEVER overwritten. Native restore recovers the exact original .asset bytes. Partial/canceled entries remain in the durable backup.	所有するアセット・インポーターの状態が変更されていない項目のみ復元します。適用後の元データ、ネイティブピクセル、メタデータ、インポーターの変更は競合となり、決して上書きしません。ネイティブ復元は元の .asset バイト列を正確に回復します。部分完了・中止項目は永続バックアップに残ります。	도구가 소유한 에셋/임포터 상태가 변경되지 않은 항목만 복원합니다. 적용 이후 원본, 네이티브 픽셀, 메타데이터 또는 임포터 편집은 충돌로 처리하며 절대 덮어쓰지 않습니다. 네이티브 복원은 정확한 원본 .asset 바이트를 복구합니다. 부분/취소 항목은 영구 백업에 남습니다.
This changes SHARED texture assets across all scenes/prefabs. Native RGBA32/DXT1/DXT5 .asset pixels are resampled under Default only, preserving their format after an exact original-byte backup. Imported source files are untouched; non-default importer targets may enable overrides, and Default does not override explicit platform settings. This is not a performance guarantee.	全シーン・プレハブに共有されるテクスチャアセットを変更します。ネイティブ RGBA32/DXT1/DXT5 .asset は元のバイト列を正確にバックアップし、Default のみで形式を維持して再サンプリングします。インポート元ファイルは変更しません。Default 以外の対象はオーバーライドを有効にする場合があります。Default は明示的なプラットフォーム設定を上書きしません。性能向上は保証しません。	모든 씬/프리팹에서 공유되는 텍스처 에셋을 변경합니다. 네이티브 RGBA32/DXT1/DXT5 .asset 픽셀은 정확한 원본 바이트를 백업한 후 Default에서만 형식을 유지하며 재샘플링됩니다. 임포트 원본 파일은 변경하지 않습니다. 기본값 이외의 임포터 대상은 오버라이드를 활성화할 수 있으며 기본값은 명시적 플랫폼 설정을 무시하지 않습니다. 성능을 보장하지 않습니다.
Native Default: resample longest edge {0} → {1}; original bytes backed up	ネイティブ Default: 最長辺を {0} → {1} に再サンプリング。元のバイト列をバックアップ	네이티브 Default: 긴 변 {0} → {1} 재샘플링; 원본 바이트 백업
{0:t} | Native: restore exact original .asset bytes	{0} | ネイティブ: 元の .asset バイト列を正確に復元	{0} | 네이티브: 정확한 원본 .asset 바이트 복원
{0:t}: cap {1} | previous stored cap {2}	{0}: 上限 {1} | 以前の保存上限 {2}	{0}: 상한 {1} | 이전 저장 상한 {2}
{0:t} | {1:t} cap {2} → previous cap {3}	{0} | {1} 上限 {2} → 以前の上限 {3}	{0} | {1} 상한 {2} → 이전 상한 {3}
{0:t} | {1:t} cap {2} → previous cap {3} (restore original override state)	{0} | {1} 上限 {2} → 以前の上限 {3} (元のオーバーライド状態を復元)	{0} | {1} 상한 {2} → 이전 상한 {3} (원래 오버라이드 상태 복원)
Batch operation failed: {0:t}	バッチ操作失敗: {0}	일괄 작업 실패: {0}
Retain/review backup: {0}	保持・確認するバックアップ: {0}	보존/검토할 백업: {0}
Choose a scope, then Analyze. Opening this window never scans or changes the world.	範囲を選び、分析してください。このウィンドウを開くだけではスキャンもワールド変更もしません。	범위를 선택한 뒤 분석하세요. 창을 열기만 해서는 스캔하거나 월드를 변경하지 않습니다.
Select a scene object, then Analyze Occluder to inspect its current flags and suitability.	シーンのオブジェクトを選び「遮蔽物を分析」で現在のフラグと適性を確認してください。	씬 오브젝트를 선택한 뒤 차폐물 분석으로 현재 플래그와 적합성을 확인하세요.
Select a static floor mesh, then Analyze Floor before preparing it to receive baked shadows.	静的な床メッシュを選び、ベイクした影を受け取る準備の前に「床を分析」を実行してください。	정적 바닥 메시를 선택한 뒤 베이크된 그림자 수신을 준비하기 전에 바닥 분석을 실행하세요.
Static geometry and configuration estimates only — not GPU/CPU profiling, measured frame time, or an FPS prediction. High triangle counts alone do not prove a bottleneck.	静的ジオメトリ・設定の推定のみです。GPU/CPU プロファイリング、実測フレーム時間、FPS 予測ではありません。三角形数が多いだけではボトルネックの証明になりません。	정적 지오메트리 및 설정 추정치일 뿐 GPU/CPU 프로파일링, 실측 프레임 시간 또는 FPS 예측이 아닙니다. 삼각형 수가 많다는 것만으로 병목을 입증하지는 않습니다.
Only confirmed Apply or Restore changes the project. LOD0 keeps the original renderers; generated lower levels contain MeshFilter + MeshRenderer only. Source meshes, materials, colliders and gameplay components are not replaced. No automatic scene saving.	確認した適用・復元のみがプロジェクトを変更します。LOD0 は元のレンダラーを維持し、生成した低詳細レベルは MeshFilter と MeshRenderer のみを持ちます。元のメッシュ、マテリアル、コライダー、ゲームプレイ部品は置き換えません。シーンは自動保存しません。	확인된 적용 또는 복원만 프로젝트를 변경합니다. LOD0은 원본 렌더러를 유지하며 생성된 저세부 단계에는 MeshFilter + MeshRenderer만 포함됩니다. 원본 메시, 머티리얼, 콜라이더 및 게임플레이 컴포넌트를 교체하지 않습니다. 씬을 자동 저장하지 않습니다.
Edit mode only. Analysis and project-changing controls are disabled while entering or running Play mode.	編集モード専用です。再生モードへの移行中・実行中は分析とプロジェクト変更操作が無効になります。	편집 모드 전용입니다. 플레이 모드 진입 중이거나 실행 중에는 분석 및 프로젝트 변경 컨트롤이 비활성화됩니다.
Selected roots: {0}. Children are included; overlapping selections are deduplicated.	選択したルート: {0}。子も含み、重複した選択は除外します。	선택한 루트: {0}개. 자식도 포함되며 겹치는 선택은 중복 제거됩니다.
Analysis is out of date. Analyze again before generating a preview. Existing results remain available for inspection.	分析結果が古くなっています。プレビュー生成前に再分析してください。既存の結果は引き続き参照できます。	분석 결과가 오래되었습니다. 미리 보기를 생성하기 전에 다시 분석하세요. 기존 결과는 계속 검사할 수 있습니다.
Sort by a column header. Checkboxes choose the batch; clicking a row shows its sources and safety details. Read-only rows cannot be checked.	列見出しで並べ替えます。チェックボックスでバッチを選び、行をクリックすると元のデータと安全性の詳細を表示します。読み取り専用の行はチェックできません。	열 머리글로 정렬하세요. 체크박스는 일괄 작업을 선택하며 항목을 클릭하면 원본과 안전 세부 정보를 표시합니다. 읽기 전용 항목은 체크할 수 없습니다.
Click a result row to inspect its source meshes and existing LOD coverage.	結果の行をクリックして元のメッシュと既存の LOD 構成を確認してください。	결과 항목을 클릭하여 원본 메시와 기존 LOD 범위를 검사하세요.
Populated existing levels are preserved, not regenerated. Builder-owned or conflicting groups remain inspect-only; missing slots are filled only when the backend confirms compatibility.	内容のある既存レベルは再生成せず保持します。ビルダー所有・競合するグループは参照のみです。空きスロットはバックエンドが互換性を確認した場合のみ補います。	이미 채워진 기존 단계는 재생성하지 않고 보존합니다. 빌더 소유 또는 충돌하는 그룹은 검사 전용이며, 빈 슬롯은 백엔드가 호환성을 확인한 경우에만 채웁니다.
These flags are not addressed by mesh LOD generation. Confirm importance with Unity/VRChat profiling on the target hardware.	これらの要因はメッシュ LOD 生成では解決しません。対象ハードウェア上の Unity / VRChat プロファイリングで重要度を確認してください。	이 항목들은 메시 LOD 생성으로 해결되지 않습니다. 대상 하드웨어에서 Unity/VRChat 프로파일링으로 중요성을 확인하세요.
No additional cost flags reported for this snapshot.	このスキャン結果では追加のコスト要因は報告されていません。	이 스냅샷에서 추가 비용 항목이 보고되지 않았습니다.
Analyze to list other possible costs.	分析すると他のコスト候補を表示します。	기타 잠재 비용을 나열하려면 분석하세요.
LOD0: original geometry (0% removed). Sliders below request the percentage of VERTICES REMOVED, not retained and not triangles.	LOD0: 元の形状 (削減 0%)。下のスライダーは保持率や三角形数ではなく、削除する頂点の割合を指定します。	LOD0: 원본 지오메트리(제거율 0%). 아래 슬라이더는 유지되는 비율이나 삼각형이 아니라 제거할 정점의 비율을 요청합니다.
Conservative edge collapse preserves material/UV seams, tests topology and local surface-plane error, and retains the original bounds envelope. It may retain MORE vertices than requested. Inspect actual counts and silhouette before applying.	保守的なエッジ縮約はマテリアル・UV の境界を維持し、トポロジーと局所表面平面の誤差を確認し、元の境界範囲を保持します。要求より多くの頂点が残る場合があります。適用前に実際の数とシルエットを確認してください。	보수적 에지 축소는 머티리얼/UV 경계를 보존하고 토폴로지 및 국소 표면 평면 오차를 검사하며 원본 바운드 범위를 유지합니다. 요청보다 더 많은 정점이 남을 수 있습니다. 적용 전에 실제 개수와 실루엣을 검사하세요.
Screen height means the object's projected height as a fraction of the camera view, not distance in metres. Thresholds descend from near to far. Camera field of view, object scale and QualitySettings.lodBias change the apparent switch distance; VRChat quality settings can change it too. The last threshold culls the group entirely.	画面占有高さはカメラ画面に占める投影高さの割合で、メートル単位の距離ではありません。閾値は近距離から遠距離へ小さくなります。カメラの視野角、オブジェクトのスケール、QualitySettings.lodBias、VRChat の画質設定で見かけの切り替え距離が変わります。最後の閾値でグループ全体が非表示になります。	화면 높이는 미터 단위 거리가 아니라 카메라 화면에 투영된 오브젝트 높이 비율입니다. 임계값은 가까운 곳에서 먼 곳으로 감소합니다. 카메라 시야각, 오브젝트 스케일, QualitySettings.lodBias 및 VRChat 품질 설정에 따라 전환 거리가 달라집니다. 마지막 임계값에서는 그룹 전체가 컬링됩니다.
Current editor LOD bias: {0}. Existing populated LOD levels and their thresholds are preserved; incompatible requested insertions are reported rather than silently changing them.	現在のエディター LOD バイアス: {0}。内容のある既存 LOD と閾値を保持します。互換性のない追加要求は黙って変更せず報告します。	현재 에디터 LOD 바이어스: {0}. 이미 채워진 기존 LOD 단계와 임계값은 보존하며 호환되지 않는 삽입 요청은 임의 변경하지 않고 보고합니다.
This configures Unity BAKED STATIC occlusion, not an arbitrary runtime blocker. Marking an object does not bake anything or immediately hide objects behind it. Unity's visibility cells, bake settings and camera positions determine the result. Moving, transparent, thin, one-sided or holed tarps are not reliable full opaque occluders.	Unity の静的ベイク式オクルージョンを設定します。任意の実行時遮蔽物ではありません。設定するだけではベイクも背後の即時非表示も行いません。Unity の可視性セル、ベイク設定、カメラ位置が結果を決めます。動く物、透明、薄い物、片面、穴のあるシートは完全に不透明な遮蔽物として信頼できません。	Unity의 베이크된 정적 오클루전을 설정하며 임의의 런타임 차폐물이 아닙니다. 표시만 해서는 베이크하거나 뒤의 오브젝트를 즉시 숨기지 않습니다. Unity 가시성 셀, 베이크 설정 및 카메라 위치가 결과를 결정합니다. 움직이거나 투명하거나 얇거나 단면이거나 구멍이 있는 천막은 완전 불투명 차폐물로 신뢰할 수 없습니다.
Open Unity Occlusion / Bake only opens Unity's native window. Review the object and bake occlusion manually when appropriate; these occlusion controls never start a bake or save scenes automatically.	Unity の Occlusion / Bake は標準ウィンドウを開くだけです。対象を確認し、必要に応じて手動でオクルージョンをベイクしてください。この操作群はベイクの開始もシーンの自動保存も行いません。	Unity Occlusion / Bake 열기는 Unity 기본 창만 엽니다. 오브젝트를 검토하고 적절할 때 수동으로 오클루전을 베이크하세요. 이 오클루전 컨트롤은 베이크를 시작하거나 씬을 자동 저장하지 않습니다.
Baked floor shadows need suitable stationary geometry, valid lightmap UVs, and existing Baked or Mixed lights with appropriate shadow settings. Preparing a floor is separate from baking. Lighting baking is SCENE-WIDE, not floor-only; it can take a long time and does not bake shadows from every moving object.	床のベイクした影には適切な静止形状、有効なライトマップ UV、適切な影設定の既存 Baked / Mixed ライトが必要です。床の準備とベイクは別です。ライティングのベイクは床だけでなくシーン全体に及び、長時間かかる場合があります。すべての動く物体の影をベイクするものではありません。	베이크된 바닥 그림자에는 적절한 고정 지오메트리, 유효한 라이트맵 UV 및 올바른 그림자 설정을 가진 기존 Baked/Mixed 조명이 필요합니다. 바닥 준비와 베이크는 별개입니다. 조명 베이크는 바닥 전용이 아니라 씬 전체 작업이며 오래 걸릴 수 있고 모든 움직이는 오브젝트의 그림자를 베이크하지는 않습니다.
Preparation sets Contribute GI and Receive GI = Lightmaps only after safety checks; it does not change light modes. Restore reverts the last setup change, not baked lighting data. Analysis and Auto LOD never trigger a lighting bake.	準備は安全性確認後に Contribute GI と Receive GI = Lightmaps のみを設定し、ライトのモードは変えません。復元は前回の設定変更のみを戻し、ベイク済み照明データは戻しません。分析と自動 LOD はライティングベイクを開始しません。	준비는 안전 검사 후 Contribute GI 및 Receive GI = Lightmaps만 설정하며 조명 모드는 변경하지 않습니다. 복원은 마지막 설정 변경만 되돌리며 베이크된 조명 데이터는 되돌리지 않습니다. 분석과 자동 LOD는 조명 베이크를 시작하지 않습니다.
Auto selects all recommended safe rows in the analyzed scope (including rows hidden by search), generates a read-only preview, then waits for your explicit Apply confirmation below.	分析した範囲内の安全な推奨行を検索で非表示の行も含めて全て選び、読み取り専用プレビューを生成し、下の明示的な適用確認を待ちます。	분석 범위의 모든 안전한 권장 항목(검색으로 숨겨진 항목 포함)을 선택하고 읽기 전용 미리 보기를 생성한 뒤 아래의 명시적 적용 확인을 기다립니다.
Drag either view to orbit both meshes. Scroll to zoom. Neutral studio lighting; source materials where available. This is a mesh comparison, not a rendered-world or performance prediction.	どちらかの表示をドラッグすると両方のメッシュが回転し、スクロールでズームします。中立的なスタジオ照明で、利用可能なら元のマテリアルを使用します。これはメッシュ比較であり、ワールド描画や性能の予測ではありません。	어느 보기든 드래그하면 두 메시가 함께 회전하며 스크롤로 확대합니다. 중립 스튜디오 조명을 사용하며 가능한 경우 원본 머티리얼을 사용합니다. 메시 비교일 뿐 렌더링된 월드나 성능 예측이 아닙니다.
Target limited: the conservative reducer retained more vertices than requested to protect shape, seams or topology. This is not the requested reduction achieved; inspect the actual mesh.	目標未達: 保守的な削減処理は形状、境界、トポロジーの保護のため要求より多くの頂点を残しました。要求した削減を達成していません。実際のメッシュを確認してください。	목표 제한: 보수적 감소기가 형태, 경계 또는 토폴로지를 보호하기 위해 요청보다 많은 정점을 유지했습니다. 요청한 감소율을 달성한 것이 아닙니다. 실제 메시를 검사하세요.
Requested: {0}% vertices removed → approximately {1} remaining.	要求: 頂点を {0}% 削減 → 残り約 {1}。	요청: 정점 {0}% 제거 → 약 {1}개 남음.
Actual: {0} → {1} vertices ({2}% removed); triangles {3} → {4}.	実際: 頂点 {0} → {1} ({2}% 削減)、三角形 {3} → {4}。	실제: 정점 {0} → {1}개({2}% 제거); 삼각형 {3} → {4}개.
{0} / LOD{1}: requested {2}% removed, ~{3} vertices; actual {4} vertices / {5} triangles	{0} / LOD{1}: 要求 {2}% 削減、約 {3} 頂点、実際 {4} 頂点 / {5} 三角形	{0} / LOD{1}: 요청 {2}% 제거, 약 {3}개 정점; 실제 정점 {4}개 / 삼각형 {5}개
{0} / LOD{1}: requested {2}% removed, ~{3} vertices; actual {4} vertices / {5} triangles — TARGET LIMITED	{0} / LOD{1}: 要求 {2}% 削減、約 {3} 頂点、実際 {4} 頂点 / {5} 三角形 — 目標未達	{0} / LOD{1}: 요청 {2}% 제거, 약 {3}개 정점; 실제 정점 {4}개 / 삼각형 {5}개 — 목표 제한
{0} planned rows / {1} generated mesh entries. Existing populated levels are not included in these generated counts.	予定 {0} 行 / 生成メッシュ {1} 件。これらの生成数に内容のある既存レベルは含みません。	계획된 항목 {0}개 / 생성된 메시 항목 {1}개. 이 생성 개수에는 이미 채워진 기존 단계가 포함되지 않습니다.
Play mode changed. Analyze again in Edit mode before applying a batch.	再生モードが変わりました。バッチ適用前に編集モードで再分析してください。	플레이 모드가 변경되었습니다. 일괄 작업 적용 전에 편집 모드에서 다시 분석하세요.
Preview validation failed: {0:t}. Analyze again before applying.	プレビューの検証失敗: {0}。適用前に再分析してください。	미리 보기 검증 실패: {0}. 적용 전에 다시 분석하세요.
Scene, assets, or Undo state changed. Analyze again before generating or applying another preview.	シーン、アセット、Undo の状態が変わりました。次のプレビューの生成・適用前に再分析してください。	씬, 에셋 또는 실행 취소 상태가 변경되었습니다. 다른 미리 보기를 생성하거나 적용하기 전에 다시 분석하세요.
The Unity selection changed. Analyze again to use the new selected roots.	Unity の選択が変わりました。新しいルートを使うには再分析してください。	Unity 선택이 변경되었습니다. 새로 선택한 루트를 사용하려면 다시 분석하세요.
Scope changed. Analyze again; the list is the previous snapshot.	範囲が変わりました。再分析してください。一覧は以前のスキャン結果です。	범위가 변경되었습니다. 다시 분석하세요. 목록은 이전 스냅샷입니다.
LOD settings changed. Generate a new preview before applying.	LOD 設定が変わりました。適用前に新しいプレビューを生成してください。	LOD 설정이 변경되었습니다. 적용 전에 새 미리 보기를 생성하세요.
Occlusion change canceled. Nothing changed.	オクルージョンの変更をキャンセルしました。変更はありません。	오클루전 변경을 취소했습니다. 변경 사항이 없습니다.
Occlusion setup changed. Analyze again before another LOD batch.	オクルージョン設定が変わりました。次の LOD バッチの前に再分析してください。	오클루전 설정이 변경되었습니다. 다른 LOD 일괄 작업 전에 다시 분석하세요.
Occlusion restore canceled. Nothing changed.	オクルージョンの復元をキャンセルしました。変更はありません。	오클루전 복원을 취소했습니다. 변경 사항이 없습니다.
Occlusion setup restored. Analyze again before another LOD batch.	オクルージョン設定を復元しました。次の LOD バッチの前に再分析してください。	오클루전 설정을 복원했습니다. 다른 LOD 일괄 작업 전에 다시 분석하세요.
Floor preparation canceled. Nothing changed.	床の準備をキャンセルしました。変更はありません。	바닥 준비를 취소했습니다. 변경 사항이 없습니다.
Floor lighting setup changed. Analyze again before another LOD batch.	床の照明設定が変わりました。次の LOD バッチの前に再分析してください。	바닥 조명 설정이 변경되었습니다. 다른 LOD 일괄 작업 전에 다시 분석하세요.
Setup restoration canceled. Nothing changed.	設定の復元をキャンセルしました。変更はありません。	설정 복원을 취소했습니다. 변경 사항이 없습니다.
Static occlusion / floor setup restored. Analyze again before another LOD batch.	静的オクルージョン / 床設定を復元しました。次の LOD バッチの前に再分析してください。	정적 오클루전 / 바닥 설정을 복원했습니다. 다른 LOD 일괄 작업 전에 다시 분석하세요.
Lighting bake canceled before starting. Nothing baked.	ライティングベイクを開始前にキャンセルしました。ベイクは行っていません。	시작 전에 조명 베이크를 취소했습니다. 베이크하지 않았습니다.
Lighting bake requested. Analyze again after lighting changes before another LOD batch.	ライティングベイクを要求しました。照明変更後、次の LOD バッチの前に再分析してください。	조명 베이크를 요청했습니다. 조명 변경 후 다른 LOD 일괄 작업 전에 다시 분석하세요.
Analysis complete: {0} geometry rows and {1} other-cost findings. Nothing changed. Review candidates or use Auto LOD recommended.	分析完了: ジオメトリ {0} 行、その他のコスト要因 {1} 件。変更はありません。候補を確認するか推奨対象の自動 LOD を使用してください。	분석 완료: 지오메트리 항목 {0}개 및 기타 비용 결과 {1}개. 변경 사항이 없습니다. 후보를 검토하거나 권장 자동 LOD를 사용하세요.
Analysis canceled. No project changes; the previous snapshot, if any, is now stale.	分析をキャンセルしました。プロジェクトの変更はありません。以前の結果があれば古い状態になっています。	분석을 취소했습니다. 프로젝트 변경은 없으며 이전 스냅샷이 있다면 오래된 상태입니다.
{0} recommended safe rows checked. Selection covers the full snapshot, not just the search results.	安全な推奨行 {0} 件をチェックしました。検索結果だけでなく、スキャン結果全体を対象にしています。	안전한 권장 항목 {0}개를 체크했습니다. 검색 결과뿐 아니라 전체 스냅샷을 대상으로 합니다.
No recommended safe candidates in this snapshot. No meshes or project changes were created.	この結果には安全な推奨候補がありません。メッシュ作成もプロジェクト変更も行っていません。	이 스냅샷에 안전한 권장 후보가 없습니다. 메시를 생성하거나 프로젝트를 변경하지 않았습니다.
Preview produced no applicable meshes. Read the notices below; nothing changed.	適用可能なメッシュは生成されませんでした。下の注意を確認してください。変更はありません。	미리 보기에서 적용 가능한 메시를 생성하지 못했습니다. 아래 알림을 확인하세요. 변경 사항이 없습니다.
Auto LOD preview ready. {0} generated mesh entries. Inspect the original/generated views and actual counts, then explicitly Apply checked preview. Nothing has changed in the project.	自動 LOD プレビューの準備完了。生成メッシュ {0} 件。元と生成後の表示、実際の数を確認し、チェックしたプレビューを明示的に適用してください。プロジェクトは未変更です。	자동 LOD 미리 보기 준비 완료. 생성된 메시 항목 {0}개. 원본/생성 보기를 확인하고 실제 개수를 검사한 뒤 체크한 미리 보기를 명시적으로 적용하세요. 프로젝트는 변경되지 않았습니다.
Preview ready. {0} generated mesh entries. Inspect the original/generated views and actual counts, then explicitly Apply checked preview. Nothing has changed in the project.	プレビューの準備完了。生成メッシュ {0} 件。元と生成後の表示、実際の数を確認し、チェックしたプレビューを明示的に適用してください。プロジェクトは未変更です。	미리 보기 준비 완료. 생성된 메시 항목 {0}개. 원본/생성 보기를 확인하고 실제 개수를 검사한 뒤 체크한 미리 보기를 명시적으로 적용하세요. 프로젝트는 변경되지 않았습니다.
Preview generation canceled. Temporary meshes discarded; nothing applied.	プレビュー生成をキャンセルしました。一時メッシュを破棄し、何も適用していません。	미리 보기 생성을 취소했습니다. 임시 메시를 폐기했으며 적용하지 않았습니다.
Preview discarded. Nothing applied.	プレビューを破棄しました。何も適用していません。	미리 보기를 폐기했습니다. 적용하지 않았습니다.
Apply canceled. The read-only preview is still available; nothing applied.	適用をキャンセルしました。読み取り専用プレビューは残っています。何も適用していません。	적용을 취소했습니다. 읽기 전용 미리 보기는 계속 사용할 수 있으며 적용하지 않았습니다.
Analyze again before another batch. Scene saves remain your choice.	次のバッチの前に再分析してください。シーンを保存するかどうかはご自身で判断してください。	다른 일괄 작업 전에 다시 분석하세요. 씬 저장 여부는 사용자가 결정합니다.
Apply canceled. Analyze again before proceeding; inspect the scene and backend restoration record if any changes had already begun.	適用をキャンセルしました。続行前に再分析してください。変更が始まっていた場合はシーンとバックエンドの復元記録を確認してください。	적용을 취소했습니다. 진행 전에 다시 분석하세요. 변경이 이미 시작되었다면 씬과 백엔드 복원 기록을 검사하세요.
Analyze again before another batch. No scene was automatically saved.	次のバッチの前に再分析してください。シーンは自動保存していません。	다른 일괄 작업 전에 다시 분석하세요. 씬을 자동 저장하지 않았습니다.
Restore canceled. Nothing changed.	復元をキャンセルしました。変更はありません。	복원을 취소했습니다. 변경 사항이 없습니다.
Analyze again before generating another batch.	次のバッチを生成する前に再分析してください。	다른 일괄 작업을 생성하기 전에 다시 분석하세요.
Restore canceled. Analyze again before proceeding.	復元をキャンセルしました。続行前に再分析してください。	복원을 취소했습니다. 진행 전에 다시 분석하세요.
{0:t} for {1}?\n\nThis changes the selected object's static occlusion setup only. It is not a runtime hide-behind-object switch, and it does not start a bake. Only suitable stationary opaque geometry should be used as an occluder.\n\nUndo and Restore Last Occlusion Change are available. No scene will be automatically saved.	{1} に「{0}」を実行しますか？\n\n選択したオブジェクトの静的オクルージョン設定のみを変えます。実行時に背後を隠すスイッチではなく、ベイクも開始しません。遮蔽物には適切な静止・不透明の形状のみを使用してください。\n\nUndo と前回のオクルージョン変更の復元が利用できます。シーンは自動保存しません。	{1}에 '{0}' 작업을 실행할까요?\n\n선택한 오브젝트의 정적 오클루전 설정만 변경합니다. 런타임에서 뒤쪽을 숨기는 스위치가 아니며 베이크도 시작하지 않습니다. 적절한 고정 불투명 지오메트리만 차폐물로 사용하세요.\n\n실행 취소와 마지막 오클루전 변경 복원이 가능합니다. 씬을 자동 저장하지 않습니다.
Restore the last occlusion or floor-preparation change made by this helper? Conflicting edits are checked by the backend. This does not restore unrelated scene work, undo a completed lighting bake, run a bake, or automatically save a scene.	このツールによる前回のオクルージョンまたは床準備の変更を復元しますか？バックエンドが競合する編集を確認します。無関係なシーン作業の復元、完了した照明ベイクの取り消し、ベイクの実行、シーンの自動保存は行いません。	이 도구로 변경한 마지막 오클루전 또는 바닥 준비 설정을 복원할까요? 백엔드가 충돌하는 편집을 검사합니다. 관련 없는 씬 작업을 복원하거나 완료된 조명 베이크를 되돌리거나 베이크를 실행하거나 씬을 자동 저장하지 않습니다.
Restore the last occlusion or floor-preparation change made by this helper? Conflicting edits are checked by the backend. This does not restore unrelated scene work or undo a completed lighting bake, and does not automatically save a scene.	このツールによる前回のオクルージョンまたは床準備の変更を復元しますか？バックエンドが競合する編集を確認します。無関係なシーン作業や完了した照明ベイクを戻すことはなく、シーンも自動保存しません。	이 도구로 변경한 마지막 오클루전 또는 바닥 준비 설정을 복원할까요? 백엔드가 충돌하는 편집을 검사합니다. 관련 없는 씬 작업이나 완료된 조명 베이크를 되돌리지 않으며 씬을 자동 저장하지 않습니다.
Prepare {0} to receive baked lighting?\n\nThe backend validates the static floor and sets Contribute GI plus Receive GI = Lightmaps. This changes setup only; it does not start a bake, alter light modes, or automatically save the scene.\n\nUndo and persistent Restore Last Occlusion / Floor Setup are available.	{0} をベイクした照明の受け取り用に準備しますか？\n\nバックエンドが静的な床を検証し、Contribute GI と Receive GI = Lightmaps を設定します。設定のみを変え、ベイク開始、ライトモード変更、シーンの自動保存は行いません。\n\nUndo と永続的な前回のオクルージョン / 床設定の復元が利用できます。	{0}이(가) 베이크된 조명을 받도록 준비할까요?\n\n백엔드가 정적 바닥을 검증하고 Contribute GI 및 Receive GI = Lightmaps를 설정합니다. 설정만 변경하며 베이크 시작, 조명 모드 변경 또는 씬 자동 저장은 하지 않습니다.\n\n실행 취소 및 재시작 후에도 유지되는 마지막 오클루전 / 바닥 설정 복원이 가능합니다.
Start Unity's lighting bake for the loaded scene configuration using the CURRENT Baked / Mixed lights?\n\nThis is a SCENE-WIDE bake, NOT a floor-only operation. It may take a long time and will update baked lighting data. No light modes are changed. Verify your lighting setup and save any work you want preserved before proceeding.\n\nThis does not bake occlusion data. Cancel Lighting Bake requests cancellation; restoring floor setup does not undo completed baked lighting data.	現在の Baked / Mixed ライトと読み込まれたシーン設定で Unity の照明ベイクを開始しますか？\n\n床だけでなくシーン全体のベイクです。長時間かかる場合があり、ベイク済み照明データを更新します。ライトモードは変更しません。続行前に照明設定を確認し、保持したい作業を保存してください。\n\nオクルージョンデータはベイクしません。「ライティングのベイクを中止」は中止を要求します。床設定の復元で完了済みの照明ベイクは取り消せません。	현재 Baked / Mixed 조명으로 로드된 씬 설정의 Unity 조명 베이크를 시작할까요?\n\n바닥 전용이 아닌 씬 전체 베이크입니다. 오래 걸릴 수 있으며 베이크된 조명 데이터를 갱신합니다. 조명 모드는 변경하지 않습니다. 진행 전에 조명 설정을 확인하고 보존할 작업을 저장하세요.\n\n오클루전 데이터는 베이크하지 않습니다. 조명 베이크 취소는 취소를 요청하며, 바닥 설정 복원은 완료된 조명 베이크 데이터를 되돌리지 않습니다.
Apply the displayed plan for {0} rows / {1} generated mesh entries?\n\nThis creates generated mesh assets and renderer-only LOD proxies, and changes eligible LODGroups. Original mesh assets and gameplay objects remain intact. Populated existing levels are preserved.\n\nUndo and persistent Restore Last Batch are provided with conflict checks. Scenes are marked dirty but are NOT automatically saved. Review scene changes before saving.	表示中の {0} 行 / 生成メッシュ {1} 件のプランを適用しますか？\n\n生成メッシュアセットとレンダラーのみの LOD プロキシを作成し、適格な LODGroup を変更します。元のメッシュアセットとゲームプレイオブジェクトは維持し、内容のある既存レベルを保持します。\n\nUndo と競合確認付きの永続的な前回バッチ復元を提供します。シーンは変更状態になりますが自動保存しません。保存前にシーンの変更を確認してください。	표시된 항목 {0}개 / 생성된 메시 항목 {1}개의 계획을 적용할까요?\n\n생성 메시 에셋 및 렌더러 전용 LOD 프록시를 만들고 적합한 LODGroup을 변경합니다. 원본 메시 에셋과 게임플레이 오브젝트는 그대로 유지하며 이미 채워진 기존 단계를 보존합니다.\n\n충돌 검사가 포함된 실행 취소와 영구적인 마지막 일괄 작업 복원을 제공합니다. 씬은 변경 상태가 되지만 자동 저장하지 않습니다. 저장 전에 씬 변경 사항을 검토하세요.
Restore the last saved LOD optimizer batch, including its original LODGroup configuration and generated proxies/assets?\n\nThe backend checks for conflicting edits and refuses unsafe restoration. This does not restore unrelated scene work. No scene is automatically saved.	元の LODGroup 構成と生成プロキシ / アセットを含め、前回保存した LOD 最適化バッチを復元しますか？\n\nバックエンドが編集の競合を確認し、危険な復元を拒否します。無関係なシーン作業は復元しません。シーンは自動保存しません。	원래 LODGroup 설정과 생성 프록시/에셋을 포함해 마지막으로 저장된 LOD 최적화 일괄 작업을 복원할까요?\n\n백엔드가 충돌하는 편집을 검사하고 안전하지 않은 복원을 거부합니다. 관련 없는 씬 작업은 복원하지 않습니다. 씬을 자동 저장하지 않습니다.
Packages, built-in or transient asset: read-only here.	Packages、組み込み、一時アセット: ここでは読み取り専用です。	Packages, 내장 또는 임시 에셋: 여기서는 읽기 전용입니다.
Only 2D textures are supported; render textures, arrays and cubemaps are locked.	2D テクスチャのみ対応します。RenderTexture、配列、キューブマップはロックされています。	2D 텍스처만 지원합니다. 렌더 텍스처, 배열 및 큐브맵은 잠깁니다.
Generated lighting/reflection asset: regenerate with its owning workflow.	生成された照明・反射アセット: 所有するワークフローで再生成してください。	생성된 조명/반사 에셋: 소유 워크플로에서 다시 생성하세요.
Embedded/sub-asset texture: owned by another asset.	埋め込み・サブアセットのテクスチャ: 別のアセットが所有しています。	내장/하위 에셋 텍스처: 다른 에셋이 소유합니다.
Asset or importer is not open for edit; check version-control checkout.	アセットまたはインポーターが編集不可です。バージョン管理のチェックアウトを確認してください。	에셋 또는 임포터를 편집할 수 없습니다. 버전 관리 체크아웃을 확인하세요.
Asset or metadata is read-only.	アセットまたはメタデータが読み取り専用です。	에셋 또는 메타데이터가 읽기 전용입니다.
No supported TextureImporter or standalone native Texture2D .asset.	対応する TextureImporter または単独のネイティブ Texture2D .asset がありません。	지원하는 TextureImporter 또는 독립 네이티브 Texture2D .asset이 없습니다.
Native Default only | S/A not applicable	ネイティブ Default のみ | S/A 対象外	네이티브 Default 전용 | S/A 해당 없음
Native texture must be the only object in its .asset file.	ネイティブテクスチャは .asset ファイル内の唯一のオブジェクトである必要があります。	네이티브 텍스처는 .asset 파일의 유일한 오브젝트여야 합니다.
Native texture has unsaved changes; save or revert them before scanning.	ネイティブテクスチャに未保存の変更があります。スキャン前に保存または元に戻してください。	네이티브 텍스처에 저장되지 않은 변경이 있습니다. 스캔 전에 저장하거나 되돌리세요.
Native asset or metadata is missing.	ネイティブアセットまたはメタデータがありません。	네이티브 에셋 또는 메타데이터가 없습니다.
Native resizing currently supports RGBA32, DXT1 and DXT5 only.	現在のネイティブサイズ変更は RGBA32、DXT1、DXT5 のみ対応します。	네이티브 크기 변경은 현재 RGBA32, DXT1 및 DXT5만 지원합니다.
Native baked lighting texture: use its owning lighting workflow.	ネイティブのベイク済み照明テクスチャ: 所有する照明ワークフローを使用してください。	네이티브 베이크 조명 텍스처: 소유 조명 워크플로를 사용하세요.
Native texture assets have no platform overrides; choose Default.	ネイティブテクスチャにはプラットフォーム別オーバーライドがありません。Default を選んでください。	네이티브 텍스처 에셋에는 플랫폼 오버라이드가 없습니다. Default를 선택하세요.
Source is smaller than the minimum offered 32-pixel cap.	元のサイズが最小選択肢の 32 ピクセル上限未満です。	원본이 제공되는 최소 상한인 32픽셀보다 작습니다.
Native pixels are resampled in place; original .asset bytes are backed up before writing. All platforms use the same resized asset.	ネイティブピクセルは同じアセット内に再サンプリングします。書き込み前に元の .asset バイト列をバックアップします。全プラットフォームで同じ変更後アセットを使います。	네이티브 픽셀을 제자리 재샘플링하며 쓰기 전에 원본 .asset 바이트를 백업합니다. 모든 플랫폼이 동일한 크기 변경 에셋을 사용합니다.
D {0} | S {1} inherited | A {2} inherited	D {0} | S {1} 継承 | A {2} 継承	D {0} | S {1} 상속 | A {2} 상속
D {0} | S {1}* | A {2} inherited	D {0} | S {1}* | A {2} 継承	D {0} | S {1}* | A {2} 상속
D {0} | S {1} inherited | A {2}*	D {0} | S {1} 継承 | A {2}*	D {0} | S {1} 상속 | A {2}*
Non-2D importer shape is unsupported.	2D 以外のインポーター形状は非対応です。	2D가 아닌 임포터 형태는 지원하지 않습니다.
Lighting importer: use lighting workflow, not resolution-cap edits.	照明用インポーター: 解像度上限の変更ではなく照明ワークフローを使用してください。	조명 임포터: 해상도 상한 편집이 아닌 조명 워크플로를 사용하세요.
Sprite/atlas inputs are locked to preserve packing, slicing and UI layout.	パッキング、スライス、UI 配置を維持するため Sprite / アトラス入力はロックされています。	패킹, 슬라이싱 및 UI 레이아웃을 보존하기 위해 스프라이트/아틀라스 입력은 잠깁니다.
Source dimensions unavailable; cannot estimate imported resolution.	元の寸法が不明なため、インポート後の解像度を推定できません。	원본 크기를 알 수 없어 임포트 해상도를 추정할 수 없습니다.
Inherited explicit texture format is unsupported by the chosen platform; select a supported format deliberately in Inspector.	継承した明示的テクスチャ形式は対象プラットフォームで非対応です。Inspector で対応形式を意図的に選択してください。	상속된 명시적 텍스처 형식을 선택한 플랫폼이 지원하지 않습니다. Inspector에서 지원 형식을 명시적으로 선택하세요.
Default is masked by {0}override(s); those platforms do not change.	Default は {0} のオーバーライドにより無効です。該当プラットフォームは変わりません。	Default는 {0} 오버라이드로 가려집니다. 해당 플랫폼은 변경되지 않습니다.
Apply enables the {0} override using inherited Default settings; inactive settings are backed up exactly.	適用すると継承した Default 設定で {0} のオーバーライドを有効にします。無効な設定も正確にバックアップします。	적용은 상속된 Default 설정으로 {0} 오버라이드를 활성화하며 비활성 설정도 정확하게 백업합니다.
Choose a valid folder under Assets. Project scope also deliberately excludes Packages.	Assets 内の有効なフォルダーを選んでください。プロジェクト範囲も意図的に Packages を除外します。	Assets 아래의 유효한 폴더를 선택하세요. 프로젝트 범위에서도 의도적으로 Packages를 제외합니다.
Select one or more scene objects or prefab assets, then scan.	シーンオブジェクトまたはプレハブアセットを 1 つ以上選んでスキャンしてください。	씬 오브젝트 또는 프리팹 에셋을 하나 이상 선택한 뒤 스캔하세요.
Not a texture asset: {0}	テクスチャアセットではありません: {0}	텍스처 에셋이 아님: {0}
Native byte-backup path is missing.	ネイティブのバイトバックアップパスがありません。	네이티브 바이트 백업 경로가 없습니다.
Native byte backup must belong to this project's optimizer backup directory.	ネイティブのバイトバックアップはこのプロジェクトの最適化バックアップフォルダー内にある必要があります。	네이티브 바이트 백업은 이 프로젝트의 최적화 백업 디렉터리에 있어야 합니다.
Unity could not preserve the native {0} format at the proposed dimensions.	提案した寸法でネイティブ形式 {0} を Unity が維持できませんでした。	Unity가 제안한 크기에서 네이티브 {0} 형식을 보존하지 못했습니다.
Native assets support Default only.	ネイティブアセットは Default のみ対応します。	네이티브 에셋은 Default만 지원합니다.
Native source changed while backing up. No asset write.	バックアップ中にネイティブの元データが変わりました。アセットは書き込んでいません。	백업 중 네이티브 원본이 변경되었습니다. 에셋을 기록하지 않았습니다.
Original native bytes backed up; native resize in progress.	元のネイティブバイト列をバックアップ済み。サイズ変更中です。	원본 네이티브 바이트를 백업했으며 크기 변경 중입니다.
Native asset/metadata changed during resize. No asset write.	サイズ変更中にネイティブアセット・メタデータが変わりました。書き込みは行っていません。	크기 변경 중 네이티브 에셋/메타데이터가 변경되었습니다. 에셋을 기록하지 않았습니다.
Native {0} pixels resized; original bytes retained in {1}	ネイティブ {0} ピクセルをサイズ変更しました。元のバイト列を {1} に保持しています。	네이티브 {0} 픽셀 크기를 변경했습니다. 원본 바이트는 {1}에 보존됩니다.
Native asset missing, moved or replaced. Restore refused.	ネイティブアセットがないか、移動・置換されています。復元を拒否しました。	네이티브 에셋이 없거나 이동/교체되었습니다. 복원을 거부했습니다.
Native asset missing, moved, locked or metadata changed. Restore refused.	ネイティブアセットがないか、移動、ロック、メタデータ変更があります。復元を拒否しました。	네이티브 에셋이 없거나 이동/잠금 상태이거나 메타데이터가 변경되었습니다. 복원을 거부했습니다.
Already matches original native bytes.	元のネイティブバイト列と既に一致しています。	이미 원본 네이티브 바이트와 일치합니다.
Native asset changed since Apply, or interrupted save is uncertain. Restore refused; retain original byte backup for manual review.	適用後にネイティブアセットが変わったか、中断した保存の状態が不明です。復元を拒否しました。手動確認のため元のバイトバックアップを保持してください。	적용 이후 네이티브 에셋이 변경되었거나 중단된 저장 상태가 불확실합니다. 복원을 거부했습니다. 수동 검토를 위해 원본 바이트 백업을 보존하세요.
Original native byte backup is missing or changed. Restore refused.	元のネイティブバイトバックアップがないか変更されています。復元を拒否しました。	원본 네이티브 바이트 백업이 없거나 변경되었습니다. 복원을 거부했습니다.
Restoring original native bytes.	元のネイティブバイト列を復元中です。	원본 네이티브 바이트를 복원 중입니다.
Exact original native .asset bytes restored; metadata/GUID unchanged.	元のネイティブ .asset バイト列を正確に復元しました。メタデータと GUID は変更していません。	정확한 원본 네이티브 .asset 바이트를 복원했습니다. 메타데이터/GUID는 변경되지 않았습니다.
Null or duplicate texture request.	空または重複したテクスチャ要求です。	비어 있거나 중복된 텍스처 요청입니다.
{0}: asset/importer changed since scan. Rescan before applying.	{0}: スキャン後にアセット・インポーターが変わりました。適用前に再スキャンしてください。	{0}: 스캔 후 에셋/임포터가 변경되었습니다. 적용 전에 다시 스캔하세요.
{0}: require an offered cap; native pixel resampling cannot exceed the source.	{0}: 提供された上限を指定してください。ネイティブの再サンプリングは元の寸法を超えられません。	{0}: 제공되는 상한이 필요합니다. 네이티브 픽셀 재샘플링은 원본 크기를 초과할 수 없습니다.
{0}: optimizer never increases an effective cap. Use Inspector for deliberate resolution increases.	{0}: このツールは有効な上限を上げません。意図的な解像度増加は Inspector で行ってください。	{0}: 최적화 도구는 유효 상한을 높이지 않습니다. 의도적인 해상도 증가는 Inspector를 사용하세요.
{0}: unchanged cap; skipped (no override created).	{0}: 上限変更なし。スキップしました (オーバーライドは作成していません)。	{0}: 변경되지 않은 상한으로 건너뛰었습니다(오버라이드 생성 없음).
Cannot safely inspect platform record presence in this Unity version.	この Unity バージョンではプラットフォーム記録の有無を安全に確認できません。	이 Unity 버전에서 플랫폼 기록 존재 여부를 안전하게 검사할 수 없습니다.
Cannot safely inspect platform record names in this Unity version.	この Unity バージョンではプラットフォーム記録名を安全に確認できません。	이 Unity 버전에서 플랫폼 기록 이름을 안전하게 검사할 수 없습니다.
Wait for Edit mode and all imports/compilation to finish.	編集モードになり、全インポートとコンパイルが完了するまで待ってください。	편집 모드에서 모든 임포트/컴파일이 끝날 때까지 기다리세요.
Invalid, already used or foreign-project plan.	無効、使用済み、または別プロジェクトのプランです。	유효하지 않거나 이미 사용했거나 다른 프로젝트의 계획입니다.
Changed, moved, locked or source edited since preview. No write.	プレビュー後に変更、移動、ロック、元データの編集がありました。書き込みは行っていません。	미리 보기 후 변경, 이동, 잠금 또는 원본 편집이 발생했습니다. 기록하지 않았습니다.
Cap applied and reimport completed.	上限を適用し、再インポートが完了しました。	상한 적용 및 재임포트가 완료되었습니다.
{0}: {1:t} (see backup state)	{0}: {1} (バックアップの状態を確認)	{0}: {1} (백업 상태 확인)
Backup must belong to this project's optimizer backup directory.	バックアップはこのプロジェクトの最適化バックアップフォルダー内にある必要があります。	백업은 이 프로젝트의 최적화 백업 디렉터리에 있어야 합니다.
Unsupported or foreign-project backup.	非対応または別プロジェクトのバックアップです。	지원하지 않거나 다른 프로젝트의 백업입니다.
Asset missing/locked or source changed. Restore refused.	アセットがない、ロック中、または元データが変わっています。復元を拒否しました。	에셋이 없거나 잠겼거나 원본이 변경되었습니다. 복원을 거부했습니다.
Already matches original importer.	元のインポーターと既に一致しています。	이미 원래 임포터와 일치합니다.
Importer edited since Apply (or uncertain interrupted import). Restore refused; retain backup for manual review.	適用後にインポーターが編集されたか、中断したインポートの状態が不明です。復元を拒否しました。手動確認のためバックアップを保持してください。	적용 후 임포터가 편집되었거나 중단된 임포트 상태가 불확실합니다. 복원을 거부했습니다. 수동 검토를 위해 백업을 보존하세요.
Original resolution/override settings restored.	元の解像度・オーバーライド設定を復元しました。	원래 해상도/오버라이드 설정을 복원했습니다.
Planned	計画済み	계획됨
Applying	適用中	적용 중
Applied	適用済み	적용됨
Restoring	復元中	복원 중
Restored	復元済み	복원됨
RestoreConflict	復元の競合	복원 충돌
Conflict	競合	충돌
Applied {0}; restored {1}; unchanged {2}; conflicts {3}; failed {4}; pending {5}	適用 {0}; 復元 {1}; 変更なし {2}; 競合 {3}; 失敗 {4}; 保留 {5}	적용 {0}; 복원 {1}; 변경 없음 {2}; 충돌 {3}; 실패 {4}; 대기 {5}
Applied {0}; restored {1}; unchanged {2}; conflicts {3}; failed {4}; pending {5}; CANCELED	適用 {0}; 復元 {1}; 変更なし {2}; 競合 {3}; 失敗 {4}; 保留 {5}; 中止	적용 {0}; 복원 {1}; 변경 없음 {2}; 충돌 {3}; 실패 {4}; 대기 {5}; 취소됨
Wait for an idle Unity editor in Edit mode.	Unity が編集モードでアイドル状態になるまで待ってください。	Unity 에디터가 편집 모드에서 유휴 상태가 될 때까지 기다리세요.
LOD operation canceled; no further objects were processed.	LOD 操作を中止しました。これ以上のオブジェクトは処理していません。	LOD 작업을 취소했습니다. 추가 오브젝트는 처리하지 않았습니다.
Select scene objects before analyzing selection.	選択対象を分析する前にシーンのオブジェクトを選んでください。	선택 항목을 분석하기 전에 씬 오브젝트를 선택하세요.
Repeated source mesh: up to {0} instances in this scope; ranked using aggregate triangle work. Mesh assets are reused at equal requested reductions.	繰り返し使用される元メッシュ: この範囲で最大 {0} インスタンス。三角形の合計作業量で順位付けします。同じ削減要求にはメッシュアセットを再利用します。	반복 원본 메시: 이 범위에 최대 {0}개 인스턴스가 있습니다. 합산 삼각형 작업량으로 순위를 매기며 동일한 감소 요청에는 메시 에셋을 재사용합니다.
No populated LOD0 renderer; cannot reconstruct authored high-detail geometry.	内容のある LOD0 レンダラーがなく、作成済みの高詳細形状を再構築できません。	채워진 LOD0 렌더러가 없어 작성된 고세부 지오메트리를 재구성할 수 없습니다.
LODGroup is disabled; preserve its authored ownership.	LODGroup は無効です。既存の所有設定を維持します。	LODGroup이 비활성화되어 있어 작성된 소유권을 보존합니다.
Nested LOD groups require manual ownership resolution.	入れ子の LOD グループは手動で所有関係を解決する必要があります。	중첩 LOD 그룹은 소유권을 수동으로 해결해야 합니다.
Existing group has missing renderer references; repair them manually first.	既存グループに欠落したレンダラー参照があります。先に手動で修正してください。	기존 그룹에 누락된 렌더러 참조가 있습니다. 먼저 수동으로 복구하세요.
Skinned/particle/line/procedural LOD members are not supported.	スキン、パーティクル、ライン、プロシージャルな LOD メンバーは非対応です。	스킨/파티클/라인/절차적 LOD 구성원은 지원하지 않습니다.
Existing group has external/cross-scene renderer membership; inspection-only.	既存グループに外部・別シーンのレンダラーが含まれるため参照のみです。	기존 그룹에 외부/다른 씬 렌더러가 포함되어 있어 검사 전용입니다.
A renderer belongs to competing LOD groups.	同じレンダラーが競合する複数の LOD グループに属しています。	렌더러가 충돌하는 여러 LOD 그룹에 속합니다.
Ungrouped renderer overlaps an existing ancestor/descendant LOD owner.	グループ外のレンダラーが既存の親・子 LOD の所有範囲と重なっています。	그룹에 속하지 않은 렌더러가 기존 조상/자손 LOD 소유자와 겹칩니다.
Existing populated LOD coverage retained; only missing requested levels can be filled.	内容のある既存 LOD を保持し、要求した未設定レベルのみ補完できます。	채워진 기존 LOD 범위를 보존하며 요청한 빈 단계만 채울 수 있습니다.
Recommended by static geometry estimate (>=1,000 triangles); not measured frame-time or FPS.	静的ジオメトリ推定 (三角形 1,000 以上) による推奨で、実測フレーム時間や FPS ではありません。	정적 지오메트리 추정치(삼각형 1,000개 이상)에 따른 권장이며 실측 프레임 시간이나 FPS가 아닙니다.
Safe manual candidate, below the automatic 1,000-triangle threshold.	自動選択の三角形 1,000 個の基準未満ですが、安全な手動候補です。	자동 기준인 삼각형 1,000개 미만이지만 안전한 수동 후보입니다.
LOD0 has {0} material slot(s); static counts do not measure draw calls or overdraw.	LOD0 のマテリアルスロット数は {0}。静的な個数はドローコールやオーバードローの測定ではありません。	LOD0의 머티리얼 슬롯은 {0}개입니다. 정적 개수는 드로 콜이나 오버드로를 측정하지 않습니다.
Prefab/project assets are read-only; select a loaded scene instance.	プレハブ・プロジェクトアセットは読み取り専用です。読み込まれたシーンのインスタンスを選んでください。	프리팹/프로젝트 에셋은 읽기 전용입니다. 로드된 씬 인스턴스를 선택하세요.
Only loaded non-preview scene instances may be modified.	読み込まれたプレビュー用でないシーンのインスタンスのみ変更できます。	로드된 미리 보기용이 아닌 씬 인스턴스만 변경할 수 있습니다.
Save this scene before authoring persistent reversible LODs.	永続的で復元可能な LOD を作成する前にシーンを保存してください。	영구적이며 복원 가능한 LOD를 작성하기 전에 씬을 저장하세요.
Hidden/noneditable/temporary scene object is read-only.	非表示・編集不可・一時的なシーンオブジェクトは読み取り専用です。	숨김/편집 불가/임시 씬 오브젝트는 읽기 전용입니다.
Only static MeshRenderer + MeshFilter geometry is supported; no skinned/particle/procedural substitution.	静的な MeshRenderer + MeshFilter の形状のみ対応します。スキン、パーティクル、プロシージャル形状の代替は行いません。	정적 MeshRenderer + MeshFilter 지오메트리만 지원하며 스킨/파티클/절차적 대체는 지원하지 않습니다.
Generated/regenerator-owned geometry is read-only.	生成済み・再生成システム所有の形状は読み取り専用です。	생성기/재생성기 소유 지오메트리는 읽기 전용입니다.
UI, text, signage or readable graphics must retain original geometry.	UI、文字、看板、読み取るためのグラフィックは元の形状を維持する必要があります。	UI, 텍스트, 표지판 또는 읽을 수 있는 그래픽은 원본 지오메트리를 유지해야 합니다.
Pickup/Udon/animation/dynamic hierarchy owns this renderer.	このレンダラーは Pickup / Udon / アニメーション / 動的な階層が所有しています。	픽업/Udon/애니메이션/동적 계층이 이 렌더러를 소유합니다.
Script-controlled hierarchy or missing script; renderer substitution is not safe.	スクリプト制御またはスクリプト欠落のある階層で、レンダラーの代替は安全ではありません。	스크립트 제어 계층이거나 스크립트가 누락되어 렌더러 대체가 안전하지 않습니다.
A script/animation references this renderer, mesh, filter or hierarchy; preserve its ownership.	スクリプト・アニメーションがこのレンダラー、メッシュ、フィルター、階層を参照しています。所有関係を維持します。	스크립트/애니메이션이 이 렌더러, 메시, 필터 또는 계층을 참조하므로 소유권을 보존합니다.
Disabled/force-off authored renderer must not be bypassed by proxies.	作成者が無効・強制非表示にしたレンダラーをプロキシで迂回してはいけません。	작성된 비활성/강제 숨김 렌더러를 프록시로 우회해서는 안 됩니다.
Property blocks/additional vertex streams cannot be safely inherited.	プロパティブロック・追加頂点ストリームを安全に継承できません。	프로퍼티 블록/추가 정점 스트림을 안전하게 상속할 수 없습니다.
Lightmap-baked/realtime-lightmapped renderer is inspection-only; reduced interpolation cannot guarantee the authored lightmap UV result.	ベイク済み・リアルタイムのライトマップ付きレンダラーは参照のみです。削減後の補間では作成済みライトマップ UV の結果を保証できません。	베이크/실시간 라이트맵 렌더러는 검사 전용입니다. 감소된 보간으로는 작성된 라이트맵 UV 결과를 보장할 수 없습니다.
Mesh is missing/non-readable/empty; original import settings are never changed.	メッシュがない、読み取り不可、または空です。元のインポート設定は変更しません。	메시가 없거나 읽기 불가이거나 비어 있습니다. 원래 임포트 설정은 절대 변경하지 않습니다.
Skin, blendshape or bind-pose mesh is not supported.	スキン、ブレンドシェイプ、バインドポーズ付きメッシュは非対応です。	스킨, 블렌드셰이프 또는 바인드 포즈 메시는 지원하지 않습니다.
Material/submesh mapping is not one-to-one.	マテリアルとサブメッシュの対応が 1 対 1 ではありません。	머티리얼/하위 메시 매핑이 일대일이 아닙니다.
Only populated triangle submeshes are supported.	三角形を含むサブメッシュのみ対応します。	채워진 삼각형 하위 메시만 지원합니다.
Transparent/cutout/deforming/unknown material retains original geometry.	透明、カットアウト、変形、不明なマテリアルは元の形状を維持します。	투명/컷아웃/변형/알 수 없는 머티리얼은 원본 지오메트리를 유지합니다.
Custom shader is outside the verified built-in non-displacing opaque allowlist.	カスタムシェーダーは検証済みの組み込み・変位なし・不透明シェーダー許可リストの対象外です。	커스텀 셰이더는 검증된 내장 비변위 불투명 셰이더 허용 목록 밖에 있습니다.
An analyzed object no longer exists; analyze again.	分析したオブジェクトが存在しません。再分析してください。	분석한 오브젝트가 더 이상 없습니다. 다시 분석하세요.
{0}: renderer/LOD owner was removed; analyze again.	{0}: レンダラー / LOD の所有者が削除されました。再分析してください。	{0}: 렌더러/LOD 소유자가 제거되었습니다. 다시 분석하세요.
{0}: skipped. {1:t}	{0}: スキップ。{1}	{0}: 건너뜀. {1}
Mesh reducer returned no geometry for {0}	{0} に対してメッシュ削減処理が形状を返しませんでした	메시 감소기가 {0}의 지오메트리를 반환하지 않았습니다
Requested {0}% fewer vertices (target {1}); actual {2} / {3} vertices, {4} triangles.	要求: 頂点を {0}% 削減 (目標 {1})。実際: {2} / {3} 頂点、{4} 三角形。	요청: 정점 {0}% 감소(목표 {1}개); 실제 정점 {2} / {3}개, 삼각형 {4}개.
Requested {0}% fewer vertices (target {1}); actual {2} / {3} vertices, {4} triangles. Conservative topology/shape constraints prevented the full requested reduction.	要求: 頂点を {0}% 削減 (目標 {1})。実際: {2} / {3} 頂点、{4} 三角形。保守的なトポロジー・形状制約のため要求どおりの削減はできませんでした。	요청: 정점 {0}% 감소(목표 {1}개); 실제 정점 {2} / {3}개, 삼각형 {4}개. 보수적 토폴로지/형태 제약으로 요청한 전체 감소율을 달성하지 못했습니다.
{0}: all requested levels already populated; unchanged.	{0}: 要求した全レベルが既に設定済みです。変更はありません。	{0}: 요청한 모든 단계가 이미 채워져 있어 변경하지 않았습니다.
{0}: skipped: topology/shape constraints prevent useful vertex AND triangle reduction against an original or preceding level; no unhelpful or partial LOD set will be authored.	{0}: スキップ: トポロジー・形状制約により元または前のレベルより頂点と三角形の両方を有効に削減できません。役に立たない、または部分的な LOD セットは作成しません。	{0}: 건너뜀: 토폴로지/형태 제약으로 원본 또는 이전 단계 대비 정점과 삼각형을 모두 유용하게 줄일 수 없습니다. 유용하지 않거나 불완전한 LOD 세트는 작성하지 않습니다.
LOD{0} static simultaneous geometry: {1} triangles versus these objects' {2} LOD0 triangles ({3} fewer). Levels are alternatives, not additive simultaneous savings.	LOD{0} の静的同時ジオメトリ: 三角形 {1}、対象の LOD0 は {2} ({3} 削減)。各レベルは切り替え候補であり、同時削減量として合算できません。	LOD{0} 정적 동시 지오메트리: 삼각형 {1}개 대 해당 오브젝트의 LOD0 삼각형 {2}개({3}개 감소). 단계는 대안이며 동시에 합산되는 절감량이 아닙니다.
Static geometry preview only: no CPU/GPU timing, FPS prediction, gameplay validation or automatic non-LOD fixes. Shape-preserving endpoint reduction can stop above the selected target; inspect the preview.	静的形状のプレビューのみです。CPU/GPU 時間、FPS 予測、ゲームプレイ検証、LOD 以外の自動修正は行いません。形状を維持する端点削減は選択した目標より多い数で停止することがあります。プレビューを確認してください。	정적 지오메트리 미리 보기 전용이며 CPU/GPU 시간 측정, FPS 예측, 게임플레이 검증 또는 비LOD 자동 수정은 하지 않습니다. 형태 보존 끝점 감소는 선택한 목표보다 많은 개수에서 멈출 수 있습니다. 미리 보기를 검사하세요.
Choose 2–8 total levels with matching reduction/transition arrays.	削減率と遷移値の配列を一致させ、合計 2～8 レベルを指定してください。	감소/전환 배열을 일치시켜 총 2~8개 단계를 선택하세요.
LOD0 must retain the original mesh (0% reduction).	LOD0 は元のメッシュを維持する必要があります (削減 0%)。	LOD0은 원본 메시를 유지해야 합니다(감소율 0%).
Reductions must increase from 0% and remain below 100%.	削減率は 0% から増加し、100% 未満である必要があります。	감소율은 0%부터 증가하며 100% 미만이어야 합니다.
Screen-relative transitions must decrease within 0–1.	画面相対遷移値は 0～1 の範囲で減少する必要があります。	화면 상대 전환값은 0~1 범위에서 감소해야 합니다.
Requested missing-level transitions conflict with retained populated levels. Adjust transitions; existing levels are never changed.	未設定レベルに要求した遷移値が保持する既存レベルと競合します。遷移値を調整してください。既存レベルは変更しません。	요청한 빈 단계 전환값이 보존된 기존 단계와 충돌합니다. 전환값을 조정하세요. 기존 단계는 변경하지 않습니다.
Create a fresh, unapplied preview first.	まず未適用の新しいプレビューを作成してください。	먼저 적용하지 않은 새 미리 보기를 생성하세요.
No safe missing LOD levels in this preview; nothing changed.	このプレビューには安全に補完できる LOD がありません。変更はありません。	이 미리 보기에 안전하게 채울 빈 LOD 단계가 없습니다. 변경 사항이 없습니다.
Settings changed after preview; preview again.	プレビュー後に設定が変わりました。再度プレビューしてください。	미리 보기 후 설정이 변경되었습니다. 다시 미리 보기를 생성하세요.
Source/LOD/transform state changed since preview. Analyze and preview again.	プレビュー後に元データ、LOD、Transform の状態が変わりました。再分析し、プレビューを作成してください。	미리 보기 후 원본/LOD/트랜스폼 상태가 변경되었습니다. 다시 분석하고 미리 보기를 생성하세요.
Ownership/safety changed: {0}	所有関係・安全性が変わりました: {0}	소유권/안전 상태가 변경됨: {0}
Source mesh changed since preview; analyze again.	プレビュー後に元のメッシュが変わりました。再分析してください。	미리 보기 후 원본 메시가 변경되었습니다. 다시 분석하세요.
Preview geometry changed or was consumed; preview again.	プレビュー形状が変わったか使用済みです。再度プレビューしてください。	미리 보기 지오메트리가 변경되었거나 사용되었습니다. 다시 미리 보기를 생성하세요.
Incomplete preview membership; no partial row may be applied.	プレビューの構成メンバーが不完全です。行の一部だけを適用することはできません。	미리 보기 구성원이 불완전합니다. 항목을 부분적으로 적용할 수 없습니다.
LOD apply failed; the current batch was rolled back. {0:t}	LOD 適用に失敗し、現在のバッチをロールバックしました。{0}	LOD 적용에 실패하여 현재 일괄 작업을 롤백했습니다. {0}
No LOD optimizer batch receipt exists.	LOD 最適化バッチの記録はありません。	LOD 최적화 일괄 작업 기록이 없습니다.
No unrestored LOD optimizer batch exists.	未復元の LOD 最適化バッチはありません。	복원되지 않은 LOD 최적화 일괄 작업이 없습니다.
Scene/object unavailable; load its saved scene before restoring: {0}	シーン・オブジェクトを利用できません。復元前に保存したシーンを読み込んでください: {0}	씬/오브젝트를 사용할 수 없습니다. 복원 전에 저장된 씬을 로드하세요: {0}
{0}: {1:t} Nothing removed.	{0}: {1} 削除は行っていません。	{0}: {1} 제거하지 않았습니다.
Generated mesh assets retained intentionally for Undo and any saved-scene references; no original asset was changed or deleted.	Undo と保存済みシーンの参照のため、生成メッシュアセットは意図的に保持しています。元のアセットは変更・削除していません。	실행 취소 및 저장된 씬 참조를 위해 생성 메시 에셋을 의도적으로 보존합니다. 원본 에셋은 변경하거나 삭제하지 않았습니다.
Restored {0} object/group(s); conflicts {1}. Save modified scenes.	オブジェクト / グループ {0} 件を復元、競合 {1} 件。変更したシーンを保存してください。	오브젝트/그룹 {0}개 복원; 충돌 {1}개. 변경된 씬을 저장하세요.
Applied {0} object/group(s), {1} proxy renderer(s), {2} new mesh asset(s). Originals, materials, colliders and scripts unchanged. Existing populated levels retained. Save the modified scenes to persist the result.	オブジェクト / グループ {0} 件、プロキシレンダラー {1} 件、新規メッシュアセット {2} 件を適用しました。元データ、マテリアル、コライダー、スクリプトは未変更です。既存レベルを保持しています。結果を残すには変更したシーンを保存してください。	오브젝트/그룹 {0}개, 프록시 렌더러 {1}개, 새 메시 에셋 {2}개를 적용했습니다. 원본, 머티리얼, 콜라이더 및 스크립트는 변경되지 않았으며 채워진 기존 단계를 보존했습니다. 결과를 유지하려면 변경된 씬을 저장하세요.
Undo is available; Restore Last Batch also survives editor restart after saving scenes.	Undo が利用できます。シーン保存後はエディター再起動後も前回バッチの復元が利用できます。	실행 취소가 가능하며 씬 저장 후에는 에디터 재시작 후에도 마지막 일괄 작업 복원을 사용할 수 있습니다.
LOD restore failed; this restore operation was rolled back. {0:t}	LOD 復元に失敗し、この復元操作をロールバックしました。{0}	LOD 복원에 실패하여 이번 복원 작업을 롤백했습니다. {0}
Incomplete batch or missing LOD owner needs manual inspection.	不完全なバッチまたは欠落した LOD 所有者を手動で確認する必要があります。	불완전한 일괄 작업 또는 누락된 LOD 소유자를 수동 검사해야 합니다.
LOD settings/membership changed since apply.	適用後に LOD 設定・構成メンバーが変わりました。	적용 이후 LOD 설정/구성원이 변경되었습니다.
Original renderer/transform/mesh reference state changed since apply.	適用後に元のレンダラー・Transform・メッシュ参照の状態が変わりました。	적용 이후 원본 렌더러/트랜스폼/메시 참조 상태가 변경되었습니다.
An owned proxy is missing.	所有するプロキシがありません。	소유 프록시가 누락되었습니다.
An owned proxy was modified or has foreign components/children.	所有するプロキシが変更されたか、別のコンポーネント・子が追加されています。	소유 프록시가 수정되었거나 외부 컴포넌트/자식이 있습니다.
Another LODGroup now references an owned proxy.	別の LODGroup が所有するプロキシを参照しています。	다른 LODGroup이 소유 프록시를 참조합니다.
A previous renderer reference is missing; exact restoration is not possible.	以前のレンダラー参照が欠落し、正確に復元できません。	이전 렌더러 참조가 누락되어 정확한 복원이 불가능합니다.
A script/animation now references an owned proxy.	スクリプト・アニメーションが所有するプロキシを参照しています。	스크립트/애니메이션이 소유 프록시를 참조합니다.
A script-controlled or missing-script hierarchy now owns a proxy.	スクリプト制御またはスクリプト欠落のある階層がプロキシを所有しています。	스크립트 제어 또는 스크립트 누락 계층이 프록시를 소유합니다.
Cannot fingerprint an unresolved serialized object reference on {0}.	{0} の未解決シリアライズ済みオブジェクト参照のフィンガープリントを作成できません。	{0}의 확인되지 않은 직렬화 오브젝트 참조의 지문을 생성할 수 없습니다.
Cannot activate the source scene for LOD proxy creation.	LOD プロキシ作成のために元のシーンをアクティブにできません。	LOD 프록시 생성을 위해 원본 씬을 활성화할 수 없습니다.
NOT MEASURED: this is a static component inventory, not CPU/GPU profiling, FPS, actual draw calls, simulation cost, overdraw or network traffic. Counts include disabled components on included objects.	未実測: 静的なコンポーネント一覧であり、CPU/GPU プロファイリング、FPS、実際のドローコール、シミュレーション負荷、オーバードロー、通信量ではありません。対象オブジェクト内の無効なコンポーネントも数えます。	실측 아님: 정적 컴포넌트 목록이며 CPU/GPU 프로파일링, FPS, 실제 드로 콜, 시뮬레이션 비용, 오버드로 또는 네트워크 트래픽이 아닙니다. 포함된 오브젝트의 비활성 컴포넌트도 집계합니다.
Lights: {0} total; {1} realtime; {2} mixed; {3} realtime/mixed with shadows configured; {4} of those currently enabled/active.	ライト: 合計 {0}、Realtime {1}、Mixed {2}、影を設定した Realtime / Mixed {3}、そのうち現在有効・アクティブ {4}。	조명: 총 {0}개; 실시간 {1}개; 혼합 {2}개; 그림자가 설정된 실시간/혼합 {3}개; 그중 현재 활성 {4}개.
Particles: {0} systems; {1} particle renderers. No emission/overdraw timing was sampled.	パーティクル: システム {0}、レンダラー {1}。放出・オーバードローの時間は測定していません。	파티클: 시스템 {0}개; 파티클 렌더러 {1}개. 방출/오버드로 시간은 측정하지 않았습니다.
Material indicators: {0} renderer(s) with transparent-queue/alpha-blend materials; {1} with alpha-test/cutout indicators. Shader classification is not a measured pixel cost.	マテリアルの指標: 透明キュー・アルファブレンドのレンダラー {0}、アルファテスト・カットアウトの指標 {1}。シェーダーの分類はピクセル負荷の実測ではありません。	머티리얼 지표: 투명 큐/알파 블렌드 머티리얼 렌더러 {0}개; 알파 테스트/컷아웃 지표 {1}개. 셰이더 분류는 실측 픽셀 비용이 아닙니다.
Scripts: {0} MonoBehaviour components, including {1} Udon/UdonSharp components (proxy and backing components may both be counted); {2} missing-script slots. No execution frequency or runtime workload was measured.	スクリプト: MonoBehaviour {0} 個、うち Udon / UdonSharp {1} 個 (プロキシと実体の両方を数える場合あり)、欠落スクリプト {2} 枠。実行頻度・実行時負荷は測定していません。	스크립트: MonoBehaviour {0}개(그중 Udon/UdonSharp {1}개; 프록시와 배킹 컴포넌트가 모두 집계될 수 있음); 누락 스크립트 슬롯 {2}개. 실행 빈도나 런타임 작업량은 측정하지 않았습니다.
No non-LOD settings were changed. Use the Unity Profiler and VRChat client measurements before changing lights, particles, materials or scripts.	LOD 以外の設定は変更していません。ライト、パーティクル、マテリアル、スクリプトを変更する前に Unity Profiler と VRChat クライアントで測定してください。	비LOD 설정은 변경하지 않았습니다. 조명, 파티클, 머티리얼 또는 스크립트를 변경하기 전에 Unity Profiler와 VRChat 클라이언트 측정을 사용하세요.
Occlusion authoring requires idle Edit mode.	オクルージョンの編集にはアイドル状態の編集モードが必要です。	오클루전 작성에는 유휴 상태의 편집 모드가 필요합니다.
Select one loaded scene object, not a project asset.	プロジェクトアセットではなく、読み込まれたシーンのオブジェクトを 1 つ選んでください。	프로젝트 에셋이 아닌 로드된 씬 오브젝트 하나를 선택하세요.
Save the scene before creating a restorable occluder change.	復元可能な遮蔽物の変更を作成する前にシーンを保存してください。	복원 가능한 차폐물 변경을 생성하기 전에 씬을 저장하세요.
Select the actual MeshRenderer object, not a container. No geometry was inferred from child bounds.	コンテナーではなく実際の MeshRenderer オブジェクトを選んでください。子の境界から形状を推測することはしていません。	컨테이너가 아닌 실제 MeshRenderer 오브젝트를 선택하세요. 자식 바운드로 지오메트리를 추측하지 않았습니다.
The renderer is inactive, hidden or shadows-only; it cannot safely stand in for a visible blocker.	レンダラーは非アクティブ、非表示、または影専用のため、可視の遮蔽物として安全に使用できません。	렌더러가 비활성, 숨김 또는 그림자 전용이어서 가시 차폐물을 안전하게 대신할 수 없습니다.
Moving, animated, cloth and UI objects are not supported as baked static occluders.	移動、アニメーション、クロス、UI オブジェクトは静的ベイク遮蔽物として非対応です。	이동, 애니메이션, 천 및 UI 오브젝트는 베이크된 정적 차폐물로 지원하지 않습니다.
Missing scripts prevent verifying static ownership.	スクリプト欠落により静的な所有関係を確認できません。	누락된 스크립트로 인해 정적 소유권을 확인할 수 없습니다.
A script, Udon behaviour, pickup or station controls this hierarchy. Keep its existing visibility policy.	スクリプト、Udon、Pickup、Station がこの階層を制御しています。既存の可視性方針を維持してください。	스크립트, Udon 비헤이비어, 픽업 또는 스테이션이 이 계층을 제어합니다. 기존 가시성 정책을 유지하세요.
No opaque material is assigned.	不透明なマテリアルが割り当てられていません。	불투명 머티리얼이 지정되지 않았습니다.
Missing material/shader.	マテリアル・シェーダーがありません。	머티리얼/셰이더가 없습니다.
Transparent and alpha-cutout surfaces must not be treated as solid blockers.	透明・アルファカットアウト面を完全な遮蔽物として扱ってはいけません。	투명 및 알파 컷아웃 표면을 완전 차폐물로 취급해서는 안 됩니다.
Unknown shader opacity: use Unity's Occlusion window after manually verifying its rendering, rather than automatic approval.	シェーダーの不透明性は不明です。自動承認せず、描画を手動確認してから Unity の Occlusion ウィンドウを使用してください。	셰이더 불투명도를 알 수 없습니다. 자동 승인 대신 렌더링을 수동 확인한 뒤 Unity Occlusion 창을 사용하세요.
This renderer is a lower LOD. Select its original LOD0 geometry for occluder authoring.	このレンダラーは低詳細 LOD です。遮蔽物の編集には元の LOD0 形状を選んでください。	이 렌더러는 저세부 LOD입니다. 차폐물 작성에는 원래 LOD0 지오메트리를 선택하세요.
Occluder Static: {0}. {1:t}	Occluder Static: {0}。{1}	Occluder Static: {0}. {1}
Eligible fixed opaque geometry. Baking uses the actual mesh, not a solid approximation of its bounding box.	適格な固定不透明形状です。ベイクは境界ボックスを埋めた近似ではなく実際のメッシュを使用します。	적합한 고정 불투명 지오메트리입니다. 베이크는 바운딩 박스의 고체 근사치가 아닌 실제 메시를 사용합니다.
This marks only this object. Other objects need appropriate Occludee settings, and static occlusion data must be baked for the scene. It does not instantly hide everything behind the object. Gaps, sightlines, cameras and VR views still matter. Existing runtime visibility controllers are separate and are not rewritten.	このオブジェクトのみを設定します。他のオブジェクトには適切な Occludee 設定が必要で、シーンの静的オクルージョンデータをベイクする必要があります。背後の全てが即座に隠れるわけではありません。隙間、視線、カメラ、VR 視点が影響します。既存の実行時可視性制御は別であり、書き換えません。	이 오브젝트만 표시합니다. 다른 오브젝트에는 적절한 Occludee 설정이 필요하며 씬의 정적 오클루전 데이터를 베이크해야 합니다. 뒤의 모든 것을 즉시 숨기지는 않습니다. 틈, 시선, 카메라 및 VR 시점도 중요합니다. 기존 런타임 가시성 컨트롤러는 별개이며 다시 작성하지 않습니다.
Occluder setting is already {0}; no change.	遮蔽物設定は既に {0} です。変更はありません。	차폐물 설정이 이미 {0}입니다. 변경 사항이 없습니다.
Marked {0} as Occluder Static {1}. Scene left unsaved; Undo and Restore Last Occluder Change are available. No bake was started; existing bake data has not been regenerated.	{0} の Occluder Static を {1} に設定しました。シーンは未保存です。Undo と前回の遮蔽物変更の復元が利用できます。ベイクは開始せず、既存のベイクデータは再生成していません。	{0}의 Occluder Static을 {1}로 표시했습니다. 씬은 저장하지 않았으며 실행 취소와 마지막 차폐물 변경 복원이 가능합니다. 베이크를 시작하지 않았으며 기존 베이크 데이터를 재생성하지 않았습니다.
No recorded occluder change to restore.	復元する遮蔽物変更の記録はありません。	복원할 차폐물 변경 기록이 없습니다.
Invalid occluder receipt.	遮蔽物の復元記録が無効です。	차폐물 복원 기록이 유효하지 않습니다.
Settings already restored (possibly by Undo).	設定は既に復元されています (Undo による可能性があります)。	설정이 이미 복원되었습니다(실행 취소에 의한 것일 수 있음).
Settings changed after this tool's edit; restore refused to preserve your changes.	このツールの編集後に設定が変わったため、ご自身の変更を保護するため復元を拒否しました。	이 도구의 편집 이후 설정이 변경되어 사용자 변경을 보존하기 위해 복원을 거부했습니다.
Restored original static flags on {0}. Scene left unsaved; no bake was started.	{0} の元の静的フラグを復元しました。シーンは未保存で、ベイクは開始していません。	{0}의 원래 정적 플래그를 복원했습니다. 씬은 저장하지 않았으며 베이크를 시작하지 않았습니다.
This shader does not have a verified baked-lighting receiver path (Unlit materials do not display floor shadows).	このシェーダーには検証済みのベイク照明受け取り経路がありません (Unlit は床の影を表示しません)。	이 셰이더에는 검증된 베이크 조명 수신 경로가 없습니다(Unlit 머티리얼은 바닥 그림자를 표시하지 않음).
A readable mesh with authored lightmap UV2 is required. This tool will not rewrite a shared mesh/importer to guess UVs.	作成済みのライトマップ UV2 を持つ読み取り可能なメッシュが必要です。このツールは UV 推測のため共有メッシュ・インポーターを書き換えません。	작성된 라이트맵 UV2가 있는 읽기 가능한 메시가 필요합니다. 이 도구는 UV를 추측하기 위해 공유 메시/임포터를 다시 작성하지 않습니다.
Eligible fixed opaque floor renderer with lightmap UV2.	ライトマップ UV2 を持つ適格な固定不透明床レンダラーです。	라이트맵 UV2가 있는 적합한 고정 불투명 바닥 렌더러입니다.
Prepare changes only this renderer's Contribute GI flag and Receive GI mode. Existing Baked/Mixed lights and shadow-casting geometry determine the result. Baking is scene-wide, not floor-only; it can take a long time. No lights, UVs, colliders or runtime scripts are changed.	準備はこのレンダラーの Contribute GI と Receive GI のみを変更します。既存の Baked / Mixed ライトと影を落とす形状が結果を決めます。ベイクは床だけでなくシーン全体で、長時間かかる場合があります。ライト、UV、コライダー、実行時スクリプトは変更しません。	준비는 이 렌더러의 Contribute GI 플래그와 Receive GI 모드만 변경합니다. 기존 Baked/Mixed 조명 및 그림자를 드리우는 지오메트리가 결과를 결정합니다. 베이크는 바닥 전용이 아닌 씬 전체 작업이며 오래 걸릴 수 있습니다. 조명, UV, 콜라이더 또는 런타임 스크립트는 변경하지 않습니다.
Floor already contributes GI and receives lightmaps. No setup changed; no bake started.	床は既に GI に寄与しライトマップを受け取っています。設定変更もベイク開始も行っていません。	바닥이 이미 GI에 기여하며 라이트맵을 받습니다. 설정을 변경하거나 베이크를 시작하지 않았습니다.
Floor prepared for baked lightmap shadows. Scene left unsaved. Restore Last Scene Setup Change recovers its previous flags/GI mode. No bake or light-mode conversion started.	床をベイクしたライトマップの影用に準備しました。シーンは未保存です。前回のシーン設定復元で以前のフラグ・GI モードに戻せます。ベイクやライトモード変換は開始していません。	바닥을 베이크된 라이트맵 그림자용으로 준비했습니다. 씬은 저장하지 않았습니다. 마지막 씬 설정 변경 복원으로 이전 플래그/GI 모드를 복구할 수 있습니다. 베이크나 조명 모드 변환은 시작하지 않았습니다.
Lighting bake requires an idle editor in Edit mode.	照明ベイクには編集モードのアイドル状態が必要です。	조명 베이크에는 편집 모드의 유휴 에디터가 필요합니다.
A lighting bake is already running; use Cancel Lighting Bake if needed.	照明ベイクは既に実行中です。必要ならベイクの中止を使用してください。	조명 베이크가 이미 실행 중입니다. 필요하면 조명 베이크 취소를 사용하세요.
Save one loaded scene before baking; this tool never saves or closes scenes for you.	ベイク前に読み込まれたシーンを 1 つ保存してください。このツールはシーンを代わりに保存・閉じることはしません。	베이크 전에 로드된 씬 하나를 저장하세요. 이 도구는 사용자를 대신해 씬을 저장하거나 닫지 않습니다.
No enabled Baked/Mixed shadow-casting light exists. Configure lights explicitly; no realtime light was converted.	有効な影付き Baked / Mixed ライトがありません。明示的にライトを設定してください。Realtime ライトは変換していません。	활성화된 그림자 투사 Baked/Mixed 조명이 없습니다. 조명을 명시적으로 설정하세요. 실시간 조명은 변환하지 않았습니다.
Unity refused to start the scene lighting bake. Inspect its Console; no fallback bake was started.	Unity がシーン照明のベイク開始を拒否しました。Console を確認してください。代替のベイクは開始していません。	Unity가 씬 조명 베이크 시작을 거부했습니다. Console을 확인하세요. 대체 베이크는 시작하지 않았습니다.
Unity scene-wide lighting bake started with existing settings. This is not a completed bake or a floor-only bake. Cancel Lighting Bake is available; setup restoration does not roll back generated lighting data.	既存の設定で Unity のシーン全体の照明ベイクを開始しました。完了済みでも床専用でもありません。ベイクの中止が利用できます。設定の復元で生成済み照明データは戻せません。	기존 설정으로 Unity 씬 전체 조명 베이크를 시작했습니다. 완료된 베이크 또는 바닥 전용 베이크가 아닙니다. 조명 베이크 취소가 가능하며 설정 복원은 생성된 조명 데이터를 롤백하지 않습니다.
No lighting bake is running.	実行中の照明ベイクはありません。	실행 중인 조명 베이크가 없습니다.
Lighting bake cancellation requested. Existing/generated lighting data is not represented as restored.	照明ベイクの中止を要求しました。既存・生成済みの照明データが復元されたという意味ではありません。	조명 베이크 취소를 요청했습니다. 기존/생성 조명 데이터가 복원되었다는 의미는 아닙니다.
Texture inventory	テクスチャ一覧	텍스처 목록
Inspecting textures	テクスチャを確認中	텍스처 검사 중
Applying texture caps	テクスチャの上限を適用中	텍스처 상한 적용 중
Restoring texture caps	テクスチャの上限を復元中	텍스처 상한 복원 중
World LOD optimizer	ワールド LOD 最適化	월드 LOD 최적화
Inspecting script/animation ownership	スクリプト・アニメーションの所有関係を確認中	스크립트/애니메이션 소유권 검사 중
Analyzing {0}	{0} を分析中	{0} 분석 중
Preview {0} / LOD{1}	{0} / LOD{1} をプレビュー	{0} / LOD{1} 미리 보기
Saving generated mesh assets	生成メッシュアセットを保存中	생성 메시 에셋 저장 중
Applying {0}	{0} を適用中	{0} 적용 중
Restoring LOD batch {0}	LOD バッチ {0} を復元中	LOD 일괄 작업 {0} 복원 중
Restore LOD optimizer batch	LOD 最適化バッチを復元	LOD 최적화 일괄 작업 복원
Set occluder flag	遮蔽物のフラグを設定	차폐물 플래그 설정
Restore optimizer scene settings	最適化ツールのシーン設定を復元	최적화 도구 씬 설정 복원
Restore floor lightmap mode	床のライトマップモードを復元	바닥 라이트맵 모드 복원
Prepare baked floor shadows	ベイクした床の影を準備	베이크된 바닥 그림자 준비
";
}
}
