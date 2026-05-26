# NotesTest — Ticky Tacky Village

MIDIのノートオンイベントに合わせて、ミニチュア集落が成長していく鑑賞型演出プロジェクト。

---

## コンセプト

参考動画（"Little Boxes" / Ticky Tacky スタイル）を元に、  
ゲームプレイではなく「集落がタイムラプスのように発展していく様子」を見せる演出。

---

## シーン構成

**アクティブシーン:** `Assets/Scenes/SampleScene.unity`

| GameObjectu | コンポーネント | 役割 |
|---|---|---|
| Spawner | `MidiSpawner` | メイン演出スクリプト |
| Main Camera | Camera + UniversalAdditionalCameraData | URPカメラ |
| Directional Light | Light | 環境光 |
| PostFX Volume | Volume (Global) | Bloom + Vignette |

---

## MidiSpawner コンポーネント

`Assets/Scripts/MidiSpawner.cs`

### インスペクタ設定

| フィールド | 説明 | 推奨値 |
|---|---|---|
| Midi Asset | `.mid` ファイル（MidiFileAsset） | Loop1.mid |
| Audio Clip | 対応する音声ファイル | Loop1.wav |
| Track Index | MIDIトラック番号 | 0 |
| Grid Size | オブジェクト間隔 (m) | 2.8 |
| Pan Speed | カメラ追従速度 | 1.2 |
| Zoom Speed | カメラズーム速度 | 0.6 |
| Min Zoom | 最小カメラ距離 | 6 |
| Max Zoom | 最大カメラ距離 | 22 |
| Camera Pitch | カメラ仰角 (度) | 48 |
| Camera Yaw | カメラ方位角 (度) | 45 |

---

## 演出の仕組み

### スポーンのタイミング

MIDIファイルと音声ファイルは**別々に再生**し、`audioSource.time`（再生秒数）を基準に同期する。

**Start()時の前処理：**

1. `MidiFileAsset.tracks[n].template.events` から全 NoteOn イベントを取得
2. MIDIティック → 秒数に変換して `noteTimes[]` リストに保存
   ```
   秒数 = tick × secondsPerBeat / ticksPerQuarterNote
   secondsPerBeat = 60 / tempo(BPM)
   ```
3. `AudioSource.Play()` で音声再生開始

**Update()毎フレーム：**

```
audioSource.time（現在の再生秒数）
    ↓
noteTimes[noteIndex] <= 現在時刻 になるまでループ
    ↓
該当するノートイベントごとにオブジェクトをスポーン
noteIndex をインクリメント
```

遅延や補正は行わず、音声の再生位置をそのまま基準にするシンプルな設計。  
音声がループしたとき（`time` が大幅に巻き戻る）は `noteIndex` もリセットする。

**Loop1.mid の場合：** 28個のノートオン / 0.00s〜3.75s / 120BPM

---

### オブジェクト出現パターン

MIDIノートオンのたびに以下のサイクルで生成：

```
H H T H H T F H H T H H T F ...
(H=家, T=木, F=花)
```

- **7回に1回 → 花**（既存オブジェクト周辺にランダム配置）
- **3回に1回 → 木**（緑/白 ランダム、ジッターあり）
- **それ以外 → 家**（5色パレット、BFS展開）

### グリッド展開（BFS）

```
家フロンティア (上下左右) → 家が建つ
木フロンティア (斜め方向) → 木が生える
```

家が建つたびに隣接マスへ展開するため、中心から外側へ自然に広がる。

### ループ処理

1. `AudioSource.loop = true` で音声を無限ループ
2. `audioSource.time` が前フレームより大幅に減少 → ループ検出
3. 全オブジェクトを縮小アニメーションで消去（0.22秒）
4. BFS・インデックスをリセットして最初から再構築

---

## カメラ挙動

参考動画のドローン撮影風に近い動作。

| 要素 | 実装 |
|---|---|
| 角度 | 固定アイソメトリック (Pitch 48°, Yaw 45°) |
| 回転 | なし |
| パン | 村の重心へ Lerp 追従 |
| ズーム | 村の広がり × 2.4 でターゲット距離を算出し Lerp |
| イージング | Vector3.Lerp / Mathf.Lerp による指数減衰 |

---

## オブジェクト一覧

| 種類 | 構成 | カラー |
|---|---|---|
| 家 | Wall (Cube) + Roof (Cube, 45°回転) + Window | 5色パレット + 茶屋根 |
| 木（緑） | Trunk (Cylinder) + Leaf (Sphere) | LeafGreen + TrunkBrown |
| 木（白） | Trunk (Cylinder) + Leaf (Sphere) | LeafWhite + TrunkBrown |
| 花 | Stem (Cylinder) + Bloom (Sphere) | ピンク or 黄色 |
| 岩 | Rock (Cube) | グレー、ゲーム開始時に7個配置 |

---

## カラーパレット

明るいパステル系。緑・白・水色・クリーム・茶を中心に構成。

```
壁: #F5DEBB  #B8E3F5  #F5C7C7  #D7F5C2  #F5EBB8
屋根: #996647
葉（緑）: #85C266
葉（白）: #E0EDD6
幹: #8A5C33
花: #F5ADBA / #F5E080
地面: #80BD6B
岩: #A39990
```

---

## 使用パッケージ

| パッケージ | バージョン | 用途 |
|---|---|---|
| Universal RP | Unity 6 標準 | レンダリング |
| jp.keijiro.klak.timeline.midi | 1.0.2 | MIDIファイル読み込み |

### MidiAnimationTrack インストール

Package Manager → Add package from git URL:

```
https://github.com/keijiro/MidiAnimationTrack.git#upm
```

---

## MIDIファイルの設定方法

1. `.mid` ファイルを `Assets/Music/midi/` にドロップ
2. 対応する音声ファイル（.wav/.mp3）を `Assets/Music/wav/` にドロップ
3. Spawner の `MidiSpawner` コンポーネントに両方をアサイン
4. `Track Index` を変えると別トラックのノートを使用可能

---

## ファイル構成

```
Assets/
├── Music/
│   ├── midi/   Loop1.mid, Note.mid, Poly.mid, CC.mid, Drums.mid
│   └── wav/    Loop1.wav, Poly.wav, Drums.wav
├── Scenes/
│   └── SampleScene.unity   (アクティブ)
├── Scripts/
│   ├── MidiSpawner.cs      (メイン)
│   ├── TickyTackySpawner.cs (BPMベースの旧版、無効化済み)
│   ├── Note.cs              (未使用)
│   └── RhythmGameManager.cs (未使用)
└── Settings/               URP設定
```

---

## 既知の注意点

- **`Application.runInBackground = true`** をStart()内で設定済み。UnityがGame Viewのフォーカスを失っても動作を継続するために必要。
- **MaterialPropertyBlock** を使用。URP環境でnew Material()を毎フレーム生成するとクラッシュするため。
- MCP経由でスクリプトを書き換えた場合は `refresh_unity (mode: force, compile: request)` が必要。
