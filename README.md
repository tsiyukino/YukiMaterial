# Yuki Material

Non-destructive material editing for VRChat avatars, applied at build time through
[NDMF](https://github.com/bdunderscore/ndmf).

[English](#english) · [中文](#中文) · [日本語](#日本語)

## English

Give a material slot several looks — the same face with several makeups, the same hair in several colours —
and switch between them from one menu that reaches across objects, so the hair and the ears change together.
Only the difference from the original material is stored, and the build gets a patched copy: your material
asset never changes and your project never grows a folder of near-identical variants.

### Install

1. Add these VPM listings to VCC / ALCOM:
   - TsiYuki: `https://tsiyukino.github.io/vpm-repos/index.json`
2. Add **Yuki Material** to your project.

### Use

Everything is set up in one window: **Tools > TsiYuki > Material Editor**, or right-click an object in the
Hierarchy and choose **TsiYuki > Edit Materials**.

1. Add the objects whose materials you want to change. Each one gets a `Yuki Material` component holding
   its slots — that component is a catalogue and nothing more.
2. Pick a slot and give it a look. A look is edited through the shader's own inspector, with images you can
   drop straight onto it; only what you change is stored.
3. To switch in game, make a menu, add the material slots it should move — they can be on any object in the
   avatar — and fill in the table: one row per state, one column per slot, each cell saying what that slot
   wears. A slot the state is not about wears the material it already has.
4. A slot no menu drives simply wears the look you set as its own, applied on upload.
5. Check the result in the Scene view (NDMF preview), Try on a state or two, then upload.

One menu costs one synced int (8 bits), however many slots on however many objects it moves.

## 中文

给一个材质插槽准备多个外观——同一张脸的几种妆、同一头发的几种颜色——再用一个菜单统一切换；菜单可以跨物体引用，
所以头发和耳朵能一起变。组件只记录与原材质的差异，构建时生成一份改好的副本：原材质资产不会被改动，工程里也不会
堆出一堆几乎一样的材质。

1. 在 VCC / ALCOM 中添加上面的 VPM 源，然后添加 **Yuki Material**。
2. 打开 **Tools > TsiYuki > Material Editor**（或在 Hierarchy 里右键物体 → **TsiYuki > Edit Materials**），
   所有设置都在这个面板里完成。
3. 把要改材质的物体加进来，每个物体会得到一个 `Yuki Material` 组件，它只负责记录这个物体的插槽和外观。
4. 选中插槽、新建外观，用 shader 自己的界面编辑，或者直接往上面拖图片叠加。
5. 想在游戏里切换，就新建一个菜单，把它该改的材质插槽加进来（可以在模型里的任意物体上），然后填状态表：
   一行一个状态，一列一个插槽，格子里写这个状态下该插槽穿什么。和这个状态无关的插槽保持原样即可。
6. 没有菜单管的插槽，就穿它自己设定的「常驻外观」，上传时直接生效。
7. 在 Scene 视图（NDMF 预览）里确认、试穿几个状态，然后上传。

一个菜单无论驱动多少个插槽、跨多少个物体，都只花一个同步 int（8 比特）。

## 日本語

1 つのマテリアルスロットに複数の見た目を用意し（同じ顔の複数のメイク、同じ髪の複数の色）、1 つのメニューで
まとめて切り替えます。メニューはオブジェクトをまたいでスロットを参照できるので、髪と耳を同時に変えられます。
保存されるのは元のマテリアルとの差分だけで、ビルド時に複製へ適用されます。マテリアルアセットは変更されず、
プロジェクトにそっくりなマテリアルが増えることもありません。

1. VCC / ALCOM に上記の VPM リポジトリを追加し、**Yuki Material** を導入します。
2. **Tools > TsiYuki > Material Editor**（または Hierarchy で右クリック → **TsiYuki > Edit Materials**）を開きます。
   設定はすべてこのパネルで行います。
3. マテリアルを変えたいオブジェクトを追加します。オブジェクトごとに `Yuki Material` が付き、そのスロットと
   見た目を保持します。
4. スロットを選んで見た目を作り、シェーダー自身のインスペクターで編集するか、画像をドロップして重ねます。
5. ゲーム内で切り替えるにはメニューを作り、動かしたいマテリアルスロットを追加して（アバター内のどのオブジェクトでも
   構いません）表を埋めます。行が状態、列がスロット、セルがその状態で着るものです。関係のないスロットは
   そのままで構いません。
6. メニューが付いていないスロットは、自分に設定した見た目をアップロード時にそのまま着ます。
7. Scene ビュー（NDMF プレビュー）で確認し、いくつか試着してからアップロードします。

メニュー 1 つにつき同期 int 1 つ（8 ビット）だけで、動かすスロットやオブジェクトの数は関係ありません。
