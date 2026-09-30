# 0.4.0 互动与休息素材记录

使用 Codex 内置 ImageGen；参考 `assets/eat-bun/00.png` 的角色身份，生成新动作。设置 `transparent_background: true`。原图保留，运行时按 2 列 × 3 行裁切，不对图片做程序重绘或背景替换。

## 共同要求

```text
Production-ready PNG sprite sheet for one adorable Q-version Chinese Nezha desktop pet. Attached reference is CHARACTER IDENTITY ONLY: no food, ignore reference background. Preserve black twin-bun hair with golden ornaments, red forehead flame, red and gold costume with dark pants, red magical ribbons and wind-fire wheels. Halo MUST be much larger than head, ethereal transparent warm-white/red divine light, subtle luminous gold accents, not a solid yellow metal ring. Clean polished 2D game illustration, bold tidy outlines, cheerful chibi proportions. Output exactly TWO columns by THREE rows of SIX equal square cells, portrait 2:3 ratio (1024x1536 ideal). Full transparent background, no cell gutters, no grid lines, no text or labels. Every frame completely inside its own square with 8% padding including halo ribbons and wheels. Same fixed front-facing camera, full-body scale, vertical baseline and center in all six cells. Read left to right, top to bottom. SIX sequential animation poses of ONE character, coherent distinct motion, no extra characters or human hands.
```

## 摸头：touch-head.png

```text
A gentle head-patting reaction with NO hand or other person: frame 1 curious relaxed face; frame 2 head tilts slightly and eyes soften; frame 3 eyes closed with warm smile and subtle blush; frame 4 delighted smile, cheeks squash slightly, tiny proud nod; frame 5 content soft smile and half-open eyes; frame 6 settles into calm cute happy pose. Distinct coherent sequential poses, anatomically consistent, both arms and both feet visible, full body.
```

## 挠痒：touch-body.png

```text
Animation is cute ticklish belly giggling: 1 alert surprised eyes, arms lifted slightly; 2 leans a little left with smiling eyes, hands near belly; 3 laughing closed eyes, both hands clutching belly; 4 cute small upward bounce with feet and wheels still wholly in the cell; 5 leans a little right giggling, ribbons sway; 6 settles into a warm content smile, hands relaxed. No extra props or food. Keep feet baseline as consistent as possible and no frame touching another cell.
```

## 午睡：sleep.png

```text
Animation is a calm looping nap in a cross-legged lotus floating seated pose: eyes CLOSED in ALL six frames, hands relaxed on knees or folded, serene cute expression. Red ribbons lie around the pose gently and wind-fire wheels under the seated body have subdued warm flames. Very large semitransparent sacred halo glows softly behind the head. Frames 1 through 6 show a subtle breathing cycle: resting, inhaling a little, chest and shoulders slightly lifted, soft exhale, relaxed lowered chest, then returns to first pose. No words/Z letters/speech bubbles, no food, no bed or pillow, no flashing alarm. Sitting pose anchored consistently in ALL cells, transparent space around character, whole feet/support wheels visible, clear visual distinction from awake idle.
```

## 神环与风火轮

`src/AnimationPlayer.cs` 使用 WPF 绘制动态光效：神环使用半透明径向渐变和呼吸亮度，风火轮使用红金拖尾与火花。该特效不占鼠标命中区域，也不修改角色图集。
