# Yuki Material

Non-destructive material editing for VRChat avatars, applied at build time through
[NDMF](https://github.com/bdunderscore/ndmf).

[English](#english) · [中文](#中文) · [日本語](#日本語)

## English

Change a material — any property its shader exposes — without duplicating it. The component stores only
the difference from the original, and the build gets a patched copy. Your material asset never changes
and your project never grows a folder of near-identical material variants.

### Install

1. Add these VPM listings to VCC / ALCOM:
   - TsiYuki: `https://tsiyukino.github.io/vpm-repos/index.json`
2. Add **Yuki Material** to your project.

### Use

1. Add `TsiYuki/Yuki Material` to the object whose materials you want to change, or anywhere under the
   avatar and pick the renderers by hand.
2. The target list fills itself from that object's renderers. Delete the rows you do not need.
3. Edit each target through the shader's own inspector. Only what you change is stored.
4. Check the result in the Scene view (NDMF preview), then upload.

## 中文

在构建时（NDMF）非破坏性地修改 VRChat 模型的材质，不改动原材质，也不会在工程里多出材质副本。

1. 在 VCC / ALCOM 中添加上面的 VPM 源，然后添加 **Yuki Material**。
2. 把 `TsiYuki/Yuki Material` 组件挂到要改材质的物体上（或挂在模型下任意位置后手动选择渲染器）。
3. 目标列表会自动填入该物体的材质槽，删掉不需要的行。
4. 用 shader 自己的面板直接编辑，组件只记录你改动的部分。
5. 在 Scene 视图（NDMF 预览）中确认效果，然后上传。

## 日本語

VRChat アバターのマテリアルを、ビルド時（NDMF）に非破壊で変更します。元のマテリアルは変更されず、
プロジェクトにマテリアルの複製も増えません。

1. VCC / ALCOM に上記の VPM リポジトリを追加し、**Yuki Material** を導入します。
2. `TsiYuki/Yuki Material` をマテリアルを変更したいオブジェクトに追加します。
3. ターゲット一覧はそのオブジェクトのマテリアルスロットから自動で埋まります。不要な行は削除してください。
4. シェーダー自身のインスペクターで編集します。変更した項目だけが保存されます。
5. Scene ビュー（NDMF プレビュー）で確認し、アップロードします。
