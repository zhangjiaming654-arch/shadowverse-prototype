# 界面美术素材 · 2026-10-07

本轮使用内置 image_gen 生成四张原创素材，并已接入实际的组卡组与对局回放界面。生图负责环境、卡面纹理和金属徽饰；文字、数值、点击和回放操作由程序绘制。

## 保存位置

交付目录：`C:/Users/67593/Documents/Codex/2026-10-07/ji/outputs/美术素材/`

项目使用目录：`C:/Users/67593/Documents/Codex/2026-09-01/s-s/src/Shadowverse.DeckEditor/Assets/`

| 文件 | 用途 | 尺寸 |
|---|---|---|
| bg_archive_v2.png | 组卡组的星空藏书殿背景 | 1672 × 941 |
| bg_arena_v2.png | 对局回放的月夜决斗场背景 | 1672 × 941 |
| card_plate_v2.png | 卡牌库、手牌和战场卡框的通用纹理 | 1024 × 1536 |
| crest_celestial_v2.png | 标题处的透明金属宝石徽饰 | 1774 × 887，含透明通道 |

素材作为新版本文件保存，原有素材未覆盖。卡面纹理是通用装饰，不对应具体卡牌的角色插画。素材加载失败时保留程序绘制的备用样式。

## 最终生成提示词

以下为四次内置 image_gen 调用的完整提示词。第一至第三张为不透明背景，第四张请求透明背景。

### 星空藏书殿

Use case: stylized-concept. Asset type: production background texture for a playable desktop fantasy card game's deck-building screen, inspired by the elegant anime fantasy UI atmosphere of Shadowverse Worlds Beyond. Generate ONE 16:9 landscape background image, preferably 1920x1080 or wider. Scene: a luxurious ancient celestial card archive, twilight blue stone and obsidian glass, elegant muted antique gold architecture, arched windows at the far sides overlooking a luminous indigo sky, subtle suspended arcane dust, restrained cyan magic light, a softly visible engraved circular celestial seal in the lower central background. Premium polished Japanese fantasy game environmental concept art, high material detail, dramatic yet calm, no people. Composition: UI will occupy most of the image; keep the middle 80% dark, quiet, low contrast with smooth navy negative space. Concentrate recognizable architectural details at extreme left and right margins and along the upper rim, where they can peek around translucent panels. Both left and right central areas must support white text, avoid bright objects in the center. Lighting: rich dark navy, desaturated blue-black, fine gold edges, a little teal, no large white bloom. This is ONLY a background asset, NOT a screenshot or UI mockup: NO buttons, NO panels, NO card rectangles, NO labels, NO text, NO numbers, NO logos, NO watermark. Do not render any UI.

### 月夜决斗场

Use case: stylized-concept. Asset type: production background for a playable fantasy card battle replay desktop screen, inspired by Shadowverse Worlds Beyond elegant anime fantasy worldbuilding. Generate ONE wide 16:9 environmental background. Scene: a moonlit celestial dueling sanctuary, grand symmetrical gothic stone terraces, distant spires and arches at the extreme sides, a large elegant engraved arcane circular arena floor in the middle distance, soft blue mist at its edges, tiny warm gold lights, muted cyan streams of magic. Premium Japanese fantasy game environment art with convincing slate, metal and glass materials. Composition: front view with slight elevated angle; card boards and UI will cover the middle 85% so this must be calm dark navy negative space with subtle stone texture, not an illustration competing with cards. Distinctive decorative architecture and bright moonlight belong only at the outer edges; gently illuminate the center floor but keep it dark enough for white text. Upper center fades into a navy sky. Mood: majestic, polished, cinematic, ethereal, legible and restrained. Palette: ink blue, charcoal stone, muted antique gold, fine silver, subtle teal. Constraints: environment texture only, NO characters, NO cards, NO HUD, NO buttons, NO interface panels, NO text, NO lettering, NO numbers, NO logos, NO watermark. NOT a screenshot or UI mockup.

### 通用卡面纹理

Use case: stylized-concept. Asset type: reusable neutral decorative card-face texture for a fantasy card game desktop UI inspired by the elegant gold and navy ornament of Shadowverse Worlds Beyond. Generate ONE upright portrait image with approximately 2:3 aspect ratio. A front-facing perfectly flat rectangular luxury enchanted grimoire cover, symmetrical gilded filigree at the very outer corners, bevelled antique gold thin double perimeter borders, charcoal navy brushed leather and obsidian glass in the center. A small finely engraved abstract eight-point celestial sigil occupies only the center, muted bronze and a dim turquoise diamond, no prominent bright glow. Texture craftsmanship rich and tactile, premium Japanese fantasy game art direction. Composition: front orthographic view, clean consistent rectangle edges, fills entire image with no surrounding scene or shadows outside. The top third and lower quarter are especially quiet very dark navy: live code will overlay cost, card names and statistics there. Whole face should remain low contrast and legible underneath white text. This is a reusable neutral card plate, NOT a specific playable card illustration and NOT a mockup. NO character, NO creature, NO text, NO numbers, NO labels, NO logo, NO watermark, NO UI buttons. No card stacks, no tilted perspective, no extra items.

### 透明金属徽饰

Use case: stylized-concept. Asset type: ONE reusable transparent ornament for an elegant fantasy card game's title header and main character status panel. Create a single centered antique gold and silver celestial heraldic medallion with a faceted luminous cyan blue diamond gem at its core, subtle symmetrical wing-like filigree flowing left and right. Inspired by premium Japanese fantasy collectible card game interface craftsmanship such as Shadowverse Worlds Beyond: crisp engraved fine metal, controlled highlights, intricate but readable silhouette. Front facing orthographic view, wide horizontally balanced heraldic motif, approximately 2:1 width to height with generous transparent margin on all sides. The cyan jewel is jewel-sized, no exaggerated neon bloom. Polished realistic metal materials. Constraints: truly transparent background with alpha, isolated object, no background, no plaque, no rectangle, no cast shadow outside the object, no text, no letters, no numbers, no logo, no watermark, no characters. Only a single refined heraldic diamond motif, not a sprite sheet or multiple variants.

## 验证

全量编译 0 警告、0 错误；完整规则自检通过；素材解码和徽饰透明通道检查通过；搜索、增减卡牌及 74 步回放首尾定位通过。两个真实窗体均检查了程序渲染预览及小窗口布局。尚未验证系统 125%/150% 缩放下的实机显示。
