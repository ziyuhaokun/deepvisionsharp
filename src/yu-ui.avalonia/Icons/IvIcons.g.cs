using System.Collections.Generic;
using Avalonia.Media;

namespace YuUI;

public static class IvIcons
{
    public const double CanvasSize = 24;

    public static Geometry Info { get; } = Create("M3 12a9 9 0 1 0 18 0a9 9 0 0 0 -18 0 M12 9h.01 M11 12h1v4h1");

    public static Geometry Success { get; } = Create("M3 12a9 9 0 1 0 18 0a9 9 0 1 0 -18 0 M9 12l2 2l4 -4");

    public static Geometry Warning { get; } = Create("M12 9v4 M10.363 3.591l-8.106 13.534a1.914 1.914 0 0 0 1.636 2.871h16.214a1.914 1.914 0 0 0 1.636 -2.87l-8.106 -13.536a1.914 1.914 0 0 0 -3.274 0 M12 16h.01");

    public static Geometry Error { get; } = Create("M3 12a9 9 0 1 0 18 0a9 9 0 1 0 -18 0 M10 10l4 4m0 -4l-4 4");

    public static Geometry Help { get; } = Create("M3 12a9 9 0 1 0 18 0a9 9 0 0 0 -18 0 M12 16v.01 M12 13a2 2 0 0 0 .914 -3.782a1.98 1.98 0 0 0 -2.414 .483");

    public static Geometry Check { get; } = Create("M5 12l5 5l10 -10");

    public static Geometry Close { get; } = Create("M18 6l-12 12 M6 6l12 12");

    public static Geometry ChevronDown { get; } = Create("M6 9l6 6l6 -6");

    public static Geometry ChevronUp { get; } = Create("M6 15l6 -6l6 6");

    public static Geometry ChevronLeft { get; } = Create("M15 6l-6 6l6 6");

    public static Geometry ChevronRight { get; } = Create("M9 6l6 6l-6 6");

    public static Geometry ArrowUp { get; } = Create("M12 5l0 14 M18 11l-6 -6 M6 11l6 -6");

    public static Geometry ArrowDown { get; } = Create("M12 5l0 14 M18 13l-6 6 M6 13l6 6");

    public static Geometry ArrowRight { get; } = Create("M5 12l14 0 M13 18l6 -6 M13 6l6 6");

    public static Geometry Menu { get; } = Create("M4 6l16 0 M4 12l16 0 M4 18l16 0");

    public static Geometry More { get; } = Create("M4 12a1 1 0 1 0 2 0a1 1 0 1 0 -2 0 M11 12a1 1 0 1 0 2 0a1 1 0 1 0 -2 0 M18 12a1 1 0 1 0 2 0a1 1 0 1 0 -2 0");

    public static Geometry Home { get; } = Create("M5 12l-2 0l9 -9l9 9l-2 0 M5 12v7a2 2 0 0 0 2 2h10a2 2 0 0 0 2 -2v-7 M9 21v-6a2 2 0 0 1 2 -2h2a2 2 0 0 1 2 2v6");

    public static Geometry WindowMinimize { get; } = Create("M5 12l14 0");

    public static Geometry WindowMaximize { get; } = Create("M3 5a2 2 0 0 1 2 -2h14a2 2 0 0 1 2 2v14a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-14");

    public static Geometry WindowRestore { get; } = Create("M9 8h7a3 3 0 0 1 3 3v7a3 3 0 0 1 -3 3h-7a3 3 0 0 1 -3 -3v-7a3 3 0 0 1 3 -3z M5 15v-8a3 3 0 0 1 3 -3h8");

    public static Geometry Add { get; } = Create("M12 5l0 14 M5 12l14 0");

    public static Geometry Delete { get; } = Create("M4 7l16 0 M10 11l0 6 M14 11l0 6 M5 7l1 12a2 2 0 0 0 2 2h8a2 2 0 0 0 2 -2l1 -12 M9 7v-3a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v3");

    public static Geometry Copy { get; } = Create("M7 9.667a2.667 2.667 0 0 1 2.667 -2.667h8.666a2.667 2.667 0 0 1 2.667 2.667v8.666a2.667 2.667 0 0 1 -2.667 2.667h-8.666a2.667 2.667 0 0 1 -2.667 -2.667l0 -8.666 M4.012 16.737a2.005 2.005 0 0 1 -1.012 -1.737v-10c0 -1.1 .9 -2 2 -2h10c.75 0 1.158 .385 1.5 1");

    public static Geometry Edit { get; } = Create("M4 20h4l10.5 -10.5a2.828 2.828 0 1 0 -4 -4l-10.5 10.5v4 M13.5 6.5l4 4");

    public static Geometry Save { get; } = Create("M6 4h10l4 4v10a2 2 0 0 1 -2 2h-12a2 2 0 0 1 -2 -2v-12a2 2 0 0 1 2 -2 M10 14a2 2 0 1 0 4 0a2 2 0 1 0 -4 0 M14 4l0 4l-6 0l0 -4");

    public static Geometry Refresh { get; } = Create("M20 11a8.1 8.1 0 0 0 -15.5 -2m-.5 -4v4h4 M4 13a8.1 8.1 0 0 0 15.5 2m.5 4v-4h-4");

    public static Geometry Clock { get; } = Create("M3 12a9 9 0 1 0 18 0a9 9 0 1 0 -18 0 M12 7v5l3 2");

    public static Geometry Search { get; } = Create("M3 10a7 7 0 1 0 14 0a7 7 0 1 0 -14 0 M21 21l-6 -6");

    public static Geometry Send { get; } = Create("M10 14l11 -11 M21 3l-6.5 18a.55 .55 0 0 1 -1 0l-3.5 -7l-7 -3.5a.55 .55 0 0 1 0 -1l18 -6.5");

    public static Geometry Power { get; } = Create("M7 6a7.75 7.75 0 1 0 10 0 M12 4l0 8");

    public static Geometry Play { get; } = Create("M7 4v16l13 -8l-13 -8");

    public static Geometry Star { get; } = Create("M12 17.75l-6.172 3.245l1.179 -6.873l-5 -4.867l6.9 -1l3.086 -6.253l3.086 6.253l6.9 1l-5 4.867l1.179 6.873l-6.158 -3.245");

    public static Geometry Link { get; } = Create("M9 15l6 -6 M11 6l.463 -.536a5 5 0 0 1 7.071 7.072l-.534 .464 M13 18l-.397 .534a5.068 5.068 0 0 1 -7.127 0a4.972 4.972 0 0 1 0 -7.071l.524 -.463");

    public static Geometry Bookmark { get; } = Create("M18 7v14l-6 -4l-6 4v-14a4 4 0 0 1 4 -4h4a4 4 0 0 1 4 4");

    public static Geometry Flag { get; } = Create("M5 5a5 5 0 0 1 7 0a5 5 0 0 0 7 0v9a5 5 0 0 1 -7 0a5 5 0 0 0 -7 0v-9 M5 21v-7");

    public static Geometry Pointer { get; } = Create("M8 13v-8.5a1.5 1.5 0 0 1 3 0v7.5 M11 11.5v-2a1.5 1.5 0 0 1 3 0v2.5 M14 10.5a1.5 1.5 0 0 1 3 0v1.5 M17 11.5a1.5 1.5 0 0 1 3 0v4.5a6 6 0 0 1 -6 6h-2h.208a6 6 0 0 1 -5.012 -2.7l-.196 -.3c-.312 -.479 -1.407 -2.388 -3.286 -5.728a1.5 1.5 0 0 1 .536 -2.022a1.867 1.867 0 0 1 2.28 .28l1.47 1.47 M5 3l-1 -1 M4 7h-1 M14 3l1 -1 M15 6h1");

    public static Geometry Folder { get; } = Create("M5 4h4l3 3h7a2 2 0 0 1 2 2v8a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-11a2 2 0 0 1 2 -2");

    public static Geometry FolderOpen { get; } = Create("M5 19l2.757 -7.351a1 1 0 0 1 .936 -.649h12.307a1 1 0 0 1 .986 1.164l-.996 5.211a2 2 0 0 1 -1.964 1.625h-14.026a2 2 0 0 1 -2 -2v-11a2 2 0 0 1 2 -2h4l3 3h7a2 2 0 0 1 2 2v2");

    public static Geometry FolderAdd { get; } = Create("M12 19h-7a2 2 0 0 1 -2 -2v-11a2 2 0 0 1 2 -2h4l3 3h7a2 2 0 0 1 2 2v3.5 M16 19h6 M19 16v6");

    public static Geometry Document { get; } = Create("M14 3v4a1 1 0 0 0 1 1h4 M17 21h-10a2 2 0 0 1 -2 -2v-14a2 2 0 0 1 2 -2h7l5 5v11a2 2 0 0 1 -2 2 M9 9l1 0 M9 13l6 0 M9 17l6 0");

    public static Geometry Documents { get; } = Create("M15 3v4a1 1 0 0 0 1 1h4 M18 17h-7a2 2 0 0 1 -2 -2v-10a2 2 0 0 1 2 -2h4l5 5v7a2 2 0 0 1 -2 2 M16 17v2a2 2 0 0 1 -2 2h-7a2 2 0 0 1 -2 -2v-10a2 2 0 0 1 2 -2h2");

    public static Geometry Book { get; } = Create("M3 19a9 9 0 0 1 9 0a9 9 0 0 1 9 0 M3 6a9 9 0 0 1 9 0a9 9 0 0 1 9 0 M3 6l0 13 M12 6l0 13 M21 6l0 13");

    public static Geometry Recipe { get; } = Create("M9 5h-2a2 2 0 0 0 -2 2v12a2 2 0 0 0 2 2h10a2 2 0 0 0 2 -2v-12a2 2 0 0 0 -2 -2h-2 M9 5a2 2 0 0 1 2 -2h2a2 2 0 0 1 2 2a2 2 0 0 1 -2 2h-2a2 2 0 0 1 -2 -2 M9 12l.01 0 M13 12l2 0 M9 16l.01 0 M13 16l2 0");

    public static Geometry Grid { get; } = Create("M4 5a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v4a1 1 0 0 1 -1 1h-4a1 1 0 0 1 -1 -1l0 -4 M14 5a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v4a1 1 0 0 1 -1 1h-4a1 1 0 0 1 -1 -1l0 -4 M4 15a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v4a1 1 0 0 1 -1 1h-4a1 1 0 0 1 -1 -1l0 -4 M14 15a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v4a1 1 0 0 1 -1 1h-4a1 1 0 0 1 -1 -1l0 -4");

    public static Geometry List { get; } = Create("M9 6l11 0 M9 12l11 0 M9 18l11 0 M5 6l0 .01 M5 12l0 .01 M5 18l0 .01");

    public static Geometry Apps { get; } = Create("M4 5a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v4a1 1 0 0 1 -1 1h-4a1 1 0 0 1 -1 -1l0 -4 M4 15a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v4a1 1 0 0 1 -1 1h-4a1 1 0 0 1 -1 -1l0 -4 M14 15a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v4a1 1 0 0 1 -1 1h-4a1 1 0 0 1 -1 -1l0 -4 M14 7l6 0 M17 4l0 6");

    public static Geometry AppWindow { get; } = Create("M3 7a2 2 0 0 1 2 -2h14a2 2 0 0 1 2 2v10a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-10 M6 8h.01 M9 8h.01");

    public static Geometry Layer { get; } = Create("M12 4l-8 4l8 4l8 -4l-8 -4 M4 12l8 4l8 -4 M4 16l8 4l8 -4");

    public static Geometry BorderOuter { get; } = Create("M4 6a2 2 0 0 1 2 -2h12a2 2 0 0 1 2 2v12a2 2 0 0 1 -2 2h-12a2 2 0 0 1 -2 -2l0 -12 M12 8l0 .01 M8 12l0 .01 M12 12l0 .01 M16 12l0 .01 M12 16l0 .01");

    public static Geometry Shape { get; } = Create("M3 5a2 2 0 1 0 4 0a2 2 0 1 0 -4 0 M17 5a2 2 0 1 0 4 0a2 2 0 1 0 -4 0 M3 19a2 2 0 1 0 4 0a2 2 0 1 0 -4 0 M17 19a2 2 0 1 0 4 0a2 2 0 1 0 -4 0 M5 7l0 10 M7 5l10 0 M7 19l10 0 M19 7l0 10");

    public static Geometry Typography { get; } = Create("M4 20l3 0 M14 20l7 0 M6.9 15l6.9 0 M10.2 6.3l5.8 13.7 M5 20l6 -16l2 0l7 16");

    public static Geometry Palette { get; } = Create("M12 21a9 9 0 0 1 0 -18c4.97 0 9 3.582 9 8c0 1.06 -.474 2.078 -1.318 2.828c-.844 .75 -1.989 1.172 -3.182 1.172h-2.5a2 2 0 0 0 -1 3.75a1.3 1.3 0 0 1 -1 2.25 M7.5 10.5a1 1 0 1 0 2 0a1 1 0 1 0 -2 0 M11.5 7.5a1 1 0 1 0 2 0a1 1 0 1 0 -2 0 M15.5 10.5a1 1 0 1 0 2 0a1 1 0 1 0 -2 0");

    public static Geometry Checkbox { get; } = Create("M9 11l3 3l8 -8 M20 12v6a2 2 0 0 1 -2 2h-12a2 2 0 0 1 -2 -2v-12a2 2 0 0 1 2 -2h9");

    public static Geometry Calculator { get; } = Create("M4 5a2 2 0 0 1 2 -2h12a2 2 0 0 1 2 2v14a2 2 0 0 1 -2 2h-12a2 2 0 0 1 -2 -2l0 -14 M8 8a1 1 0 0 1 1 -1h6a1 1 0 0 1 1 1v1a1 1 0 0 1 -1 1h-6a1 1 0 0 1 -1 -1l0 -1 M8 14l0 .01 M12 14l0 .01 M16 14l0 .01 M8 17l0 .01 M12 17l0 .01 M16 17l0 .01");

    public static Geometry Settings { get; } = Create("M10.325 4.317c.426 -1.756 2.924 -1.756 3.35 0a1.724 1.724 0 0 0 2.573 1.066c1.543 -.94 3.31 .826 2.37 2.37a1.724 1.724 0 0 0 1.065 2.572c1.756 .426 1.756 2.924 0 3.35a1.724 1.724 0 0 0 -1.066 2.573c.94 1.543 -.826 3.31 -2.37 2.37a1.724 1.724 0 0 0 -2.572 1.065c-.426 1.756 -2.924 1.756 -3.35 0a1.724 1.724 0 0 0 -2.573 -1.066c-1.543 .94 -3.31 -.826 -2.37 -2.37a1.724 1.724 0 0 0 -1.065 -2.572c-1.756 -.426 -1.756 -2.924 0 -3.35a1.724 1.724 0 0 0 1.066 -2.573c-.94 -1.543 .826 -3.31 2.37 -2.37c1 .608 2.296 .07 2.572 -1.065 M9 12a3 3 0 1 0 6 0a3 3 0 0 0 -6 0");

    public static Geometry Sliders { get; } = Create("M12 6a2 2 0 1 0 4 0a2 2 0 1 0 -4 0 M4 6l8 0 M16 6l4 0 M6 12a2 2 0 1 0 4 0a2 2 0 1 0 -4 0 M4 12l2 0 M10 12l10 0 M15 18a2 2 0 1 0 4 0a2 2 0 1 0 -4 0 M4 18l11 0 M19 18l1 0");

    public static Geometry Camera { get; } = Create("M5 7h1a2 2 0 0 0 2 -2a1 1 0 0 1 1 -1h6a1 1 0 0 1 1 1a2 2 0 0 0 2 2h1a2 2 0 0 1 2 2v9a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-9a2 2 0 0 1 2 -2 M9 13a3 3 0 1 0 6 0a3 3 0 0 0 -6 0");

    public static Geometry CameraSnap { get; } = Create("M13 20h-8a2 2 0 0 1 -2 -2v-9a2 2 0 0 1 2 -2h1a2 2 0 0 0 2 -2a1 1 0 0 1 1 -1h6a1 1 0 0 1 1 1a2 2 0 0 0 2 2h1a2 2 0 0 1 2 2v3.5 M9 13a3 3 0 1 0 6 0a3 3 0 0 0 -6 0 M19 16l-2 3h4l-2 3");

    public static Geometry Image { get; } = Create("M15 8h.01 M3 6a3 3 0 0 1 3 -3h12a3 3 0 0 1 3 3v12a3 3 0 0 1 -3 3h-12a3 3 0 0 1 -3 -3v-12 M3 16l5 -5c.928 -.893 2.072 -.893 3 0l5 5 M14 14l1 -1c.928 -.893 2.072 -.893 3 0l3 3");

    public static Geometry Images { get; } = Create("M7 5.667a2.667 2.667 0 0 1 2.667 -2.667h8.666a2.667 2.667 0 0 1 2.667 2.667v8.666a2.667 2.667 0 0 1 -2.667 2.667h-8.666a2.667 2.667 0 0 1 -2.667 -2.667l0 -8.666 M4.012 7.26a2.005 2.005 0 0 0 -1.012 1.737v10c0 1.1 .9 2 2 2h10c.75 0 1.158 -.385 1.5 -1 M17 7h.01 M7 13l3.644 -3.644a1.21 1.21 0 0 1 1.712 0l3.644 3.644 M15 12l1.644 -1.644a1.21 1.21 0 0 1 1.712 0l2.644 2.644");

    public static Geometry Eye { get; } = Create("M10 12a2 2 0 1 0 4 0a2 2 0 0 0 -4 0 M21 12c-2.4 4 -5.4 6 -9 6c-3.6 0 -6.6 -2 -9 -6c2.4 -4 5.4 -6 9 -6c3.6 0 6.6 2 9 6");

    public static Geometry Scan { get; } = Create("M5 12h14 M3 7v-2a2 2 0 0 1 2 -2h2 M3 17v2a2 2 0 0 0 2 2h2 M17 3h2a2 2 0 0 1 2 2v2 M17 21h2a2 2 0 0 0 2 -2v-2");

    public static Geometry Inspect { get; } = Create("M8 11a3 3 0 1 0 6 0a3 3 0 0 0 -6 0 M16 16l-2.5 -2.5 M3 7v-2a2 2 0 0 1 2 -2h2 M3 17v2a2 2 0 0 0 2 2h2 M17 3h2a2 2 0 0 1 2 2v2 M17 21h2a2 2 0 0 0 2 -2v-2");

    public static Geometry Target { get; } = Create("M11 12a1 1 0 1 0 2 0a1 1 0 1 0 -2 0 M7 12a5 5 0 1 0 10 0a5 5 0 1 0 -10 0 M3 12a9 9 0 1 0 18 0a9 9 0 1 0 -18 0");

    public static Geometry Calibrate { get; } = Create("M11 12a1 1 0 1 0 2 0a1 1 0 1 0 -2 0 M4 8v-2a2 2 0 0 1 2 -2h2 M4 16v2a2 2 0 0 0 2 2h2 M16 4h2a2 2 0 0 1 2 2v2 M16 20h2a2 2 0 0 0 2 -2v-2");

    public static Geometry Compass { get; } = Create("M8 16l2 -6l6 -2l-2 6l-6 2 M3 12a9 9 0 1 0 18 0a9 9 0 1 0 -18 0 M12 3l0 2 M12 19l0 2 M3 12l2 0 M19 12l2 0");

    public static Geometry Terrain { get; } = Create("M3 20h18l-6.921 -14.612a2.3 2.3 0 0 0 -4.158 0l-6.921 14.612 M7.5 11l2 2.5l2.5 -2.5l2 3l2.5 -2");

    public static Geometry Lamp { get; } = Create("M3 12h1m8 -9v1m8 8h1m-15.4 -6.4l.7 .7m12.1 -.7l-.7 .7 M9 16a5 5 0 1 1 6 0a3.5 3.5 0 0 0 -1 3a2 2 0 0 1 -4 0a3.5 3.5 0 0 0 -1 -3 M9.7 17l4.6 0");

    public static Geometry Bolt { get; } = Create("M13 3l0 7l6 0l-8 11l0 -7l-6 0l8 -11");

    public static Geometry Robot { get; } = Create("M6 6a2 2 0 0 1 2 -2h8a2 2 0 0 1 2 2v4a2 2 0 0 1 -2 2h-8a2 2 0 0 1 -2 -2l0 -4 M12 2v2 M9 12v9 M15 12v9 M5 16l4 -2 M15 14l4 2 M9 18h6 M10 8v.01 M14 8v.01");

    public static Geometry Brain { get; } = Create("M15.5 13a3.5 3.5 0 0 0 -3.5 3.5v1a3.5 3.5 0 0 0 7 0v-1.8 M8.5 13a3.5 3.5 0 0 1 3.5 3.5v1a3.5 3.5 0 0 1 -7 0v-1.8 M17.5 16a3.5 3.5 0 0 0 0 -7h-.5 M19 9.3v-2.8a3.5 3.5 0 0 0 -7 0 M6.5 16a3.5 3.5 0 0 1 0 -7h.5 M5 9.3v-2.8a3.5 3.5 0 0 1 7 0v10");

    public static Geometry Sparkles { get; } = Create("M16 18a2 2 0 0 1 2 2a2 2 0 0 1 2 -2a2 2 0 0 1 -2 -2a2 2 0 0 1 -2 2m0 -12a2 2 0 0 1 2 2a2 2 0 0 1 2 -2a2 2 0 0 1 -2 -2a2 2 0 0 1 -2 2m-7 12a6 6 0 0 1 6 -6a6 6 0 0 1 -6 -6a6 6 0 0 1 -6 6a6 6 0 0 1 6 6");

    public static Geometry Cube { get; } = Create("M21 16.008v-8.018a1.98 1.98 0 0 0 -1 -1.717l-7 -4.008a2.016 2.016 0 0 0 -2 0l-7 4.008c-.619 .355 -1 1.01 -1 1.718v8.018c0 .709 .381 1.363 1 1.717l7 4.008a2.016 2.016 0 0 0 2 0l7 -4.008c.619 -.355 1 -1.01 1 -1.718 M12 22v-10 M12 12l8.73 -5.04 M3.27 6.96l8.73 5.04");

    public static Geometry Message { get; } = Create("M3 20l1.3 -3.9c-2.324 -3.437 -1.426 -7.872 2.1 -10.374c3.526 -2.501 8.59 -2.296 11.845 .48c3.255 2.777 3.695 7.266 1.029 10.501c-2.666 3.235 -7.615 4.215 -11.574 2.293l-4.7 1");

    public static Geometry Wafer { get; } = Create("M6.343 19a9 9 0 1 1 11.314 0z M8.5 8h7v7h-7z M12 8v7 M8.5 11.5h7");

    public static Geometry Tray { get; } = Create("M8 6v-1a1 1 0 0 1 1 -1h6a1 1 0 0 1 1 1v1 M4 6h16a1 1 0 0 1 1 1v11a1 1 0 0 1 -1 1h-16a1 1 0 0 1 -1 -1v-11a1 1 0 0 1 1 -1z M3 12.5h18 M9 6v13 M15 6v13");

    public static Geometry Nozzle { get; } = Create("M8 3h8v3h-8z M10 6v6l-2 3v2h8v-2l-2 -3v-6 M9 21h6");

    public static IReadOnlyDictionary<string, Geometry> All { get; } = new Dictionary<string, Geometry>
    {
        ["Info"] = Info,
        ["Success"] = Success,
        ["Warning"] = Warning,
        ["Error"] = Error,
        ["Help"] = Help,
        ["Check"] = Check,
        ["Close"] = Close,
        ["ChevronDown"] = ChevronDown,
        ["ChevronUp"] = ChevronUp,
        ["ChevronLeft"] = ChevronLeft,
        ["ChevronRight"] = ChevronRight,
        ["ArrowUp"] = ArrowUp,
        ["ArrowDown"] = ArrowDown,
        ["ArrowRight"] = ArrowRight,
        ["Menu"] = Menu,
        ["More"] = More,
        ["Home"] = Home,
        ["WindowMinimize"] = WindowMinimize,
        ["WindowMaximize"] = WindowMaximize,
        ["WindowRestore"] = WindowRestore,
        ["Add"] = Add,
        ["Delete"] = Delete,
        ["Copy"] = Copy,
        ["Edit"] = Edit,
        ["Save"] = Save,
        ["Refresh"] = Refresh,
        ["Search"] = Search,
        ["Send"] = Send,
        ["Power"] = Power,
        ["Play"] = Play,
        ["Star"] = Star,
        ["Link"] = Link,
        ["Bookmark"] = Bookmark,
        ["Flag"] = Flag,
        ["Pointer"] = Pointer,
        ["Folder"] = Folder,
        ["FolderOpen"] = FolderOpen,
        ["FolderAdd"] = FolderAdd,
        ["Document"] = Document,
        ["Documents"] = Documents,
        ["Book"] = Book,
        ["Recipe"] = Recipe,
        ["Grid"] = Grid,
        ["List"] = List,
        ["Apps"] = Apps,
        ["AppWindow"] = AppWindow,
        ["Layer"] = Layer,
        ["BorderOuter"] = BorderOuter,
        ["Shape"] = Shape,
        ["Typography"] = Typography,
        ["Palette"] = Palette,
        ["Checkbox"] = Checkbox,
        ["Calculator"] = Calculator,
        ["Settings"] = Settings,
        ["Sliders"] = Sliders,
        ["Camera"] = Camera,
        ["CameraSnap"] = CameraSnap,
        ["Image"] = Image,
        ["Images"] = Images,
        ["Eye"] = Eye,
        ["Scan"] = Scan,
        ["Inspect"] = Inspect,
        ["Target"] = Target,
        ["Calibrate"] = Calibrate,
        ["Compass"] = Compass,
        ["Terrain"] = Terrain,
        ["Lamp"] = Lamp,
        ["Bolt"] = Bolt,
        ["Robot"] = Robot,
        ["Brain"] = Brain,
        ["Sparkles"] = Sparkles,
        ["Cube"] = Cube,
        ["Message"] = Message,
        ["Wafer"] = Wafer,
        ["Tray"] = Tray,
        ["Nozzle"] = Nozzle,
    };

    private static Geometry Create(string data)
    {
        Geometry geometry = Geometry.Parse(data);
        return geometry;
    }
}
