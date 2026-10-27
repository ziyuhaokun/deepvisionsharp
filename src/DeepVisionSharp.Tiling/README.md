# DeepVisionSharp.Tiling

切图与尺寸几何。输入图像尺寸和请求，输出源图窗口或画布映射；不读文件、不依赖 OpenCV / TorchSharp。

分类 1.0 的训练和推理**不调用**本模块：整张图在 `TorchSharpImageTensor.ToClassificationTensor` 里线性拉伸到网络输入。产品要切图时，在 Application / TorchSharp 引用本 project，传入 `TileRequest` / `ImageFitKind`，不要把窗口公式写进推理后端。

```text
PixelSize + TileRequest                 -> TilePlanner.Plan            -> TilePlan
PixelSize + canvas + Stretch/Letterbox/Cover/Pad
                                        -> ImageFits.Compute           -> ImageFit
PixelSize + shortEdge [, maxLongEdge]   -> ImageFits.ShortEdge         -> ImageFit
PixelSize + longEdge                    -> ImageFits.LongEdge          -> ImageFit
ImageFit + canvas point                 -> CoordinateMaps.TryToSource  -> source point or none
PixelSize + stride                      -> PixelSizes.AlignUp          -> canvas multiple of 32/64
```

## 切图 `TileRequest`

| 请求 | 社区对应 | 结果 |
| --- | --- | --- |
| `WholeImage` | 整图分类 / 检测单次前向 | 一个窗口，覆盖整张图 |
| `SlidingWindow(tile, overlap, remainder)` | SAHI `slice_image` | 固定切块；图比切块小则一个窗口 |
| `Grid(rows, columns)` | 均匀宫格 | 格子均分，余数摊到前几格，边长最多差 1 |
| `Crop(size, anchor)` | torchvision `CenterCrop` | 一个源窗口；大于图时夹到图内 |

滑窗末片 `RemainderPolicy`：

| 策略 | 行为 |
| --- | --- |
| `EdgeAlign`（默认） | 最后一块贴边，窗口仍是完整 tile（SAHI） |
| `Drop` | 丢弃伸到图外的块；图比 tile 小则仍保留一块 |
| `Pad` | 原点按 stride 前进，末块源是图内交集，`Canvas` 仍是 tile；源贴在画布左上，留白在右 / 下 |

每个 `TileWindow` 有 `Source`（图内交集）、`Canvas`（送给网络的画布）和 `Dest`（源在画布上的位置）。只有 `Pad` 末块会出现 `Canvas != Source.Size`。

## 适配 `ImageFitKind`

切图决定**从哪块源图取**；适配决定**这块怎么放进网络输入**。

| 种类 | 社区对应 | 几何 |
| --- | --- | --- |
| `Stretch` | torchvision `Resize((H,W))`、当前分类热路径 | 两轴独立缩放，铺满画布 |
| `Letterbox` | Ultralytics `letterbox` | 等比缩小装进画布，短边留白 |
| `Cover` | torchvision `Resize` + `CenterCrop` | 等比放大盖住画布，裁掉溢出 |
| `Pad` | albumentations `PadIfNeeded` | 不缩放，画布至少是 `max(源, 最小画布)` |
| `ShortEdge` | Detectron2 `MIN_SIZE` / torchvision short-side | 短边缩到给定值；可选 `maxLongEdge`。不要走 `Compute`，用 `ImageFits.ShortEdge` |
| `LongEdge` | albumentations `LongestMaxSize` | 长边缩到给定值。不要走 `Compute`，用 `ImageFits.LongEdge` |

`FitAnchor`：`Center`（默认）或 `TopLeft`。Letterbox / Pad / Cover / Crop 都认它。

YOLO 常见的「边长对齐到 32」用 `PixelSizes.AlignUp(canvas, 32)`，再把对齐后的尺寸传给 `Letterbox`。

`CoordinateMaps.TryToSource` / `ToSource` 把画布上的点 / 框映回原图。留白和裁掉的区域不能映回；点用 `TryToSource`，框与内容区无交集时返回 null。分类 1.0 不需要。

填充色、tensor、NMS、结果聚合都不在这里。
