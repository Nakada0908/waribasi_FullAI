# T20で指定したパッケージと配布通知

確認日：2026-10-29（日本時間）。Unity 6000.0.23f1。

| パッケージ | 採用リリース | manifestに固定したコミット | ライセンス |
|---|---|---|---|
| UniTask | 2.5.11 | `2e993ff18f28c931602a07292df0b0804eebef99` | MIT / Copyright (c) 2019 Yoshifumi Kawai / Cysharp, Inc. |
| LitMotion | 2.0.2 (`v2.0.2`) | `0b4c588ee75a07198841d92aab653e6b39445089` | MIT / Copyright (c) 2023 Yusuke Nakada |

- [UniTask公式UPM手順（採用コミット）](https://github.com/Cysharp/UniTask/tree/2e993ff18f28c931602a07292df0b0804eebef99#upm-package)
- [UniTask採用版MIT原文](https://github.com/Cysharp/UniTask/blob/2e993ff18f28c931602a07292df0b0804eebef99/LICENSE)
- [LitMotion公式UPM手順（採用コミット）](https://github.com/annulusgames/LitMotion/tree/0b4c588ee75a07198841d92aab653e6b39445089#installation)
- [LitMotion採用版MIT原文](https://github.com/annulusgames/LitMotion/blob/0b4c588ee75a07198841d92aab653e6b39445089/LICENSE)

Git URLは公式の`?path=...`形式に固定コミットを付けて使用する。ブランチ名だけの追従は行わない。単純なフェードをC#で設定しており、LitMotion.Animationは使用・導入していない。

UniTaskのpackage.jsonはUnity 2018.4以上、追加依存なし。公式READMEのGit path対応要件はUnity 2019.3.4f1以上。LitMotionの公式要件はUnity 2021.3以上。package.jsonの依存はBurst 1.6.0、Collections 1.5.1、Mathematics 1.0.1。手元の解決済みキャッシュはそれぞれ1.8.24、2.5.1、1.3.2で、いずれも既存の依存である。T20で他の既存パッケージの指定版は変更していない。

**UPMの実解決は未確認。** manifestには上記2件を追加したが、packages-lock.json・既存EditorのPackageCacheはまだ新しいパッケージを含まない。開いているEditorの強制終了やロック削除は行っていない。UnityでAssets > Refreshを実行し、Package Managerで上記2版がエラーなく解決されたことを確認するまで、導入成功とは扱わない。なお着手前からBurstはmanifestが1.8.13、lock/cacheが1.8.24という不一致があった。解決後に既存依存の差分も確認すること。

## 原文と配布場所

`UniTask/LICENSE.txt`と`LitMotion/LICENSE.txt`は採用コミットのルート`LICENSE`のGit blobをそのまま保存したもの。翻訳・省略をしていない。公式UPM指定のサブディレクトリには両方ともLICENSEファイルが含まれていないため、リポジトリルートの原文をここへ保存した。取得した元リポジトリとパッケージソースは改変していない。

配布用には同じ原文を`Assets/StreamingAssets/ThirdPartyLicenses/`へ複製した。Windows等は`<製品名>_Data/StreamingAssets/ThirdPartyLicenses/`、WebGLは配布する`StreamingAssets/ThirdPartyLicenses/`、Android等はPlayer内のStreamingAssetsに含まれる構成。今回、製品ビルドによる梱包の実確認は行っていない。配布時にはStreamingAssetsを省かないこと。

LitMotionが利用する既存依存についても、現在のキャッシュからBurst 1.8.24、Collections 2.5.1、Mathematics 1.3.2のLICENSE.mdを`Dependencies/`へ原文保存し、同じ配布場所へ複製した。Burstの`Third Party Notices.md`も含める。これらはMITではなく、各原文の条件が適用される。UPMの解決で依存版が変わった場合は、対応する原文の更新が必要。

既存のプロジェクトルート`LICENSE`（Copyright (c) 2026 NT）は維持した。着手前の追跡ファイルには配布用の第三者通知フォルダがなく、今回StreamingAssetsの通知位置を追加した。本資料は全既存素材・全パッケージのライセンス監査完了を示すものではない。
